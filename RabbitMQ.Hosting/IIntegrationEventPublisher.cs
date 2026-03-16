namespace RabbitMQ.Hosting;

/// <summary>
/// Publicador de eventos de integración hacia RabbitMQ.
/// </summary>
/// <remarks>
/// Esta interfaz representa el contrato "raw" de publicación:
/// el caller indica explícitamente el exchange y la routing key.
/// </remarks>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publica un evento de integración en el exchange y routing key indicados.
    /// </summary>
    /// <typeparam name="TEvent">Tipo del evento de integración.</typeparam>
    /// <param name="integrationEvent">Instancia del evento a publicar.</param>
    /// <param name="exchangeName">Nombre del exchange destino.</param>
    /// <param name="routingKey">Routing key / topic del mensaje.</param>
    /// <param name="ct">Token de cancelación.</param>
    Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        string exchangeName,
        string routingKey,
        CancellationToken ct = default);
}
