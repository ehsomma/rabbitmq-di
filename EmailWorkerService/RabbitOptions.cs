namespace EmailWorkerService;

public sealed class RabbitOptions
{
    public string HostName { get; init; } = "localhost";
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";

    public string ExchangeName { get; init; } = "person.integration"; // topic
    public string QueueName { get; init; } = "email.person.integration";
    public string BindingKey { get; init; } = "person.*";
    public string DlxName { get; internal set; } = string.Empty;
    public string DlqName { get; internal set; } = string.Empty;
    public string DlqRoutingKey { get; internal set; } = string.Empty;
}
