namespace RabbitMQ.Hosting;

/// <summary>
/// Estrategia de naming por convención para eventos de integración.
/// </summary>
/// <remarks>
/// Convención actual:
///
/// Namespace:
/// MyProject.Shared.IntegrationEvents.{aggregate}
/// Ejemplo:
/// MyProject.Shared.IntegrationEvents.Persons
/// -> aggregate = "persons"
///
/// Event:
/// {action}IntegrationEvent
/// Ejemplo:
/// AddressCreatedIntegrationEvent
/// -> action = "addresscreated"
///
/// Resultado:
/// - exchange   = {aggregate}.integration
/// - routingKey = {aggregate}.{action}
///
/// Ejemplos:
/// - MyProject.Shared.IntegrationEvents.Persons.PersonCreatedIntegrationEvent
///   -> exchange: persons.integration
///   -> routing key: persons.personcreated
///
/// - MyProject.Shared.IntegrationEvents.Persons.AddressCreatedIntegrationEvent
///   -> exchange: persons.integration
///   -> routing key: persons.addresscreated
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
    /// Extrae el aggregate desde el último segmento del namespace del evento.
    /// </summary>
    /// <remarks>
    /// Ejemplo:
    /// Namespace: MyProject.Shared.IntegrationEvents.Persons
    /// Resultado: persons
    /// </remarks>
    private static string GetAggregateName(Type eventType)
    {
        string? ns = eventType.Namespace;

        if (string.IsNullOrWhiteSpace(ns))
        {
            throw new InvalidOperationException(
                $"El tipo '{eventType.Name}' no tiene namespace y no se puede inferir el aggregate.");
        }

        int lastDotIndex = ns.LastIndexOf('.');

        if (lastDotIndex < 0 || lastDotIndex == ns.Length - 1)
        {
            throw new InvalidOperationException(
                $"No se pudo inferir el aggregate desde el namespace '{ns}'.");
        }

        string aggregate = ns[(lastDotIndex + 1)..];

        return aggregate.ToLowerInvariant();
    }

    /// <summary>
    /// Extrae la acción desde el nombre del evento, quitando el sufijo 'IntegrationEvent'.
    /// </summary>
    /// <remarks>
    /// Ejemplo:
    /// AddressCreatedIntegrationEvent -> addresscreated
    /// PersonDeletedIntegrationEvent -> persondeleted
    /// </remarks>
    private static string GetActionName(Type eventType)
    {
        string eventName = eventType.Name;

        const string suffix = "IntegrationEvent";

        if (!eventName.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"El tipo '{eventName}' no sigue la convención '*{suffix}'.");
        }

        string action = eventName[..^suffix.Length];

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new InvalidOperationException(
                $"No se pudo inferir la acción desde el evento '{eventName}'.");
        }

        return action.ToLowerInvariant();
    }
}