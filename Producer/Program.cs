using MyProject.Shared.IntegrationEvents.Persons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Hosting;

// **********************
// **     PRODUCER     **
// **********************
// Este producer sería el que dispara los eventos de integración del aggregate "Person".
// Ver: Naming_Ejemplos.txt
//
// TODO:
// [*] Analizar DefaultIntegrationEventNamingStrategy y ver si es suficiente o si necesitamos algo más complejo (ej: para manejar casos especiales, o para permitir cierta flexibilidad sin tener que crear un naming strategy completamente nuevo).
//     Ver como hace MassTransit: https://masstransit-project.com/advanced/rabbitmq.html#exchange-and-queue-naming-conventions
//     Usar Namespaces y no solo el nombre del tipo para definir la convención? Ej: "MyApp.Domain.Person.CreatedIntegrationEvent".
// [ ] (ChatGPT) lograr que los bindings se generen automáticamente a partir de los handlers registrados (usando el IntegrationEventTypeResolver que ya hicimos). 

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Registra la infraestructura común de publicación RabbitMQ.
builder.Services.AddRabbitMqConventionPublisher(opt =>
{
    opt.ServiceName = "Producer";
    opt.HostName = "localhost";
    opt.UserName = "guest";
    opt.Password = "guest";
});

IHost host = builder.Build();

// ============================================


// Resuelve el publisher desde el contenedor de dependencias.
// NOTA: En un escenario real, el producer probablemente no necesite resolver el publisher directamente,
// sino que lo haría dentro de un servicio o handler específico (inyectado, sin usar GetRequiredService()).
// Pero para esta demo lo hacemos directamente en el Program.cs para simplificar.
IConventionIntegrationEventPublisher publisher =
    host.Services.GetRequiredService<IConventionIntegrationEventPublisher>();

//await publisher.PublishAsync(
//    new PersonCreatedIntegrationEvent(
//        Guid.NewGuid(),
//        "test@test.com",
//        "11 5000-0001",
//        DateTime.UtcNow));

//await publisher.PublishAsync(
//    new PersonUpdatedIntegrationEvent(
//        Guid.NewGuid(),
//        "updated@test.com",
//        "11 5000-0002",
//        DateTime.UtcNow));

//await publisher.PublishAsync(
//    new PersonDeletedIntegrationEvent(
//        Guid.NewGuid(),
//        DateTime.UtcNow));

// Simulación de eventos ('c', 'u', 'd', 'error' o 'exit').
Console.WriteLine("Producer listo. Escribí mensajes y Enter para enviar. 'c', 'u', 'd', 'error' o 'exit' para salir.");

while (true)
{
    Console.Write("> ");
    var cmd = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (cmd is null) continue;
    if (cmd == "exit") break;

    var personId = Guid.NewGuid();

    if (cmd == "c")
    {
        await publisher.PublishAsync(new PersonCreatedIntegrationEvent(personId, "test@acme.com", "11 5000-0001", DateTime.UtcNow));
    }
    else if (cmd == "u")
    {
        await publisher.PublishAsync(new PersonUpdatedIntegrationEvent(personId, "new@acme.com", "11 5000-0002", DateTime.UtcNow));
    }
    else if (cmd == "d")
    {
        await publisher.PublishAsync(new PersonDeletedIntegrationEvent(personId, DateTime.UtcNow));
    }
    else if (cmd == "error") 
    {
        await publisher.PublishAsync(new PersonCreatedIntegrationEvent(personId, "error@acme.com", "1234error", DateTime.UtcNow));
    }
}

