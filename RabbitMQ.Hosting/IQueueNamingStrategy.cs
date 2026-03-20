namespace RabbitMQ.Hosting;

/// <summary>
/// Estrategia de naming para generar queue, DLX, DLQ y routing key de DLQ
/// a partir del microservicio y el aggregate.
/// </summary>
public interface IQueueNamingStrategy
{
    /// <summary>
    /// Obtiene el nombre de la queue principal.
    /// </summary>
    string GetQueueName(string serviceName, string aggregateName);

    /// <summary>
    /// Obtiene el nombre del dead-letter exchange (DLX).
    /// </summary>
    string GetDlxName(string serviceName, string aggregateName);

    /// <summary>
    /// Obtiene el nombre de la dead-letter queue (DLQ).
    /// </summary>
    string GetDlqName(string serviceName, string aggregateName);

    /// <summary>
    /// Obtiene la routing key para enviar mensajes a la DLQ.
    /// </summary>
    string GetDlqRoutingKey(string serviceName, string aggregateName);
}
