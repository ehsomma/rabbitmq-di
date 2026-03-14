namespace Entities;

public sealed class RabbitOptions
{
    public string HostName { get; init; } = "localhost";
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";

    public string ExchangeName { get; init; } = "person.integration"; // topic
    public string QueueName { get; init; } = string.Empty;
    public string BindingKey { get; init; } = "person.*";
    public string DlxName { get; init; } = string.Empty;
    public string DlqName { get; init; } = string.Empty;
    public string DlqRoutingKey { get; set; } = string.Empty;
}
