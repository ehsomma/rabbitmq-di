using RabbitMQ.Client;

namespace RabbitMQInterfaces;

/// <summary>
/// Handler de infraestructura para mensajes de integración recibidos desde RabbitMQ.
/// </summary>
/// <remarks>
/// Esta interfaz representa el contrato "raw" (payload en bytes + propiedades AMQP (Advanced Message Queuing Protocol)).
/// El dispatcher resuelve implementaciones de esta interfaz en base a <see cref="MessageType"/>,
/// que normalmente coincide con <see cref="IReadOnlyBasicProperties.Type"/> publicado por el producer.
/// </remarks>
public interface IIntegrationMessageHandler
{
    // MODI: Ahora usamos el Type (HandledEventType) en lugar del string.
    ///// <summary>
    ///// Identificador del tipo de mensaje que este handler procesa.
    ///// Por convención coincide con el nombre del evento (por ejemplo <c>"PersonCreatedIntegrationEvent"</c>).
    ///// </summary>
    //string MessageType { get; }

    /// <summary>
    /// Tipo CLR del evento que este handler procesa.
    /// </summary>
    Type HandledEventType { get; }

    /// <summary>
    /// Procesa el mensaje crudo recibido (payload en bytes).
    /// </summary>
    /// <param name="body">Payload del mensaje (por ejemplo JSON UTF-8).</param>
    /// <param name="props">Propiedades AMQP del mensaje (type, message-id, correlation-id, headers, etc.).</param>
    /// <param name="ct">Token de cancelación.</param>
    Task HandleAsync(ReadOnlyMemory<byte> body, IReadOnlyBasicProperties props, CancellationToken ct);
}
