namespace BoardingHouse.Domain.Entities.Enums;

// Where a utility service is already referenced; the service looks it up (it needs a query).
[Flags]
public enum UtilityServiceUsage
{
    None = 0,
    Leases = 1,
    Readings = 2
}
