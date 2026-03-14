using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQInterfaces;
using System.Data.Common;
using System.Runtime.ConstrainedExecution;

namespace EmailWorkerService;

/// <summary>
/// Worker encargado de consumir eventos de integración desde RabbitMQ para el 
/// microservicio EmailService.
/// </summary>
/// <remarks>
/// Responsabilidades:
/// - Declara la infraestructura RabbitMQ del microservicio (DLX, DLQ, queue principal, bind).
/// - Escucha mensajes de la queue configurada.
/// - Resuelve el handler correcto según el tipo del mensaje.
/// - Hace ACK si se procesa correctamente.
/// - Hace NACK + dead-lettering si falla.
/// </remarks>
public sealed class EmailWorker : BackgroundService
{
    private readonly RabbitOptions _opt;
    private readonly IntegrationEventDispatcher _dispatcher;
    private readonly IConnection _connection;
    //private readonly IChannel _channel;
    private IChannel? _channel;
    private readonly ILogger<EmailWorker> _logger;
    private readonly IntegrationEventTypeResolver _typeResolver;

    /// <summary>
    /// Inicializa una nueva instancia de <see cref="EmailWorker"/>.
    /// </summary>
    public EmailWorker(
        RabbitOptions opt,
        IntegrationEventDispatcher dispatcher,
        //IChannel channel,
        IConnection connection,
        IntegrationEventTypeResolver typeResolver,
        ILogger<EmailWorker> logger)
    {
        _opt = opt;
        _dispatcher = dispatcher;
        //_channel = channel;
        _connection = connection;
        _typeResolver = typeResolver;
        _logger = logger;

    }

    /// <summary>
    /// Punto de entrada principal del worker.
    /// El Host llama a este método al iniciar la aplicación.
    /// </summary>
    /// <param name="stoppingToken">Token de cancelación controlado por el Host.Se activa cuando la app se está cerrando.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await _connection.CreateChannelAsync();

        /*
        Sobre DLQ (Dead Letter Queue):
        =============================
        Cuando el consumer captura una excepción lanzada por el *EventHandler y no puede procesar un mensaje, 
        hay 4 caminos típicos:
            A. Reintentar un número limitado de veces.
            B. Si sigue fallando (o si falla 1 sola vez) → mandarlo a DLQ (Dead Letter Queue) para análisis/manual/reproceso.
            C. Descartar (solo si el mensaje es realmente descartable).
            D. No usar DLQ y loggear el error (solo si el mensaje es realmente descartable).
        
        Un mensaje va a DLQ cuando (según config):
            • Se rechaza con BasicReject / BasicNack con requeue:false.
            • Expira por TTL.
            • Se supera el max length de la cola (si configuraste límites).

        Un DLQ por microservicio (ej: este es un microservicio de envío de emials):
            Creo que tener una DLQ por microservicio es la mejor opción para evitar mezclar mensajes de distintos 
            servicios y facilitar el análisis. En este ejemplo, el EmailService tiene su propia 
            DLQ (email.person.integration.dlq) y su propio DLX (email.person.integration.dlx) para reenviar 
            los mensajes rechazados.
        */
        // Declara DLX y DLQ (Dead Letter).
        //
        // DLX: Exchange donde RabbitMQ re-publica mensajes "dead-lettered".
        await _channel.ExchangeDeclareAsync(
            exchange: _opt.DlxName,
            type: ExchangeType.Direct, // Direct es suficiente para DLQ.
            durable: true,
            autoDelete: false,
            arguments: null);

