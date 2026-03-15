using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Core;

namespace RabbitMQ.Hosting;

/// <summary>
/// Worker genérico encargado de consumir eventos de integración desde RabbitMQ.
/// </summary>
/// <remarks>
/// Responsabilidades:
/// - Crear su propio channel a partir de una conexión compartida.
/// - Declarar la infraestructura RabbitMQ del microservicio (DLX, DLQ, queue principal, bind).
/// - Escuchar mensajes de la queue configurada.
/// - Resolver el handler correcto según el tipo del mensaje.
/// - Hacer ACK si se procesa correctamente.
/// - Hacer NACK + dead-lettering si falla.
/// </remarks>
public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private readonly RabbitOptions _opt;
    private readonly IntegrationEventTypeResolver _typeResolver;
    private readonly IntegrationEventDispatcher _dispatcher;
    private readonly IConnection _connection;
    private readonly RabbitMqTopologyInitializer _topologyInitializer;
    private readonly ILogger<RabbitMqConsumerWorker> _logger;

    private IChannel? _channel;

    /// <summary>
    /// Inicializa una nueva instancia del worker genérico de RabbitMQ.
    /// </summary>
    public RabbitMqConsumerWorker(
        RabbitOptions opt,
        IntegrationEventTypeResolver typeResolver,
        IntegrationEventDispatcher dispatcher,
        IConnection connection,
        RabbitMqTopologyInitializer topologyInitializer,
        ILogger<RabbitMqConsumerWorker> logger)
    {
        _opt = opt;
        _typeResolver = typeResolver;
        _dispatcher = dispatcher;
        _connection = connection;
        _topologyInitializer = topologyInitializer;
        _logger = logger;
    }

    /// <summary>
    /// Punto de entrada principal del worker.
    /// El Host llama a este método al iniciar la aplicación.
    /// </summary>
    /// <param name="stoppingToken">Token de cancelación controlado por el Host.Se activa cuando la app se está cerrando.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // =====================================================
        // Crea un channel propio para este Worker.
        // =====================================================
        //
        // [!] IMPORTANTE:
        // IConnection es thread-safe.
        // IChannel NO es thread-safe.
        //
        // Por eso el patrón recomendado es el siguiente y crea el chanel aquí:
        // - 1 Connection por proceso.
        // - 1 Channel por Worker / consumer.
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declara toda la infraestructura RabbitMQ necesaria para el consumer.
        await _topologyInitializer.InitializeAsync(_channel, _opt, stoppingToken);

        // =====================================================
        // Crea el consumer AMQP real.
        // =====================================================

        // Crea un consumer Consumer + Handler de recepción.
        // Es el que recibe mensajes desde la queue a través del channel.
        AsyncEventingBasicConsumer rabbitConsumer = new AsyncEventingBasicConsumer(_channel);

        // =====================================================
        // Handler del evento que se ejecuta al llegar un mensaje.
        // =====================================================
        //
        // Declara el handler para el evento que se ejecuta cuando llega un mensaje y se suscribe.
        // NOTA: += es la forma de C# para agregar el handler (suscribirse) a un evento.
        //
        // ea (BasicDeliverEventArgs) contiene la metadata del mensaje:
        //  - Body (payload).
        //  - BasicProperties (props.Type, MessageId, CorrelationId, headers, etc.).
        //  - DeliveryTag (id interno para ACK/NACK).
        //  - Exchange / RoutingKey (info del delivery).
        rabbitConsumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                // MODI: Antes usábamos el string MessageType, ahora usamos el Type HandledEventType.
                //// Obtiene el tipo del mensaje, ej: "PersonCreatedIntegrationEvent".
                //string? type = ea.BasicProperties?.Type;

                //// Si no hay handler registrado para ese tipo de mensaje, se rechaza.
                //if (!_dispatcher.TryResolve(type, out IIntegrationMessageHandler? handler))
                //{
                //    _logger.LogWarning("[WhatsAppService] Unknown message type: '{MessageType}' -> reject", type ?? "(null)");
                //    await _channel.BasicNackAsync(ea.DeliveryTag,multiple: false, requeue: false);
                //    return;
                //}

                // Aunque normalmente esperamos que el producer envíe propiedades,
                // por seguridad validamos el null.
                if (ea.BasicProperties is null)
                {
                    _logger.LogWarning("[{ServiceName}] Message without properties -> reject", _opt.ServiceName);
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                // Obtiene el nombre del tipo del mensaje, ej: "PersonCreatedIntegrationEvent".
                string? messageTypeName = ea.BasicProperties.Type;

                // Intenta resolver el tipo CLR de un evento a partir del string recibido por RabbitMQ.
                if (!_typeResolver.TryResolve(messageTypeName, out Type? eventType))
                {
                    _logger.LogWarning("[{ServiceName}] Unknown event type name: '{MessageTypeName}' -> reject", _opt.ServiceName, messageTypeName ?? "(null)");
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                // Resuelve el handler para el tipo CLR del evento.
                // Si no hay handler registrado para ese tipo de mensaje, se rechaza.
                if (!_dispatcher.TryResolve(eventType, out IIntegrationMessageHandler? handler))
                {
                    _logger.LogWarning("[{ServiceName}] No handler registered for CLR type: '{EventType}' -> reject", _opt.ServiceName, eventType?.FullName);
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                    return;
                }

                // Ejecuta el handler:
                // - IntegrationEventHandlerBase<T> deserializa bytes -> JSON -> TEvent
                // - luego invoca el handler tipado concreto
                await handler.HandleAsync(ea.Body, ea.BasicProperties, stoppingToken);

                // ACK: confirma al broker que el mensaje fue procesado correctamente.
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Si el Host está apagando el proceso, evitamos ACK/NACK acá.
                // El cierre ordenado se hace fuera de este callback.
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{ServiceName}] Error processing message", _opt.ServiceName);

                // NACK = Not ACK (mensaje no procesado).
                // [!] Si `requeue: true`, vuelve a la cola inmediatamente pero si sigue fallando, hace un loop infinito.
                // [!] Si `requeue: false` Si la cola tiene DLX/DLQ configurada, dispara el dead-lettering.
                await _channel.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: false, // Dispara el dead-lettering hacia la DLQ (por el DLX configurado).
                    cancellationToken: stoppingToken);
            }
        };

        // =====================================================
        // Suscribe el consumer a la queue.
        // =====================================================
        //
        // autoAck = false => ACK manual.
        // NOTE: A partir de esta línea, RabbitMQ ya puede empezar a entregar mensajes.
        await _channel.BasicConsumeAsync(
            queue: _opt.QueueName,
            autoAck: false,
            consumer: rabbitConsumer,
            cancellationToken: stoppingToken);

        // Mensaje en la consola para indicar que el worker está listo y escuchando.
        _logger.LogInformation(
            "[{ServiceName}] Listening queue {QueueName}. Press Ctrl+C to exit.",
            _opt.ServiceName,
            _opt.QueueName);

        // =====================================================
        // Mantiene el worker vivo hasta que el Host solicite cancelación.
        // =====================================================
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown solicitado por el Host.
        }
    }

    /// <summary>
    /// Ejecuta lógica de cierre ordenado antes de detener el worker.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{ServiceName}] Shutting down...", _opt.ServiceName);

        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
