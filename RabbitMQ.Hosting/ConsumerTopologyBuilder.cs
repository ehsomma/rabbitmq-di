using RabbitMQ.Core;

namespace RabbitMQ.Hosting;

/// <summary>
/// Construye automáticamente una topología RabbitMQ por aggregate
/// a partir de los handlers registrados en el microservicio.
/// </summary>
public sealed class ConsumerTopologyBuilder : IConsumerTopologyBuilder
{
    private readonly IIntegrationEventNamingStrategy _eventNamingStrategy;
    private readonly IQueueNamingStrategy _queueNamingStrategy;

    /// <summary>
    /// Inicializa una nueva instancia del builder de topología.
    /// </summary>
    public ConsumerTopologyBuilder(
        IIntegrationEventNamingStrategy eventNamingStrategy,
        IQueueNamingStrategy queueNamingStrategy)
    {
        _eventNamingStrategy = eventNamingStrategy;
        _queueNamingStrategy = queueNamingStrategy;
    }

    /// <summary>
    /// Genera una definición de queue por aggregate presente en los handlers.
    /// </summary>
    public IReadOnlyCollection<AggregateQueueDefinition> Build(
        string serviceName,
        IEnumerable<IIntegrationMessageHandler> handlers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(handlers);

        // Agrupa rutas por aggregate.
        Dictionary<string, List<(string ExchangeName, string RoutingKey)>> groupedRoutes = new(
            StringComparer.Ordinal);

        foreach (IIntegrationMessageHandler handler in handlers)
        {
            Type eventType = handler.HandledEventType;

            string aggregateName = _eventNamingStrategy.GetAggregateName(eventType);
            string exchangeName = _eventNamingStrategy.GetExchangeName(eventType);
            string routingKey = _eventNamingStrategy.GetRoutingKey(eventType);

            if (!groupedRoutes.TryGetValue(aggregateName, out List<(string ExchangeName, string RoutingKey)>? routes))
            {
                routes = new List<(string ExchangeName, string RoutingKey)>();
                groupedRoutes[aggregateName] = routes;
            }

            routes.Add((exchangeName, routingKey));
        }

        List<AggregateQueueDefinition> result = new();

        foreach ((string aggregateName, List<(string ExchangeName, string RoutingKey)> routes) in groupedRoutes)
        {
            string queueName = _queueNamingStrategy.GetQueueName(serviceName, aggregateName);
            string dlxName = _queueNamingStrategy.GetDlxName(serviceName, aggregateName);
            string dlqName = _queueNamingStrategy.GetDlqName(serviceName, aggregateName);
            string dlqRoutingKey = _queueNamingStrategy.GetDlqRoutingKey(serviceName, aggregateName);

            // Un aggregate debería tener un solo exchange principal.
            string exchangeName = routes
                .Select(r => r.ExchangeName)
                .Distinct(StringComparer.Ordinal)
                .Single();

            string[] routingKeys = routes
                .Select(r => r.RoutingKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            result.Add(new AggregateQueueDefinition
            {
                AggregateName = aggregateName,
                ExchangeName = exchangeName,
                QueueName = queueName,
                DlxName = dlxName,
                DlqName = dlqName,
                DlqRoutingKey = dlqRoutingKey,
                RoutingKeys = routingKeys
            });
        }

        return result;
    }
}