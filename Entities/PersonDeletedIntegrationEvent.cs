namespace Entities;

public class PersonDeletedIntegrationEvent
{
    public PersonDeletedIntegrationEvent(Guid personId, DateTime occurredAt)
    {
        PersonId = personId;
        OccurredAt = occurredAt;
    }

    public Guid PersonId { get; set; }

    public DateTime OccurredAt { get; set; }
}
