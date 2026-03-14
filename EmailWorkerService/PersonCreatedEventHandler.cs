using Entities;
using RabbitMQ.Client;

namespace EmailWorkerService;

/// <summary>
/// Handler encargado de procesar el evento <see cref="PersonCreatedIntegrationEvent"/> en el contexto 
/// del EmailService.
/// </summary>
/// <remarks>
/// Se invoca cuando el producer publica <c>BasicProperties.Type</c> = <c>"PersonCreatedIntegrationEvent"</c>.
/// </remarks>
public sealed class PersonCreatedEventHandler : IntegrationEventHandlerBase<PersonCreatedIntegrationEvent>
{
    /// <inheritdoc />
    public override Task HandleAsync(PersonCreatedIntegrationEvent evt, IReadOnlyBasicProperties props, CancellationToken ct)
    {
        /*
        // Simulación de excepción.

        Cuando el consumer captura una excepción lanzada por el *EventHandler y no puede procesar un mensaje, 
        hay 4 caminos típicos:
            A. Reintentar un número limitado de veces.
            B. Si sigue fallando (o si falla 1 sola vez) → mandarlo a DLQ (Dead Letter Queue) para análisis/manual/reproceso.
            C. Descartar (solo si el mensaje es realmente descartable).
            D. No usar DLQ y loggear el error (solo si el mensaje es realmente descartable).
        
        Un mensaje va a DLQ cuando (según config):
            • Se rechaza con BasicReject / BasicNack con requeue:false.
            • Expira por TTL.
            • Se supera el max length de la cola (si configuraste límites).
        */
        if (evt.Email == "error@acme.com")
        {
            throw new Exception("Fail simulado (ver DLQ)");
        }
        // Fin Simulación de excepción.

        Console.WriteLine($"[EmailService] Person created email -> {evt.Email} (PersonId={evt.PersonId})");

        return Task.CompletedTask;
    }
}
