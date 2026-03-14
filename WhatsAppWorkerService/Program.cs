using Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using RabbitMQ.Client;
using RabbitMQInterfaces;
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
// [ ] Ver consejo de ChatGPT para usar type y no string.
// [ ] Mover a extension methods tipo AddXxxxx y UseXxxxx
// [*] Ver este consejo de ChatGPT: En lugar de registrar IChannel directamente, yo registraría solo la conexión y crearía el channel dentro del Worker.
// ========================================

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Registra RabbitOptions.
// Configuración de RabbitMQ.
// NOTE: Por ahora hardcode. Más adelante pasar a appsettings.json + IOptions<RabbitOptions>.
builder.Services.AddSingleton(new RabbitOptions
{
    HostName = "localhost",
    UserName = "guest",
    Password = "guest",

    // Exchange de eventos de integración del aggregate Person.
    ExchangeName = "person.integration", // Renombrar a "person.integration.events"

    // Queue de este microservicio.
    QueueName = "whatsapp.person.integration",

    // Topic(s) que escucha esta queue.
    BindingKey = "person.*",

    // Dead Letter Exchange / Queue propios del microservicio.
    DlxName = "whatsapp.person.integration.dlx",
    DlqName = "whatsapp.person.integration.dlq",
    DlqRoutingKey = "whatsapp.person.dlq"
});

// Registra los handlers de los eventos de integración.
//
// Opción A: uno por uno (manual).
// services.AddSingleton<IIntegrationMessageHandler, PersonCreatedEventHandler>();
// services.AddSingleton<IIntegrationMessageHandler, PersonUpdatedEventHandler>();
// services.AddSingleton<IIntegrationMessageHandler, PersonDeletedEventHandler>();
// 
// Opción B: con scrutor (scan por assembly).
// En B, Scrutor busca clases en el assembly de PersonCreatedEventHandler que sean asignables a
// IIntegrationMessageHandler y las registra como sus interfaces.
// Con WithSingletonLifetime() quedan como singletons.
//
// Resultado: DI va a poder resolver IEnumerable<IIntegrationMessageHandler> con la lista completa
// de handlers encontrados.
builder.Services.Scan(scan => scan
    .FromAssemblyOf<PersonCreatedEventHandler>()
    .AddClasses(c => c.AssignableTo<IIntegrationMessageHandler>())
    .AsImplementedInterfaces()
    .WithSingletonLifetime());

// Registra el IntegrationEventTypeResolver.
builder.Services.AddSingleton<IntegrationEventTypeResolver>();

// Registra el IntegrationEventDispatcher.
//
// El dispatcher recibe internamente: IEnumerable<IIntegrationMessageHandler>
// NOTE: DI construye automáticamente ese IEnumerable con todos los handlers registrados arriba.
builder.Services.AddSingleton<IntegrationEventDispatcher>();

// =====================================================
// RabbitMQ
//
// Analogía de conceptos básicos:
// 📞 Channel = Línea telefónica con el correo (línea de comunicación con RabbitMQ).
// 🏢 Exchange = Centro de clasificación de correo (distribuidor de mensajes).
// 📬 Queue = Buzón donde quedan los mensajes luego de clasificarlos.
// 🔗 Bind = Etiqueta que le dice al centro: "Todo lo que diga 'Ventas' mandalo al buzón Ventas.".
// =====================================================

// Registra el ConnectionFactory (usando factory lambda).
builder.Services.AddSingleton(sp =>
{
    RabbitOptions opt = sp.GetRequiredService<RabbitOptions>();

    return new ConnectionFactory
    {
        HostName = opt.HostName,
        UserName = opt.UserName,
        Password = opt.Password
    };
});

// Registra Connection.
// NOTE: Crea la conexión async, pero como estamos en composición DI (sync), usa "sync over async"
// con GetAwaiter().GetResult().
builder.Services.AddSingleton<IConnection>(sp =>
{
    // Resuelve ConnectionFactory.
    ConnectionFactory connectionFactory = sp.GetRequiredService<ConnectionFactory>();

    // Crea la conexión una vez (sync over async en composición).
    return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// MODI: El chanel ahora se crea dentro del Worker en ExecuteAsync(), no se registra en DI.
//// Registra el Channel.
//// [!] IMPORTANTE: IChannel NO es thread-safe. En este ejemplo está bien porque hay un solo consumer
//// AMQP usando este channel (un chanel por Proyecto). Si se quisiesen trabajar en paralelo dentro
//// del mismo proyecto, se necesitarian más chanels.
//builder.Services.AddSingleton<IChannel>(sp =>
//{
//    IConnection conn = sp.GetRequiredService<IConnection>();
//    return conn.CreateChannelAsync().GetAwaiter().GetResult();
//});

// =====================================================

// Registra el Worker.
// AddHostedService<T> le dice al Host: "cuando la aplicación arranque, ejecutá este BackgroundService".
builder.Services.AddHostedService<WhatsAppWorker>();

// Construye y ejecuta el Host.
// A partir de acá, el ciclo de vida lo maneja .NET.
IHost host = builder.Build();
await host.RunAsync();
