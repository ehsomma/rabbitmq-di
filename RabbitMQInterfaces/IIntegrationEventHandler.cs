using RabbitMQ.Client; // Requiere instalar paquete NuGet RabbitMQ.Client para acceder a IReadOnlyBasicProperties.

namespace RabbitMQInterfaces;

/*
NOTA:
Esta interfaz está acoplada a RabbitMQ ya que usa IReadOnlyBasicProperties en el parámetro `props`.
Si se quisiera desacoplar, se podría crear una interfaz más genérica (ej: IGenericIntegrationConsumer) 
que no dependa de RabbitMQ y manejar la metadata de en un envelope de nuestro mensaje.
*/

/// <summary>
/// Handler tipado para un evento de integración.
/// </summary>
/// <typeparam name="TEvent">Tipo del evento de integración (ej. PersonCreatedIntegrationEvent).</typeparam>
/// <remarks>
/// Las implementaciones de esta interfaz deben enfocarse en lógica de negocio,
/// sin preocuparse por deserialización (bytes → objeto).
/// </remarks>
public interface IIntegrationEventHandler<in TEvent>
{
    /// <summary>
    /// Procesa el evento de integración ya deserializado.
    /// </summary>
    /// <param name="evt">
    /// El evento a procesar.
    /// </param>
    /// <param name="props">
    /// Propiedades AMQP (metadata de RabbitMQ) asociadas al mensaje (headers, message-id, 
    /// correlation-id, type, content-type, etc.). Útil para correlación, idempotencia, versionado 
    /// y metadatos de transporte.
    /// </param>
    /// <param name="ct">
    /// Token de cancelación para soportar apagado ordenado y/o timeouts externos.
    /// </param>
    /// <returns>
    /// Una tarea que finaliza cuando el mensaje fue procesado.
    /// Si la tarea falla (excepción), el caller decidirá si hace NACK, requeue, DLQ, etc.
    /// </returns>
    Task HandleAsync(TEvent evt, IReadOnlyBasicProperties props, CancellationToken ct);
}
