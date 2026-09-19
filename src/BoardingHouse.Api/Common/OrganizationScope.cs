namespace BoardingHouse.Api.Common;

public class OrganizationScope
{
    public required bool IsUnrestricted { get; init; }

    public required IReadOnlySet<Guid> OrganizationIds { get; init; }

    public bool Includes(Guid organizationId) => IsUnrestricted || OrganizationIds.Contains(organizationId);

    public static readonly OrganizationScope None = new()
    {
        IsUnrestricted = false,
        OrganizationIds = new HashSet<Guid>()
    };
}
