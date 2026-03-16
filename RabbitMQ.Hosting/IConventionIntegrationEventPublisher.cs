namespace RabbitMQ.Hosting;

/// <summary>
/// Publicador de eventos de integración que resuelve exchange y routing key
/// mediante una convención centralizada.
/// </summary>
public interface IConventionIntegrationEventPublisher
{
    /// <summary>
    /// Publica un evento de integración aplicando la estrategia de naming configurada.
    /// </summary>
    /// <typeparam name="TEvent">Tipo del evento de integración.</typeparam>
    /// <param name="integrationEvent">Evento a publicar.</param>
    /// <param name="ct">Token de cancelación.</param>
    Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken ct = default);
}
