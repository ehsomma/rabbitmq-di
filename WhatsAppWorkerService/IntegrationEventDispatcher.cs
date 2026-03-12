using Microsoft.Extensions.DependencyInjection;
using RabbitMQInterfaces;
using System.Diagnostics.CodeAnalysis;

namespace WhatsAppWorkerService;

/// <summary>
/// Resuelve el <see cref="IIntegrationMessageHandler"/> adecuado en función del tipo de mensaje.
/// Enruta el evento al handler correspondiente.
/// </summary>
/// <remarks>
/// Convención: <paramref name="messageType"/> suele ser el valor de <c>BasicProperties.Type</c>.
/// </remarks>
public sealed class IntegrationEventDispatcher
{
    // Diccionario "MessageType -> handler". Se construye una sola vez en el constructor a partir de
    // la colección de handlers para luego devolver el handler correspondiente al MessageType.
    // MODI: Antes usábamos el string MessageType, ahora usamos el Type HandledEventType.
    //private readonly Dictionary<string, IIntegrationMessageHandler> _consumersHandlers;
    private readonly Dictionary<Type, IIntegrationMessageHandler> _consumersHandlers;

    /// <summary>
    /// Construye el dispatcher con todos los handlers registrados en DI.
    /// </summary>
    /// <param name="handlers">Colección de handlers registrados en DI.</param>
    /// <exception cref="ArgumentException">Si hay tipos duplicados.</exception>
    public IntegrationEventDispatcher(IEnumerable<IIntegrationMessageHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        // MODI: Antes usábamos el string MessageType, ahora usamos el Type HandledEventType.
        //_consumersHandlers = handlers.ToDictionary(h => h.MessageType, StringComparer.Ordinal);
        _consumersHandlers = handlers.ToDictionary(h => h.HandledEventType, elementSelector: h => h);

    }

    // MODI: Antes usábamos el string MessageType, ahora usamos el Type HandledEventType.
    ///// <summary>
    ///// Intenta resolver el handler para el tipo de mensaje indicado.
    ///// </summary>
    ///// <param name="messageType">Identificador del tipo de mensaje, normalmente proveniente de <c>BasicProperties.Type</c>.</param>
    ///// <param name="handler">Asigna el handler resuelto si la operación fue exitosa.</param>
    //public bool TryResolve(string? messageType, [NotNullWhen(true)] out IIntegrationMessageHandler? handler)
    //{
    //    bool ret = false;
    //    handler = null;

    //    if (string.IsNullOrWhiteSpace(messageType))
    //    {
    //        handler = null;
    //        ret = false;
    //    }
    //    else if (_consumersHandlers.TryGetValue(messageType, out var resolved))
    //    {
    //        handler = resolved;
    //        ret = true;
    //    }

    //    return ret;
    //}

    /// <summary>
    /// Intenta resolver el handler correspondiente al tipo CLR del evento.
    /// </summary>
    /// <param name="eventType">El Type del evento.</param>
    /// <param name="handler">Asigna el handler resuelto si la operación fue exitosa.</param>
    public bool TryResolve(Type? eventType, [NotNullWhen(true)] out IIntegrationMessageHandler? handler)
    {
        bool ret = false;
        handler = null;

        if (eventType is null)
        {
            handler = null;
            ret = false;
        }
        else if (_consumersHandlers.TryGetValue(eventType, out var resolved))
        {
            handler = resolved;
            ret = true;
        }

        return ret;
    }
}
