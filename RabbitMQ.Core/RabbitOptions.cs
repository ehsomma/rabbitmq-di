namespace RabbitMQ.Core;

public sealed class RabbitOptions
{
    /// <summary>Nombre lógico del microservicio. Se usa para generar nombres de queues, DLX y DLQ.</summary>
    public string ServiceName { get; set; } = default!;

    /// <summary>Host del broker RabbitMQ.</summary>
    public string HostName { get; set; } = default!;

    /// <summary>Usuario de conexión.</summary>
    public string UserName { get; set; } = default!;

    /// <summary>Contraseña de conexión.</summary>
    public string Password { get; set; } = default!;

    /// <summary>Cantidad máxima de mensajes "en vuelo" por consumer antes de ACK.</summary>
    public ushort PrefetchCount { get; set; } = 10;
}
