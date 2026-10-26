using System.Text.Json.Serialization;
using Common.Serialization;

namespace DataAccessLayer.Entities;

[JsonConverter(typeof(KebabCaseEnumConverter<WaitlistStatus>))]
public enum WaitlistStatus
{
    Waiting,
    Promoted,
    Cancelled
}
