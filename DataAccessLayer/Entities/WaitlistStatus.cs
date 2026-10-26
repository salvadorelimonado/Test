using System.Text.Json.Serialization;

namespace DataAccessLayer.Entities;

[JsonConverter(typeof(KebabCaseEnumConverter<WaitlistStatus>))]
public enum WaitlistStatus
{
    Waiting,
    Promoted,
    Cancelled
}
