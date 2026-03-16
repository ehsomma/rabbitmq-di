using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Core;

namespace RabbitMQ.Hosting;

/// <summary>
/// Implementación de <see cref="IIntegrationEventPublisher"/> que publica
/// eventos de integración en RabbitMQ.
/// </summary>
/// <remarks>
/// Esta implementación:
/// - reutiliza una conexión compartida
/// - crea un channel por publicación
/// - declara el exchange destino (idempotente)
/// - serializa el evento a JSON
/// - publica con BasicProperties apropiadas
/// </remarks>
public sealed class RabbitMqIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IConnection _connection;
    private readonly RabbitOptions _opt;

    /// <summary>
    /// Inicializa una nueva instancia del publicador RabbitMQ.
    /// </summary>
    public RabbitMqIntegrationEventPublisher(
        IConnection connection,
        RabbitOptions opt)
    {
        _connection = connection;
        _opt = opt;
    }

    /// <summary>
    /// Publica un evento de integración en el exchange y routing key indicados.
    /// </summary>
    public async Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        string exchangeName,
        string routingKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        // =====================================================
        // Crea un channel propio para esta publicación.
        // =====================================================
        //
        // [!] IMPORTANTE:
        // IConnection es thread-safe.
        // IChannel NO es thread-safe.
        //
        // En publicación, crear un channel por operación es simple y seguro.
        await using IChannel channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        // Declara el exchange destino.
        // La operación es idempotente si la configuración coincide.
        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        // Serializa el evento a JSON UTF-8.
        string json = JsonSerializer.Serialize(integrationEvent);
        byte[] body = Encoding.UTF8.GetBytes(json);

        // Propiedades AMQP del mensaje.
        BasicProperties props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",

            // Type se usa luego del lado del consumer para resolver el CLR Type.
            Type = typeof(TEvent).Name,

            // Metadata útil para diagnóstico e idempotencia.
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);
    }
}
