using Consumer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQInterfaces;

namespace EmailServiceConsumer
{
    internal class EmailConsumerWorker : BackgroundService
    {
        private readonly RabbitOptions _opt;
        private readonly IntegrationEventDispatcher _dispatcher;
        private readonly IChannel _channel;
        private readonly ILogger<EmailConsumerWorker> _logger;
        private readonly IntegrationEventTypeResolver _typeResolver;

        public EmailConsumerWorker(
            RabbitOptions opt,
            IntegrationEventDispatcher dispatcher,
            IChannel channel,
            IntegrationEventTypeResolver typeResolver,
            ILogger<EmailConsumerWorker> logger)
        {
            _opt = opt;
            _dispatcher = dispatcher;
            _channel = channel;
            _typeResolver = typeResolver;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _channel.ExchangeDeclareAsync(
                _opt.ExchangeName,
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);

            await _channel.QueueDeclareAsync(
                queue: _opt.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            await _channel.QueueBindAsync(
                _opt.QueueName,
                _opt.ExchangeName,
                _opt.BindingKey);

            await _channel.BasicQosAsync(0, 10, false);

            AsyncEventingBasicConsumer rabbitConsumer = new AsyncEventingBasicConsumer(_channel);

            rabbitConsumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    // Obtiene el nombre del tipo del mensaje, ej: "PersonCreatedIntegrationEvent".
                    string? messageTypeName = ea.BasicProperties?.Type;

                    // Intenta resolver el tipo CLR de un evento a partir del string recibido por RabbitMQ.
                    if (!_typeResolver.TryResolve(messageTypeName, out Type? eventType))
                    {
                        _logger.LogWarning(
                            "[EmailAppService] Unknown event type name: '{MessageTypeName}' -> reject",
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
                        _logger.LogWarning("[EmailAppService] No handler registered for event CLR type: '{EventType}' -> reject", eventType?.FullName);
                        await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                        return;
                    }

                    // Aunque normalmente esperamos que el producer envíe propiedades,
                    // por seguridad validamos el null.
                    if (ea.BasicProperties is null)
                    {
                        _logger.LogWarning("[EmailAppService] Message without properties -> reject");
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
                    _logger.LogError(ex, "[WhatsAppService] Error processing message");

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

            await _channel.BasicConsumeAsync(
                queue: _opt.QueueName,
                autoAck: false,
                consumer: rabbitConsumer);

            _logger.LogInformation("Listening queue {queue}", _opt.QueueName);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _channel.CloseAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}
