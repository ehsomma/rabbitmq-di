using RabbitMQ.Client;
using RabbitMQInterfaces;
using System.Text.Json;

namespace EmailWorkerService;

/// <summary>namespace EmailWorkerd
/// Clase base que adapta un mensaje crudo (bytes) a un evento tipado (<typeparamref name="TEvent"/>).
/// </summary>
/// <typeparam name="TEvent">Tipo del evento de integración.</typeparam>
/// <remarks>
/// Expone dos métodos <c>HandleAsync</c> porque implementa dos contratos:
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="IIntegrationMessageHandler"/>: entrada raw (bytes) usada por el dispatcher / Program.cs.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="IIntegrationEventHandler{TEvent}"/>: entrada tipada implementada por las clases concretas.
/// </description>
/// </item>
/// </list>
/// </remarks>
public abstract class IntegrationEventHandlerBase<TEvent> :
    IIntegrationMessageHandler,
    IIntegrationEventHandler<TEvent>
{
    // MODI: Ahora usamos el Type (HandledEventType) en lugar del string.
    ///// <summary>
    ///// Identificador del tipo de mensaje asociado a <typeparamref name="TEvent"/>.
    ///// Por convención coincide con <see cref="IReadOnlyBasicProperties.Type"/> (producer).
    ///// </summary>
    //public string MessageType => typeof(TEvent).Name;

    /// <summary>
    /// Tipo CLR del evento manejado por este handler.
    /// </summary>
    public Type HandledEventType => typeof(TEvent);

    /// <summary>
    /// Maneja el mensaje raw (bytes): deserializa a <typeparamref name="TEvent"/> y delega al handler tipado.
    /// </summary>
    /// <remarks>
    /// Si el JSON es inválido o incompatible, <see cref="JsonSerializer.Deserialize{TValue}"/> puede lanzar
    /// <see cref="JsonException"/>. La política de ACK/NACK/DLQ debe decidirla el caller (Program.cs).
    /// </remarks>
    public async Task HandleAsync(
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties props,
        CancellationToken ct)
    {
        TEvent evt = JsonSerializer.Deserialize<TEvent>(body.Span)!;

        await HandleAsync(evt, props, ct);
    }

    /// <summary>
    /// Procesa el evento ya deserializado. Las clases concretas implementan este método.
    /// </summary>
    public abstract Task HandleAsync(
        TEvent evt,
        IReadOnlyBasicProperties props,
        CancellationToken ct);
}
