using System.Text.Json.Serialization;
using Common.Serialization;

namespace DataAccessLayer.Entities;

[JsonConverter(typeof(KebabCaseEnumConverter<EnrollmentStatus>))]
public enum EnrollmentStatus
{
    Active,
    Cancelled
}
