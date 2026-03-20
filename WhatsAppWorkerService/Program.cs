using MyProject.Shared.IntegrationEvents.Persons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using WhatsAppWorkerService;

// **********************
// **   WORKER / HOST  **
// **    (CONSUMER)    **
// **********************
// Ver: Naming_Ejemplos.txt

// ========================================
// Simula un microservicio de envío de WhatsApp.
//
// Lee la cola: whatsapp.person.integration
// Eventos que captura:
//      PersonCreatedIntegrationEvent,
//      PersonUpdatedIntegrationEvent,
//      PersonDeletedIntegrationEvent
//
// TODO:
// [*] Preguntar a chatgpt que haga ejemplos de nombres de colas y topic para WhatsApp y Projection
// [*] Convertir de app de consola a Worker/HostedService (para no tener que usar "await Task.Delay infinito" para mantener el proceso vivo).
// [*] Ver consejo de ChatGPT para usar type y no string.
// [*] Mover a extension methods tipo AddXxxxx y UseXxxxx
// [*] Ver este consejo de ChatGPT: En lugar de registrar IChannel directamente, yo registraría solo la conexión y crearía el channel dentro del Worker.
// ========================================

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// NOTE: Por ahora las opt estan hardcode. Más adelante pasar a appsettings.json + IOptions.
builder.Services.AddRabbitMqIntegrationConsumer(
    typeof(Program).Assembly,
    opt =>
    {
        opt.ServiceName = "whatsapp";
        opt.HostName = "localhost";
        opt.UserName = "guest";
        opt.Password = "guest";
        opt.PrefetchCount = 10;
    });

IHost host = builder.Build();
await host.RunAsync();
