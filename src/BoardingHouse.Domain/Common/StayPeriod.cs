namespace BoardingHouse.Domain.Common;

// The days a tenant lived in a lease's room: from the day they moved in to the day they left (null = still there).
// A tenant who moved out and came back has one StayPeriod per lease_tenants row.
public readonly record struct StayPeriod(DateOnly From, DateOnly? To)
{
    // Lived there at least one day of the month
    public bool Covers(BillingMonth month) => From <= month.LastDay && (To is null || To >= month.FirstDay);
}
