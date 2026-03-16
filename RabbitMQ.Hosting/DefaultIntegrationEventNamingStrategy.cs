namespace RabbitMQ.Hosting;

/// <summary>
/// Estrategia de naming por convención para eventos de integración.
/// </summary>
/// <remarks>
/// Convención actual:
/// - PersonCreatedIntegrationEvent  -> exchange: person.integration / routing key: person.created
/// - PersonUpdatedIntegrationEvent  -> exchange: person.integration / routing key: person.updated
/// - PersonDeletedIntegrationEvent  -> exchange: person.integration / routing key: person.deleted
///
/// Es decir:
/// {aggregate}{Action}IntegrationEvent
///      ↓
/// exchange   = {aggregate}.integration
/// routingKey = {aggregate}.{action}
/// </remarks>
public sealed class DefaultIntegrationEventNamingStrategy : IIntegrationEventNamingStrategy
{
    /// <summary>
    /// Obtiene el exchange aplicando la convención definida.
    /// </summary>
    public string GetExchangeName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        string aggregateName = GetAggregateName(eventType);
        return $"{aggregateName}.integration";
    }

    /// <summary>
    /// Obtiene la routing key aplicando la convención definida.
    /// </summary>
    public string GetRoutingKey(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        string aggregateName = GetAggregateName(eventType);
        string actionName = GetActionName(eventType);

        return $"{aggregateName}.{actionName}";
    }

    /// <summary>
    /// Extrae el nombre del aggregate del tipo del evento.
    /// Ejemplo: PersonCreatedIntegrationEvent -> person
    /// </summary>
    private static string GetAggregateName(Type eventType)
    {
        string eventName = eventType.Name;

        if (!eventName.EndsWith("IntegrationEvent", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"El tipo '{eventName}' no sigue la convención '*IntegrationEvent'.");
        }

        string baseName = eventName[..^"IntegrationEvent".Length];

        string[] knownActions = ["Created", "Updated", "Deleted"];

        foreach (string action in knownActions)
        {
            if (baseName.EndsWith(action, StringComparison.Ordinal))
            {
                string aggregate = baseName[..^action.Length];
                return aggregate.ToLowerInvariant();
            }
        }

        throw new InvalidOperationException(
            $"No se pudo inferir la acción del evento '{eventName}'.");
    }

    /// <summary>
    /// Extrae la acción del tipo del evento.
    /// Ejemplo: PersonCreatedIntegrationEvent -> created
    /// </summary>
    private static string GetActionName(Type eventType)
    {
        string eventName = eventType.Name;

        if (!eventName.EndsWith("IntegrationEvent", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"El tipo '{eventName}' no sigue la convención '*IntegrationEvent'.");
        }

        string baseName = eventName[..^"IntegrationEvent".Length];

        string[] knownActions = ["Created", "Updated", "Deleted"];

        foreach (string action in knownActions)
        {
            if (baseName.EndsWith(action, StringComparison.Ordinal))
            {
                return action.ToLowerInvariant();
            }
        }

        throw new InvalidOperationException(
            $"No se pudo inferir la acción del evento '{eventName}'.");
    }
}
