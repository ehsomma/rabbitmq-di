namespace RabbitMQ.Hosting;

/// <summary>
/// Publicador de eventos de integración basado en convención.
/// </summary>
/// <remarks>
/// Resuelve exchange y routing key a partir del tipo CLR del evento
/// usando una <see cref="IIntegrationEventNamingStrategy"/>.
/// </remarks>
public sealed class ConventionIntegrationEventPublisher : IConventionIntegrationEventPublisher
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIntegrationEventNamingStrategy _namingStrategy;

    /// <summary>
    /// Inicializa una nueva instancia del publicador por convención.
    /// </summary>
    public ConventionIntegrationEventPublisher(
        IIntegrationEventPublisher publisher,
        IIntegrationEventNamingStrategy namingStrategy)
    {
        _publisher = publisher;
        _namingStrategy = namingStrategy;
    }

    /// <summary>
    /// Publica un evento aplicando la convención de naming configurada.
    /// </summary>
    public async Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        Type eventType = typeof(TEvent);

        string exchangeName = _namingStrategy.GetExchangeName(eventType);
        string routingKey = _namingStrategy.GetRoutingKey(eventType);

        await _publisher.PublishAsync(
            integrationEvent,
            exchangeName,
            routingKey,
            ct);
    }
}