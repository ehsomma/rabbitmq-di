namespace RabbitMQ.Hosting;

/// <summary>
/// Estrategia de naming para resolver exchange y routing key
/// a partir del tipo CLR de un evento de integración.
/// </summary>
public interface IIntegrationEventNamingStrategy
{
    /// <summary>
    /// Obtiene el nombre del exchange para el tipo de evento indicado.
    /// </summary>
    string GetExchangeName(Type eventType);

    /// <summary>
    /// Obtiene la routing key / topic para el tipo de evento indicado.
    /// </summary>
    string GetRoutingKey(Type eventType);
}
