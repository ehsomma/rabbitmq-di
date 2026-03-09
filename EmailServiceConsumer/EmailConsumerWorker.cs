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

        public EmailConsumerWorker(
            RabbitOptions opt,
            IntegrationEventDispatcher dispatcher,
            IChannel channel,
            ILogger<EmailConsumerWorker> logger)
        {
            _opt = opt;
            _dispatcher = dispatcher;
            _channel = channel;
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
                    if (ea.BasicProperties is null)
                    {
                        await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
                        return;
                    }

                    string? type = ea.BasicProperties.Type;

                    if (!_dispatcher.TryResolve(type, out IIntegrationMessageHandler? handler))
                    {
                        _logger.LogWarning("Unknown message type: {type}", type);

                        await _channel.BasicNackAsync(
                            ea.DeliveryTag,
                            multiple: false,
                            requeue: false);

                        return;
                    }

                    await handler.HandleAsync(ea.Body, ea.BasicProperties, stoppingToken);

                    await _channel.BasicAckAsync(ea.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message");

                    await _channel.BasicNackAsync(
                        ea.DeliveryTag,
                        multiple: false,
                        requeue: false);
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
