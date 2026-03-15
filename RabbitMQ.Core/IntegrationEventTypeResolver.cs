using System;
using System.Collections.Generic;
using System.Text;

namespace RabbitMQ.Core;

/// <summary>
/// Resuelve el tipo CLR de un evento de integración a partir del valor textual recibido 
/// en <c>BasicProperties.Type</c>.
/// </summary>
/// <remarks>
/// El objetivo es encapsular el uso de strings en un único punto del sistema.
/// A partir de acá, el resto del pipeline trabaja con <see cref="Type"/>.
/// </remarks>
public sealed class IntegrationEventTypeResolver
{
    private readonly Dictionary<string, Type> _map;

    /// <summary>
    /// Construye el resolver a partir de los handlers registrados.
    /// </summary>
    public IntegrationEventTypeResolver(IEnumerable<IIntegrationMessageHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        _map = handlers
            .Select(h => h.HandledEventType)
            .Distinct()
            .ToDictionary(
                keySelector: t => t.Name,
                elementSelector: t => t,
                comparer: StringComparer.Ordinal);
    }

    /// <summary>
    /// Intenta resolver el tipo CLR de un evento a partir del string recibido por RabbitMQ.
    /// </summary>
    public bool TryResolve(string? messageTypeName, out Type? eventType)
    {
        eventType = null;

        if (string.IsNullOrWhiteSpace(messageTypeName))
        {
            return false;
        }

        return _map.TryGetValue(messageTypeName, out eventType);
    }
}
