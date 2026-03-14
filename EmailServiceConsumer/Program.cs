using Consumer;
using EmailServiceConsumer;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQInterfaces;
using System.Threading.Channels;

// **********************
// **     CONSUMER     **
// **********************
// Ver: Naming_Ejemplos.txt
// NOTE: Este proyecto es una app de consola para demo. El microservicio WhatsappWorkerService es
// un Worker/HostedService más adecuado para producción (para prod, convertir esta app de consola
// a Worker).

// ========================================
// Simula un microservicio de envío de email.
//
// Lee la cola: email.person.integration
// Eventos que captura:
//      PersonCreatedIntegrationEvent,
//      PersonUpdatedIntegrationEvent,
//      PersonDeletedIntegrationEvent
//
// TODO:
// [*] Preguntar a chatgpt que haga ejemplos de nombres de colas y topic para WhatsApp y Projection
// [ ] Convertir de app de consola a Worker/HostedService (para no tener que usar "await Task.Delay infinito" para mantener el proceso vivo).
// [ ] Mover a extension methods tipo AddXxxxx y UseXxxxx
// ========================================

ServiceCollection services = new ServiceCollection();

// Registra RabbitOptions.
// Configuración de RabbitMQ.
// NOTE: Por ahora hardcode. Más adelante pasar a appsettings.json + IOptions<RabbitOptions>.
services.AddSingleton(new RabbitOptions
{
    HostName = "localhost",
    UserName = "guest",
    Password = "guest",

    // Exchange de eventos de integración del aggregate Person.
    ExchangeName = "person.integration", // Renombrar a "person.integration.events"

    // Queue de este microservicio.
    QueueName = "email.person.integration",

    // Topic(s) que escucha esta queue.
    BindingKey = "person.*",

    // Dead Letter Exchange / Queue propios del microservicio.
    DlxName = "email.person.integration.dlx",
    DlqName = "email.person.integration.dlq",
    DlqRoutingKey = "email.person.dlq"
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
services.Scan(scan => scan
    .FromAssemblyOf<PersonCreatedEventHandler>()
    .AddClasses(c => c.AssignableTo<IIntegrationMessageHandler>())
    .AsImplementedInterfaces()
    .WithSingletonLifetime());

// Registra el IntegrationEventTypeResolver.
services.AddSingleton<IntegrationEventTypeResolver>();

// Registra el IntegrationEventDispatcher.
//
// El dispatcher recibe internamente: IEnumerable<IIntegrationMessageHandler>
// NOTE: DI construye automáticamente ese IEnumerable con todos los handlers registrados arriba.
services.AddSingleton<IntegrationEventDispatcher>();

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
services.AddSingleton(sp =>
{
    // Resuelve RabbitOptions para configurar el ConnectionFactory.
    RabbitOptions opt = sp.GetRequiredService<RabbitOptions>();

    return new ConnectionFactory
    {
        HostName = opt.HostName,
        UserName = opt.UserName,
        Password = opt.Password
    };
});

// Registra Connection.
//    ^
//   / \
//  / ! \
// /_____\
//
// [!] IMPORTANTE: Acá crea la conexión async, pero como estamos en registro DI (sync), hace
// "sync over async" con GetAwaiter().GetResult().
// Para consola demo está OK; en Worker/HostedService hay que hacerlo 100% async.
// NOTA2: Cuando le dije a ChatGPT, hace un Worker, lo hizo igual con "sync over async".
services.AddSingleton<IConnection>(sp =>
{
    // Resuelve ConnectionFactory.
    ConnectionFactory connectionFactory = sp.GetRequiredService<ConnectionFactory>();
    
    // Crea la conexión una vez (sync over async en composición).
    return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
});

// Registra el Channel.
// [!] IMPORTANTE: IChannel NO es thread-safe. En este ejemplo está bien porque hay un solo consumer
// AMQP usando este channel (un chanel por Proyecto). Si se quisiesen trabajar en paralelo dentro
// del mismo proyecto, se necesitarian más chanels.
services.AddSingleton<IChannel>(sp =>
{
    // Resuelve Connection.
    IConnection conn = sp.GetRequiredService<IConnection>();

    return conn.CreateChannelAsync().GetAwaiter().GetResult();
});

// =====================================================

// Construye el contenedor (ServiceProvider).
// A partir de acá ya se puede usar serviceProvider para resolver instancias.
//
// "await using" se usa para disponer el provider al final. Igual cerramos explícitamente
// channel/connection porque son IAsyncDisposable y el contenedor no siempre los cierra async
// automáticamente en consola.
await using ServiceProvider serviceProvider = services.BuildServiceProvider();

// Resuelve RabbitOptions.
RabbitOptions opt = serviceProvider.GetRequiredService<RabbitOptions>();

// Resuelve el IntegrationEventDispatcher.
IntegrationEventDispatcher dispatcher = serviceProvider.GetRequiredService<IntegrationEventDispatcher>();

// Resuelve el Channel.
IChannel channel = serviceProvider.GetRequiredService<IChannel>();

/*
Sobre DLQ (Dead Letter Queue):
=============================
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

Un DLQ por microservicio (ej: este es un microservicio de envío de emials):
    Creo que tener una DLQ por microservicio es la mejor opción para evitar mezclar mensajes de distintos 
    servicios y facilitar el análisis. En este ejemplo, el EmailService tiene su propia 
    DLQ (email.person.integration.dlq) y su propio DLX (email.person.integration.dlx) para reenviar 
    los mensajes rechazados.
*/
// Declara DLX y DLQ (Dead Letter).
//
// DLX: Exchange donde RabbitMQ re-publica mensajes "dead-lettered".
await channel.ExchangeDeclareAsync(
    exchange: opt.DlxName,
    type: ExchangeType.Direct,   // Direct es suficiente para DLQ.
    durable: true,
    autoDelete: false,
    arguments: null);

// DLQ: Cola que recibirá los mensajes rechazados / fallidos.
await channel.QueueDeclareAsync(
    queue: opt.DlqName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null);

// Bind DLQ al DLX con una routing key (en Direct es obligatorio que coincida).
await channel.QueueBindAsync(
    queue: opt.DlqName,
    exchange: opt.DlxName,
    routingKey: opt.DlqRoutingKey);

// Define los argumentos de la DLX para luego asignarlos a la queue principal.
Dictionary<string, object?> mainQueueArgs = new Dictionary<string, object?>
{
    // Exchange al que RabbitMQ enviará el mensaje cuando se rechace.
    ["x-dead-letter-exchange"] = opt.DlxName,

    // Con qué routing key se re-publica al DLX (para que matchee el bind de la DLQ).
    ["x-dead-letter-routing-key"] = opt.DlqRoutingKey,
};

// Declara exchange + queue + bind (infra del servicio).
// [!] IMPORTANTE: declare debe ser CONSISTENTE en todos los servicios (si ya existe en otros consumers y producers,
// debe coincidir) o RabbitMQ lanza PRECONDITION_FAILED.
//
// 1) Exchange principal de eventos de integración.
// Exchange: Es el "distribuidor", recibe mensajes del producer y decide a qué cola(s) enviarlos según
// reglas (tipo Direct, Topic, Fanout, etc.). 
await channel.ExchangeDeclareAsync(
    opt.ExchangeName, 
    ExchangeType.Topic, 
    durable: true, 
    autoDelete: false, 
    arguments: null);

// 2) Queue principal del microservicio.
// Queue: Es donde se almacenan los mensajes hasta que un consumer los procesa.
await channel.QueueDeclareAsync(
    queue: opt.QueueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: mainQueueArgs);

// 3) Bind.
// Bind: Es la regla que conecta el exchage con la queue
await channel.QueueBindAsync(
    opt.QueueName, 
    opt.ExchangeName, 
    opt.BindingKey);

// Configura el QoS del chanel (Quality of Service).
//
// QoS controla cuántos mensajes RabbitMQ entrega "en vuelo" a este consumer antes de recibir ACKs.
// ACK = Acknowledgement (confirmación). Es el mensaje que el consumer envía al broker para
//       decir: "Ya procesé este mensaje correctamente.".
//
// - prefetchCount = 10 => hasta 10 mensajes sin ACK al mismo tiempo.
// - Como autoAck = false, el mensaje se considera pendiente hasta ACK/NACK.
await channel.BasicQosAsync(
    prefetchSize: 0, 
    prefetchCount: 10, 
    global: false);

// Cancelación / shutdown con Ctrl+C.
//
// Capturamos Ctrl+C y cancelamos un token.
// La idea es:
//  - cortar el "Task.Delay infinito".
//  - permitir salir ordenadamente y luego cerrar channel/connection.
using CancellationTokenSource cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Crea un consumer Consumer + Handler de recepción.
// Es el que recibe mensajes desde la queue a través del channel.
AsyncEventingBasicConsumer consumer = new AsyncEventingBasicConsumer(channel);

// Declara el handler para el evento que se ejecuta cuando llega un mensaje y se suscribe.
// NOTA: += es la forma de C# para agregar el handler (suscribirse) a un evento.
//
// ea (BasicDeliverEventArgs) contiene la metadata del mensaje:
//  - Body (payload).
//  - BasicProperties (props.Type, MessageId, CorrelationId, headers, etc.).
//  - DeliveryTag (id interno para ACK/NACK).
//  - Exchange / RoutingKey (info del delivery).
consumer.ReceivedAsync += async (_, ea) =>
{
    try
    {
        // Obtiene el nombre del tipo del mensaje, ej: "PersonCreatedIntegrationEvent".
        string? messageTypeName = ea.BasicProperties?.Type;

        // Intenta resolver el tipo CLR de un evento a partir del string recibido por RabbitMQ.
        if (!_typeResolver.TryResolve(messageTypeName, out Type? eventType))
        {
            _logger.LogWarning(
                "[WhatsAppService] Unknown event type name: '{MessageTypeName}' -> reject",
                messageTypeName ?? "(null)");

            await _channel.BasicNackAsync(
                ea.DeliveryTag,
                multiple: false,
                requeue: false);

            return;
        }

        // Generalmente, el handler espera que el mensaje tenga propiedades con info como
        // MessageType, CorrelationId, etc. Pero por las dudas, controla el null.
        if (ea.BasicProperties is null)
        {
            Console.WriteLine("[EmailService] Message without properties -> reject");
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
            return;
        }

        // Ejecuta el handler:
        //  - IntegrationEventHandlerBase<T> deserializa bytes → JSON → TEvent.
        //  - luego llama al handler tipado (PersonCreatedEventHandler, etc.).
        await handler.HandleAsync(ea.Body, ea.BasicProperties, cts.Token);

        // ACK: confirma al broker que el mensaje fue procesado OK.
        // RabbitMQ lo quita de la queue.
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
    }
    catch (OperationCanceledException)
    {
        // Si estamos apagando (Ctrl+C), evitamos hacer ACK/NACK.
        // El cierre del channel/connection se hace fuera de este callback.
        return; // shutdown.
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[EmailService] ERROR: {ex.Message}");

        // NACK = Not ACK (mensaje no procesado).
        // [!] Si `requeue: true`, vuelve a la cola inmediatamente pero si sigue fallando, hace un loop infinito.
        // [!] Si `requeue: false` Si la cola tiene DLX/DLQ configurada, dispara el dead-lettering.
        ////await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true); // Loop infinito si sigue fallando.
        await channel.BasicNackAsync(
            ea.DeliveryTag, 
            multiple: false, 
            requeue: false); // Dispara el dead-lettering hacia la DLQ (por el DLX configurado).
    }
};

// Suscripción del consumer a la queue.
// autoAck = false => ACK manual.
// NOTE: A partir de esta línea, RabbitMQ puede empezar a entregar mensajes.
await channel.BasicConsumeAsync(
    queue: opt.QueueName, 
    autoAck: false, // Hacemos el ACK manualmente luego de procesar el mensaje.
    consumer: consumer);

// Mantiene el proceso vivo hasta Ctrl+C.
// NOTE: Esto es para demo de consola. En un Worker/HostedService, el proceso se mantiene vivo por
// el ciclo de vida del host.
Console.WriteLine("[EmailService] Listening... Ctrl+C to exit.");
try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Shutdown.
}

// Shutdown ordenado.
Console.WriteLine("[EmailService] Shutting down...");

// Limpieza explícita (porque DI no sabe async-dispose de IChannel/IConnection)
await channel.CloseAsync();
await channel.DisposeAsync();

IConnection conn = serviceProvider.GetRequiredService<IConnection>();
await conn.CloseAsync();
await conn.DisposeAsync();