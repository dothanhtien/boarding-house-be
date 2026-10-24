using System.Text.Json.Serialization;
using BoardingHouse.Application.Common;

namespace BoardingHouse.Application.DTOs.Leases;

public record UpsertLeaseUtilityServiceRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<decimal?> UnitPriceOverride { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IsActive { get; init; }
}
