using Entities;
using RabbitMQ.Client;
using RabbitMQ.Core;

namespace EmailWorkerService;

/// <summary>
/// Handler encargado de procesar el evento <see cref="PersonDeletedIntegrationEvent"/> en el contexto 
/// del EmailService.
/// </summary>
/// <remarks>
/// Se invoca cuando el producer publica <c>BasicProperties.Type</c> = <c>"PersonCreatedIntegrationEvent"</c>.
/// </remarks>
public sealed class PersonDeletedEventHandler : IntegrationEventHandlerBase<PersonDeletedIntegrationEvent>
{
    public override Task HandleAsync(PersonDeletedIntegrationEvent evt, IReadOnlyBasicProperties props, CancellationToken ct)
    {
        // Lógica de negocio del microservicio (en este caso, simula envío de email).
        Console.WriteLine($"[EmailService] Person deleted email -> {evt.PersonId}");

        return Task.CompletedTask;
    }
}
