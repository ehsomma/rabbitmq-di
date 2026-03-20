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
/// - Crear un channel por aggregate/queue.
/// - Declarar la infraestructura RabbitMQ correspondiente.
/// - Escuchar mensajes de todas las queues generadas para el microservicio.
/// - Resolver el handler correcto según el tipo del mensaje.
/// - Hacer ACK si se procesa correctamente.
/// - Hacer NACK + dead-lettering si falla.
/// </remarks>
public sealed class RabbitMqConsumerWorker : BackgroundService
{
    private readonly RabbitOptions _opt;
    private readonly IEnumerable<IIntegrationMessageHandler> _handlers;
    private readonly IntegrationEventTypeResolver _typeResolver;
    private readonly IntegrationEventDispatcher _dispatcher;
    private readonly IConnection _connection;
    private readonly IConsumerTopologyBuilder _topologyBuilder;
    private readonly RabbitMqTopologyInitializer _topologyInitializer;
    private readonly ILogger<RabbitMqConsumerWorker> _logger;

    private readonly List<IChannel> _channels = [];

    /// <summary>
    /// Inicializa una nueva instancia del worker genérico de RabbitMQ.
    /// </summary>
    public RabbitMqConsumerWorker(
        RabbitOptions opt,
        IEnumerable<IIntegrationMessageHandler> handlers,
        IntegrationEventTypeResolver typeResolver,
        IntegrationEventDispatcher dispatcher,
        IConnection connection,
        IConsumerTopologyBuilder topologyBuilder,
        RabbitMqTopologyInitializer topologyInitializer,
        ILogger<RabbitMqConsumerWorker> logger)
    {
        _opt = opt;
        _handlers = handlers;
        _typeResolver = typeResolver;
        _dispatcher = dispatcher;
        _connection = connection;
        _topologyBuilder = topologyBuilder;
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
        IReadOnlyCollection<AggregateQueueDefinition> definitions =
            _topologyBuilder.Build(_opt.ServiceName, _handlers);

        foreach (AggregateQueueDefinition definition in definitions)
        {
            // =====================================================
            // Crea un channel propio para esta queue.
            // =====================================================
            //
            // [!] IMPORTANTE:
            // IConnection es thread-safe.
            // IChannel NO es thread-safe.
            //
            // Por eso el patrón recomendado es:
            // - 1 Connection por proceso
            // - 1 Channel por queue / consumer

            IChannel channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
            _channels.Add(channel);

            // Declara la infraestructura RabbitMQ necesaria para esta queue.
            await _topologyInitializer.InitializeAsync(channel, definition, _opt.PrefetchCount, stoppingToken);

            // =====================================================
            // Crea el consumer AMQP real.
            // =====================================================

            // Crea un consumer Consumer + Handler de recepción.
            // Es el que recibe mensajes desde la queue a través del channel.
            AsyncEventingBasicConsumer rabbitConsumer = new AsyncEventingBasicConsumer(channel);

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
                    // Aunque normalmente esperamos que el producer envíe propiedades,
                    // por seguridad validamos el null.
                    if (ea.BasicProperties is null)
                    {
                        _logger.LogWarning(
                            "[{ServiceName}] Message without properties -> reject",
                            _opt.ServiceName);

                        await channel.BasicNackAsync(
                            ea.DeliveryTag,
                            multiple: false,
                            requeue: false,
                            cancellationToken: stoppingToken);

                        return;
                    }

                    // Obtiene el tipo del mensaje, ej: "PersonCreatedIntegrationEvent".
                    string? messageTypeName = ea.BasicProperties.Type;

                    // Convierte el string del transporte al Type CLR real.
                    if (!_typeResolver.TryResolve(messageTypeName, out Type? eventType))
                    {
                        _logger.LogWarning(
                            "[{ServiceName}] Unknown event type name: '{MessageTypeName}' -> reject",
                            _opt.ServiceName,
                            messageTypeName ?? "(null)");

                        await channel.BasicNackAsync(
                            ea.DeliveryTag,
                            multiple: false,
                            requeue: false,
                            cancellationToken: stoppingToken);

                        return;
                    }

                    // Resuelve el handler para el tipo CLR del evento.
                    // Si no hay handler registrado para ese tipo de mensaje, se rechaza.
                    if (!_dispatcher.TryResolve(eventType, out IIntegrationMessageHandler? handler))
                    {
                        _logger.LogWarning("[{ServiceName}] No handler registered for CLR type: '{EventType}' -> reject", _opt.ServiceName, eventType?.FullName);
                        await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                        return;
                    }

                    // Ejecuta el handler:
                    // - IntegrationEventHandlerBase<T> deserializa bytes -> JSON -> TEvent
                    // - luego invoca el handler tipado concreto
                    await handler.HandleAsync(ea.Body, ea.BasicProperties, stoppingToken);

                    // ACK: confirma al broker que el mensaje fue procesado correctamente.
                    await channel.BasicAckAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        cancellationToken: stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Si el Host está apagando el proceso, evitamos ACK/NACK acá.
                    // El cierre ordenado se hace fuera de este callback.
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[{ServiceName}] Error processing message on queue {QueueName}",
                        _opt.ServiceName,
                        definition.QueueName);

                    // NACK = Not ACK (mensaje no procesado).
                    // [!] Si `requeue: true`, vuelve a la cola inmediatamente pero si sigue fallando, hace un loop infinito.
                    // [!] Si `requeue: false` Si la cola tiene DLX/DLQ configurada, dispara el dead-lettering.
                    await channel.BasicNackAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        requeue: false,
                        cancellationToken: stoppingToken);
                }
            };

            // =====================================================
            // Suscribe el consumer a la queue.
            // =====================================================
            //
            // autoAck = false => ACK manual.
            // NOTE: A partir de esta línea, RabbitMQ ya puede empezar a entregar mensajes.
            await channel.BasicConsumeAsync(
                queue: definition.QueueName,
                autoAck: false,
                consumer: rabbitConsumer,
                cancellationToken: stoppingToken);

            // Mensaje en la consola para indicar que el worker está listo y escuchando.
            _logger.LogInformation(
                "[{ServiceName}] Listening queue {QueueName} on exchange {ExchangeName}",
                _opt.ServiceName,
                definition.QueueName,
                definition.ExchangeName);
        }

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

        foreach (IChannel channel in _channels)
        {
            await channel.CloseAsync(cancellationToken);
            await channel.DisposeAsync();
        }

        _channels.Clear();

        await base.StopAsync(cancellationToken);
    }
}
