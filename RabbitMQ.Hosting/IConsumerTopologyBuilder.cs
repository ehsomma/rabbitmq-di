using RabbitMQ.Core;

namespace RabbitMQ.Hosting;

/// <summary>
/// Construye la topología RabbitMQ de un microservicio consumidor
/// a partir de sus handlers registrados.
/// </summary>
public interface IConsumerTopologyBuilder
{
    /// <summary>
    /// Genera una definición de queue por aggregate presente en los handlers.
    /// </summary>
    IReadOnlyCollection<AggregateQueueDefinition> Build(
        string serviceName,
        IEnumerable<IIntegrationMessageHandler> handlers);
}
