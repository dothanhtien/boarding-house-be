using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.Common;

// Lease's constructor is private: tests build leases through the aggregate like LeaseService does
internal static class TestLeases
{
    // JoinedDate null = the lease StartDate (set by Activate)
    public sealed record TenantSpec(DateOnly? JoinedDate = null, DateOnly? LeftDate = null);

    // The first tenant without a LeftDate is the representative; no tenants = one representative
    public static Lease Draft(
        DateOnly startDate,
        DateOnly? endDate = null,
        decimal monthlyRent = 3_000_000,
        decimal depositAmount = 0,
        int? billingDay = null,
        Guid? previousLeaseId = null,
        Room? room = null,
        IReadOnlyList<LeaseUtilityServiceSelection>? utilityServices = null,
        params TenantSpec[] tenants)
    {
        if (tenants.Any(t => t.LeftDate is not null))
        {
            throw new ArgumentException("Only an active lease can have tenants who moved out", nameof(tenants));
        }

        return NewDraft(
            startDate, endDate, monthlyRent, depositAmount, billingDay, previousLeaseId, room, utilityServices, tenants).Lease;
    }

    public static Lease Active(
        DateOnly startDate,
        DateOnly? endDate = null,
        decimal monthlyRent = 3_000_000,
        decimal depositAmount = 0,
        int? billingDay = null,
        Guid? previousLeaseId = null,
        Room? room = null,
        IReadOnlyList<LeaseUtilityServiceSelection>? utilityServices = null,
        params TenantSpec[] tenants)
    {
        var (lease, people) = NewDraft(
            startDate, endDate, monthlyRent, depositAmount, billingDay, previousLeaseId, room, utilityServices, tenants);

        lease.Activate(At(startDate), startDate, SentinelActors.System);

        var moveOuts = people.Where(p => p.Spec.LeftDate is not null).ToList();
        if (moveOuts.Count > 0)
        {
            // "Today" must not be before any LeftDate
            var today = moveOuts.Max(p => p.Spec.LeftDate!.Value);
            var changes = moveOuts
                .Select(p => new TenantChange
                {
                    TenantId = lease.Tenants.Single(t => t.UserId == p.User.Id).Id,
                    LeftDate = p.Spec.LeftDate
                })
                .ToList();

            // Both steps of a tenant update, as LeaseService runs them (minus the save in between)
            lease.Update(lease.CurrentTerms, changes, new Dictionary<Guid, User>(), today, SentinelActors.System);
            if (lease.HasPendingTenantChanges)
            {
                lease.ApplyPendingTenantChanges();
            }
        }

        return lease;
    }

    // Terminated "today" = leftDate, so leftDate is the latest day Terminate accepts
    public static void Terminate(Lease lease, DateOnly leftDate) =>
        lease.Terminate(At(leftDate), leftDate, SentinelActors.System, leftDate, "reason");

    public static void Expire(Lease lease)
    {
        var dayAfterEnd = lease.EndDate!.Value.AddDays(1);
        lease.Expire(At(dayAfterEnd), dayAfterEnd);
    }

    private static (Lease Lease, (TenantSpec Spec, User User)[] People) NewDraft(
        DateOnly startDate,
        DateOnly? endDate,
        decimal monthlyRent,
        decimal depositAmount,
        int? billingDay,
        Guid? previousLeaseId,
        Room? room,
        IReadOnlyList<LeaseUtilityServiceSelection>? utilityServices,
        TenantSpec[] tenants)
    {
        if (tenants.Length == 0)
        {
            tenants = [new TenantSpec()];
        }

        var representative = Array.FindIndex(tenants, t => t.LeftDate is null);
        if (representative < 0)
        {
            throw new ArgumentException("At least one tenant must still live in the room", nameof(tenants));
        }

        var people = tenants.Select(t => (Spec: t, User: NewUser())).ToArray();

        var lease = Lease.CreateDraft(
            Guid.NewGuid(),
            room ?? TestRooms.New(),
            previousLeaseId,
            "HD-TEST",
            startDate,
            endDate,
            billingDay,
            monthlyRent,
            depositAmount,
            people
                .Select((p, index) => new TenantChange
                {
                    UserId = p.User.Id,
                    IsRepresentative = index == representative,
                    JoinedDate = p.Spec.JoinedDate
                })
                .ToList(),
            people.ToDictionary(p => p.User.Id, p => p.User),
            utilityServices ?? [],
            At(startDate),
            SentinelActors.System);

        return (lease, people);
    }

    private static User NewUser() => TestEntities.New<User>(new
    {
        FullName = "Tenant",
        Phone = "0900000000",
        PasswordHash = "hash",
        IsActive = true,
        CreatedBy = SentinelActors.System
    });

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
