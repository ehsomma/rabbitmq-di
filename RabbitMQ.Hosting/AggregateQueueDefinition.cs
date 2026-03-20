namespace RabbitMQ.Hosting;

/// <summary>
/// Representa la topología RabbitMQ generada para un aggregate específico
/// dentro de un microservicio consumidor.
/// </summary>
public sealed class AggregateQueueDefinition
{
    /// <summary>
    /// Nombre del aggregate.
    /// </summary>
    public string AggregateName { get; init; } = default!;

    /// <summary>
    /// Exchange principal del aggregate.
    /// </summary>
    public string ExchangeName { get; init; } = default!;

    /// <summary>
    /// Queue principal del microservicio para ese aggregate.
    /// </summary>
    public string QueueName { get; init; } = default!;

    /// <summary>
    /// Dead-letter exchange (DLX) de la queue principal.
    /// </summary>
    public string DlxName { get; init; } = default!;

    /// <summary>
    /// Dead-letter queue (DLQ) donde caerán los mensajes fallidos.
    /// </summary>
    public string DlqName { get; init; } = default!;

    /// <summary>
    /// Routing key usada para enviar mensajes al DLX / DLQ.
    /// </summary>
    public string DlqRoutingKey { get; init; } = default!;

    /// <summary>
    /// Routing keys que deben bindearse a esta queue.
    /// </summary>
    public IReadOnlyCollection<string> RoutingKeys { get; init; } = [];
}
