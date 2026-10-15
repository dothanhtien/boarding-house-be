using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;

namespace BoardingHouse.Api.DTOs.Leases;

public record UpdateLeaseTenantRequest
{
    // Null = add a new tenant (UserId required); set = update (or delete, with IsDeleted) an existing tenant.
    public Guid? Id { get; init; }

    // Only for new tenants: an existing row can't be re-pointed to another user (delete + add instead).
    public Guid? UserId { get; init; }

    public bool? IsRepresentative { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateOnly?> JoinedDate { get; init; }

    public bool IsDeleted { get; init; }
}
