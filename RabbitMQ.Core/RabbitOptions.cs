namespace RabbitMQ.Core;

public sealed class RabbitOptions
{
    /// <summary>Host del broker RabbitMQ.</summary>
    public string HostName { get; set; } = default!;

    /// <summary>Usuario de conexión.</summary>
    public string UserName { get; set; } = default!;

    /// <summary>Contraseña de conexión.</summary>
    public string Password { get; set; } = default!;

    /// <summary>Exchange principal de eventos de integración.</summary>
    public string ExchangeName { get; set; } = default!;

    /// <summary>Queue principal del microservicio consumidor.</summary>
    public string QueueName { get; set; } = default!;

    /// <summary>Binding key / topic que escucha esta queue.</summary>
    public string BindingKey { get; set; } = default!;

    /// <summary>Dead Letter Exchange (DLX) de la queue principal.</summary>
    public string DlxName { get; set; } = default!;

    /// <summary>Dead Letter Queue (DLQ) donde caerán los mensajes fallidos.</summary>
    public string DlqName { get; set; } = default!;

    /// <summary>Routing key usada para enviar mensajes al DLX / DLQ.</summary>
    public string DlqRoutingKey { get; set; } = default!;

    /// <summary>Cantidad máxima de mensajes "en vuelo" por consumer antes de ACK.</summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>Nombre lógico del microservicio consumidor.Solo se usa para logging / diagnóstico.</summary>
    public string ServiceName { get; set; } = default!;
}
