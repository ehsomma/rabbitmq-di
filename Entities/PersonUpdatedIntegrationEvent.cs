namespace Entities
{
    public class PersonUpdatedIntegrationEvent
    {
        public PersonUpdatedIntegrationEvent(Guid personId, string? email, DateTime occurredAt)
        {
            PersonId = personId;
            OccurredAt = occurredAt;
            Email = email;
        }

        public Guid PersonId { get; set; }

        public DateTime OccurredAt { get; set; }

        public string? Email { get; set; }
    }
}
