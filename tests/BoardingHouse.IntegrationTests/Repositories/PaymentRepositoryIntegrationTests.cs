using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Repositories;

public class PaymentRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_OnlyStagesThePayment_UntilSaveChanges()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        await using var context = fixture.CreateContext();
        var repository = new PaymentRepository(context);
        var payment = NewPayment(invoiceId, staffId);

        await repository.AddAsync(payment);

        Assert.Equal(EntityState.Added, context.Entry(payment).State);
        await using (var readContext = fixture.CreateContext())
        {
            Assert.False(await readContext.Payments.AnyAsync());
        }

        await context.SaveChangesAsync();

        await using var afterSave = fixture.CreateContext();
        var saved = await afterSave.Payments.SingleAsync();
        Assert.Equal(payment.Id, saved.Id);
        Assert.Equal(invoiceId, saved.InvoiceId);
        Assert.Equal(PaymentStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_LoadsInvoiceLeaseRoomPropertyAndPayer_Untracked()
    {
        var (invoiceId, staffId, tenantId) = await SeedInvoiceAsync();
        var paymentId = await InsertAsync(NewPayment(invoiceId, staffId, tenantId));

        await using var context = fixture.CreateContext();
        var payment = await new PaymentRepository(context).GetByIdWithDetailsAsync(paymentId);

        Assert.NotNull(payment);
        Assert.Equal("INV-2026-11-0001", payment.Invoice!.InvoiceNumber);
        Assert.Equal("HD-001", payment.Invoice.Lease!.LeaseCode);
        Assert.Equal("101", payment.Invoice.Lease.Room!.RoomNumber);
        Assert.Equal("Property", payment.Invoice.Lease.Room.Property!.Name);
        Assert.Equal("Tenant", payment.PayerUser!.FullName);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_UnknownPayment_ReturnsNull()
    {
        await SeedInvoiceAsync();

        await using var context = fixture.CreateContext();

        Assert.Null(await new PaymentRepository(context).GetByIdWithDetailsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_SoftDeletedInvoice_ReturnsNull()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        var paymentId = await InsertAsync(NewPayment(invoiceId, staffId));
        await SoftDeleteAsync("invoices", invoiceId);

        await using var context = fixture.CreateContext();

        Assert.Null(await new PaymentRepository(context).GetByIdWithDetailsAsync(paymentId));
    }

    // Like InvoiceRepository: an old payment still shows where it came from after its lease / room / property / payer
    // is soft-deleted
    [Fact]
    public async Task GetByIdWithDetailsAsync_SoftDeletedLeaseRoomPropertyAndPayer_AreStillLoaded()
    {
        var (invoiceId, staffId, tenantId) = await SeedInvoiceAsync();
        var paymentId = await InsertAsync(NewPayment(invoiceId, staffId, tenantId));
        Guid leaseId, roomId, propertyId;
        await using (var context = fixture.CreateContext())
        {
            var lease = await context.Leases.Include(l => l.Room).SingleAsync();
            (leaseId, roomId, propertyId) = (lease.Id, lease.RoomId, lease.Room!.PropertyId);
        }
        await SoftDeleteAsync("leases", leaseId);
        await SoftDeleteAsync("rooms", roomId);
        await SoftDeleteAsync("properties", propertyId);
        await SoftDeleteAsync("users", tenantId);

        await using var readContext = fixture.CreateContext();
        var payment = await new PaymentRepository(readContext).GetByIdWithDetailsAsync(paymentId);

        Assert.NotNull(payment);
        Assert.Equal("HD-001", payment.Invoice!.Lease!.LeaseCode);
        Assert.Equal("101", payment.Invoice.Lease.Room!.RoomNumber);
        Assert.Equal("Property", payment.Invoice.Lease.Room.Property!.Name);
        Assert.Equal("Tenant", payment.PayerUser!.FullName);
    }

    private static Payment NewPayment(Guid invoiceId, Guid createdBy, Guid? payerUserId = null) =>
        Payment.ForInvoice(
            invoiceId,
            new PaymentDetails(500_000, PaymentMethod.Cash, DateTimeOffset.UtcNow.AddHours(-1), payerUserId,
                PayerNameOverride: null, TransactionReference: null, Note: null),
            createdBy,
            DateTimeOffset.UtcNow);

    private async Task<Guid> InsertAsync(Payment payment)
    {
        await using var context = fixture.CreateContext();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    // table is one of the fixed names above, never user input
    private async Task SoftDeleteAsync(string table, Guid id)
    {
        await using var context = fixture.CreateContext();
#pragma warning disable EF1002
        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET deleted_at = now(), deleted_by = {{0}} WHERE id = {{1}}", SentinelActors.System, id);
#pragma warning restore EF1002
    }

    private async Task<(Guid InvoiceId, Guid StaffId, Guid TenantId)> SeedInvoiceAsync()
    {
        await using var context = fixture.CreateContext();
        return await TestInvoices.SeedAsync(context);
    }
}
