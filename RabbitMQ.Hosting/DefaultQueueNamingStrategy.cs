namespace RabbitMQ.Hosting;

/// <summary>
/// Estrategia de naming por convención para queue, DLX y DLQ.
/// </summary>
/// <remarks>
/// Convención actual:
/// - Queue         = {service}.{aggregate}.integration
/// - DLX           = {queue}.dlx
/// - DLQ           = {queue}.dlq
/// - DLQ RoutingKey= {service}.{aggregate}.dlq
///
/// Ejemplos:
/// - service = whatsapp, aggregate = persons
///   -> queue         = whatsapp.persons.integration
///   -> dlx           = whatsapp.persons.integration.dlx
///   -> dlq           = whatsapp.persons.integration.dlq
///   -> dlqRoutingKey = whatsapp.persons.dlq
/// </remarks>
public sealed class DefaultQueueNamingStrategy : IQueueNamingStrategy
{
    /// <summary>
    /// Obtiene el nombre de la queue principal.
    /// </summary>
    public string GetQueueName(string serviceName, string aggregateName)
    {
        return $"{serviceName.ToLowerInvariant()}.{aggregateName}.integration";
    }

    /// <summary>
    /// Obtiene el nombre del DLX.
    /// </summary>
    public string GetDlxName(string serviceName, string aggregateName)
    {
        return $"{GetQueueName(serviceName, aggregateName)}.dlx";
    }

    /// <summary>
    /// Obtiene el nombre del DLQ.
    /// </summary>
    public string GetDlqName(string serviceName, string aggregateName)
    {
        return $"{GetQueueName(serviceName, aggregateName)}.dlq";
    }

    /// <summary>
    /// Obtiene la routing key del DLQ.
    /// </summary>
    public string GetDlqRoutingKey(string serviceName, string aggregateName)
    {
        return $"{serviceName.ToLowerInvariant()}.{aggregateName}.dlq";
    }
}