        // DLQ: Cola que recibirá los mensajes rechazados / fallidos.
        await _channel.QueueDeclareAsync(
            queue: _opt.DlqName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Bind DLQ al DLX con una routing key (en Direct es obligatorio que coincida).
        await _channel.QueueBindAsync(
            queue: _opt.DlqName,
            exchange: _opt.DlxName,
            routingKey: _opt.DlqRoutingKey);

        // Define los argumentos de la DLX para luego asignarlos a la queue principal.
        Dictionary<string, object?> mainQueueArgs = new Dictionary<string, object?>
        {
            // Exchange al que RabbitMQ enviará el mensaje cuando se rechace.
            ["x-dead-letter-exchange"] = _opt.DlxName,

            // Con qué routing key se re-publica al DLX (para que matchee el bind de la DLQ).
            ["x-dead-letter-routing-key"] = _opt.DlqRoutingKey,
        };

        // Declara exchange + queue + bind (infra del servicio).
        // [!] IMPORTANTE: declare debe ser CONSISTENTE en todos los servicios (si ya existe en otros consumers y producers,
        // debe coincidir) o RabbitMQ lanza PRECONDITION_FAILED.
        //
        // 1) Exchange principal de eventos de integración.
        // Exchange: Es el "distribuidor", recibe mensajes del producer y decide a qué cola(s) enviarlos según
        // reglas (tipo Direct, Topic, Fanout, etc.). 
        await _channel.ExchangeDeclareAsync(
            _opt.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

        // 2) Queue principal del microservicio.
        // Queue: Es donde se almacenan los mensajes hasta que un consumer los procesa.
        await _channel.QueueDeclareAsync(
            queue: _opt.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: mainQueueArgs);

        // 3) Bind.
        // Bind: Es la regla que conecta el exchage con la queue
        await _channel.QueueBindAsync(
            _opt.QueueName,
            _opt.ExchangeName,
            _opt.BindingKey);

        // Configura el QoS del chanel (Quality of Service).
        //
        // QoS controla cuántos mensajes RabbitMQ entrega "en vuelo" a este consumer antes de recibir ACKs.
        // ACK = Acknowledgement (confirmación). Es el mensaje que el consumer envía al broker para
        //       decir: "Ya procesé este mensaje correctamente.".
        //
        // - prefetchCount = 10 => hasta 10 mensajes sin ACK al mismo tiempo.
        // - Como autoAck = false, el mensaje se considera pendiente hasta ACK/NACK.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false);

        // Crea un consumer Consumer + Handler de recepción.
        // Es el que recibe mensajes desde la queue a través del channel.
        AsyncEventingBasicConsumer rabbitConsumer = new AsyncEventingBasicConsumer(_channel);

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
                //    _logger.LogWarning("[EmailService] Unknown message type: '{MessageType}' -> reject", type ?? "(null)");
                //    await _channel.BasicNackAsync(ea.DeliveryTag,multiple: false, requeue: false);
                //    return;
                //}

                // Obtiene el nombre del tipo del mensaje, ej: "PersonCreatedIntegrationEvent".
                string? messageTypeName = ea.BasicProperties?.Type;

                // Intenta resolver el tipo CLR de un evento a partir del string recibido por RabbitMQ.
                if (!_typeResolver.TryResolve(messageTypeName, out Type? eventType))
                {
                    _logger.LogWarning(
                        "[EmailService] Unknown event type name: '{MessageTypeName}' -> reject",
                        messageTypeName ?? "(null)");

                    await _channel.BasicNackAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        requeue: false);

                    return;
                }

                // Si no hay handler registrado para ese tipo de mensaje, se rechaza.
                if (!_dispatcher.TryResolve(eventType, out IIntegrationMessageHandler? handler))
                {
                    _logger.LogWarning("[EmailService] No handler registered for event CLR type: '{EventType}' -> reject", eventType?.FullName);
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                // Aunque normalmente esperamos que el producer envíe propiedades,
                // por seguridad validamos el null.
                if (ea.BasicProperties is null)
                {
                    _logger.LogWarning("[EmailService] Message without properties -> reject");
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    return;
                }

                // Ejecuta el handler:
                // - IntegrationEventHandlerBase<T> deserializa bytes -> JSON -> TEvent
                // - luego invoca el handler tipado concreto
                await handler.HandleAsync(ea.Body, ea.BasicProperties, stoppingToken);

                // ACK: confirma al broker que el mensaje fue procesado correctamente.
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (OperationCanceledException)
            {
                // Si el Host está apagando el proceso, evitamos ACK/NACK acá.
                // El cierre ordenado se hace fuera de este callback.
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailService] Error processing message");

                // NACK = Not ACK (mensaje no procesado).
                // [!] Si `requeue: true`, vuelve a la cola inmediatamente pero si sigue fallando, hace un loop infinito.
                // [!] Si `requeue: false` Si la cola tiene DLX/DLQ configurada, dispara el dead-lettering.
                ////await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true); // Loop infinito si sigue fallando.
                await _channel.BasicNackAsync(
                    ea.DeliveryTag,
                    multiple: false,
                    requeue: false); // Dispara el dead-lettering hacia la DLQ (por el DLX configurado).
            }
        };

        // Suscripción del consumer a la queue.
        // autoAck = false => ACK manual.
        // NOTE: A partir de esta línea, RabbitMQ puede empezar a entregar mensajes.
        await _channel.BasicConsumeAsync(
            queue: _opt.QueueName,
            autoAck: false,
            consumer: rabbitConsumer);

        // Mantiene el worker vivo hasta que el Host solicite cancelación.
        //
        // Mensaje en la consola para indicar que el worker está listo y escuchando.
        _logger.LogInformation("[EmailService] Listening queue {QueueName}. Press Ctrl+C to exit.", _opt.QueueName);
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
        _logger.LogInformation("[EmailService] Shutting down...");

        //await _channel.CloseAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
