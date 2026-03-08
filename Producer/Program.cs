using Entities;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

// **********************
// **     PRODUCER     **
// **********************
// Este producer sería el que dispara los eventos de integración del aggregate "Person".
// Ver: Naming_Ejemplos.txt

const string MainExchange = "person.integration"; // Renombrar a "person.integration.events"
////const string MainQueue = "demo.queue"; // Si se declara en el consumer, no es necesario para el producer. Se puede hacer asi o declararla en los 2 lados con los mismos mainQueueArgs.
//const string MainKey = "demo.key";

var factory = new ConnectionFactory
{
    HostName = "localhost",
    UserName = "guest",
    Password = "guest"
};

// Chanel: Es el canal técnico de comunicación entre la aplicación y RabbitMQ dentro de una conexión.
await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

// Analogía:
// 📞 Channel = Línea telefónica con el correo.
// 🏢 Exchange = Centro de clasificación de correo.
// 📬 Queue = Buzón.
// 🔗 Bind = Etiqueta que le dice al centro: “Todo lo que diga ‘Ventas’ mandalo al buzón Ventas.”.

// 1) Exchange.
// Exchange: Es el "distribuidor", recibe mensajes del producer y decide a qué cola(s) enviarlos según
// reglas (tipo Direct, Topic, Fanout, etc.).
await channel.ExchangeDeclareAsync(
    exchange: MainExchange,
    type: ExchangeType.Topic,
    durable: true,
    autoDelete: false,
    arguments: null);

//// Queue: Es donde se almacenan los mensajes hasta que un consumer los procesa.
//// 2) Queue.
//await channel.QueueDeclareAsync(
//    queue: MainQueue,
//    durable: true, // La cola sobrevive si RabbitMQ se reinicia.
//    exclusive: false, // Si es exclusiva a esta conexión.
//    autoDelete: false, // Si la cola se borra sola cuando ya no hay consumidores.
//    arguments: null);

//// Bind: Es la regla que conecta el exchage con la queue
//// 3) Bind
//await channel.QueueBindAsync(
//    queue: MainQueue,
//    exchange: MainExchange,
//    routingKey: MainKey);

// Registra el handler para mensajes NO ruteados (cuando se publica un mensaje y el exchange no
// encuentra ninguna cola vinculada (bind) que coincida).
// NOTA: Esto aplica si al publicar se manda "mandatory: true" al publicar.
channel.BasicReturnAsync += async (_, args) =>
{
    // args: reply code/text, exchange, routingKey, properties, body, etc.
    string jsonMessage = Encoding.UTF8.GetString(args.Body.ToArray());

    Console.WriteLine("⚠️ BASIC.RETURN (mensaje NO ruteado)");
    Console.WriteLine($"- Reply: {args.ReplyCode} {args.ReplyText}");
    Console.WriteLine($"- Exchange: {args.Exchange}");
    Console.WriteLine($"- RoutingKey: {args.RoutingKey}");
    Console.WriteLine($"- Body: {jsonMessage}");

    await Task.CompletedTask;
};

// Se crea un método genérico para poder pasarle el tipo del mensaje (messageType) al dispatcher,
// que lo usará para decidir qué handler ejecutar.
static BasicProperties CreateProps(string messageType)
{
    return new BasicProperties
    {
        Persistent = true,
        ContentType = "application/json",
        Type = messageType,                       // 👈 para el dispatcher
        MessageId = Guid.NewGuid().ToString(),     // útil para idempotencia
        CorrelationId = Guid.NewGuid().ToString(), // en real: correlacioná con request/trace
        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    };
}

// Se crea un método genérico para publicar eventos de cualquier tipo, serializándolos a JSON y
// enviándolos al exchange con la routing key adecuada.
async Task PublishAsync<T>(T evt)
{
    var messageType = typeof(T).Name;              // "PersonCreatedIntegrationEvent"
    var routingKey = RoutingKeyFor(messageType);

    // JSON es estándar y recomendado, luego se pasa a bytes.
    var json = JsonSerializer.Serialize(evt);

    // El body siempre debe estar en bytes.
    var body = Encoding.UTF8.GetBytes(json);

    var props = CreateProps(messageType);

    await channel.BasicPublishAsync(
        exchange: MainExchange,
        routingKey: routingKey,
        mandatory: true, // para no perder silenciosamente si no hay bindings
        basicProperties: props,
        body: body);

    Console.WriteLine($"[x] Published {messageType} rk={routingKey} msgId={props.MessageId}");
}





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
        await PublishAsync(new PersonCreatedIntegrationEvent(personId, "test@acme.com", DateTime.UtcNow));
    }
    else if (cmd == "u")
    {
        await PublishAsync(new PersonUpdatedIntegrationEvent(personId, "new@acme.com", DateTime.UtcNow));
    }
    else if (cmd == "d")
    {
        await PublishAsync(new PersonDeletedIntegrationEvent(personId, DateTime.UtcNow));
    }
    else if (cmd == "error") 
    {
        await PublishAsync(new PersonCreatedIntegrationEvent(personId, "error@acme.com", DateTime.UtcNow));
    }
}


static string RoutingKeyFor(string messageType)
{
    switch (messageType)
    {
        case nameof(PersonCreatedIntegrationEvent):
            return "person.created";

        case nameof(PersonUpdatedIntegrationEvent):
            return "person.updated";

        case nameof(PersonDeletedIntegrationEvent):
            return "person.deleted";

        default:
            throw new InvalidOperationException($"Unknown message type: {messageType}");
    }
}

/*
Ejemplo de mensaje para microservicios:

{
  "eventType": "UserCreated",
  "version": 1,
  "data": {
    "id": 123,
    "name": "Esteban"
  }
}

*/