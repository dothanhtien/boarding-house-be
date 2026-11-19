using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.IntegrationTests.Fixtures;

internal static class TestInvoices
{
    // A staff user, a draft lease (one representative tenant) in room 101 and an unpaid 2,000,000 invoice of that lease
    public static async Task<(Guid InvoiceId, Guid StaffId, Guid TenantId)> SeedAsync(AppDbContext context)
    {
        // lease_status_histories.changed_by references users, so the actor must be a real user
        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        var tenant = TestEntities.New<User>(new { FullName = "Tenant", PasswordHash = "hash", IsActive = true, CreatedBy = SentinelActors.System });
        var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = staff.Id });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property", CreatedBy = staff.Id });
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101", capacity: 2, createdBy: staff.Id);
        context.AddRange(staff, tenant, organization, property, room);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lease = Lease.CreateDraft(
            organization.Id, room, previousLeaseId: null, "HD-001", today, today.AddYears(1), billingDay: null,
            monthlyRent: 1_000_000, depositAmount: 1_000_000,
            [new TenantChange { UserId = tenant.Id, IsRepresentative = true }],
            new Dictionary<Guid, User> { [tenant.Id] = tenant }, [], DateTimeOffset.UtcNow, staff.Id);
        var invoice = TestEntities.New<Invoice>(new
        {
            OrganizationId = organization.Id,
            LeaseId = lease.Id,
            InvoiceNumber = "INV-2026-11-0001",
            BillingMonth = BillingMonth.FromFirstDay(new DateOnly(2026, 11, 1)),
            DueDate = new DateOnly(2026, 11, 10),
            Subtotal = 2_000_000m,
            TotalAmount = 2_000_000m,
            CreatedBy = staff.Id
        });
        context.AddRange(lease, invoice);
        await context.SaveChangesAsync();

        return (invoice.Id, staff.Id, tenant.Id);
    }
}
