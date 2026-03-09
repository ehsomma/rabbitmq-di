using System;
using System.Collections.Generic;
using System.Text;

namespace Entities
{
    public class PersonCreatedIntegrationEvent
    {
        public PersonCreatedIntegrationEvent(Guid personId, string? email, string? cellphone , DateTime occurredAt)
        {
            PersonId = personId;
            OccurredAt = occurredAt;
            Email = email;
            CellPhone = cellphone;
        }

        public Guid PersonId { get; set; }

        public DateTime OccurredAt { get; set; }

        public string? Email { get; set; }

        public string? CellPhone { get; set; }
    }
}
