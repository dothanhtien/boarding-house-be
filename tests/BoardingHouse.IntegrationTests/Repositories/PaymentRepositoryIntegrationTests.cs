using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    [Fact]
    public async Task GetInvoiceIdAsync_ReturnsTheInvoiceOfThePayment()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        var paymentId = await InsertAsync(NewPayment(invoiceId, staffId));

        await using var context = fixture.CreateContext();
        var repository = new PaymentRepository(context);

        Assert.Equal(invoiceId, await repository.GetInvoiceIdAsync(paymentId));
        Assert.Null(await repository.GetInvoiceIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetForUpdateAsync_ReturnsATrackedPayment_AndLocksTheRowUntilCommit()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        var paymentId = await InsertAsync(NewPayment(invoiceId, staffId));

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var payment = await new PaymentRepository(context).GetForUpdateAsync(paymentId);

        Assert.NotNull(payment);
        Assert.Equal(EntityState.Unchanged, context.Entry(payment).State);

        // Another connection cannot take the same row lock while the transaction is open
        await using (var other = fixture.CreateContext())
        {
            await using var otherTransaction = await other.Database.BeginTransactionAsync();
            var exception = await Assert.ThrowsAsync<PostgresException>(() => other.Database.ExecuteSqlRawAsync(
                "SELECT id FROM payments WHERE id = {0} FOR UPDATE NOWAIT", paymentId));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        }

        await transaction.CommitAsync();

        await using var afterCommit = fixture.CreateContext();
        await using var afterCommitTransaction = await afterCommit.Database.BeginTransactionAsync();
        await afterCommit.Database.ExecuteSqlRawAsync("SELECT id FROM payments WHERE id = {0} FOR UPDATE NOWAIT", paymentId);
    }

    [Fact]
    public async Task GetForUpdateAsync_UnknownPayment_ReturnsNull()
    {
        await SeedInvoiceAsync();

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.Null(await new PaymentRepository(context).GetForUpdateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListPendingForUpdateByInvoiceIdAsync_ReturnsOnlyThePendingPaymentsOfTheInvoice_TrackedAndOrderedById()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        var (otherInvoiceId, otherStaffId, _) = await SeedInvoiceAsync();
        var firstPending = await InsertAsync(NewPayment(invoiceId, staffId));
        var secondPending = await InsertAsync(NewPayment(invoiceId, staffId));
        var confirmed = NewPayment(invoiceId, staffId);
        confirmed.Confirm(staffId, DateTimeOffset.UtcNow);
        await InsertAsync(confirmed);
        var voided = NewPayment(invoiceId, staffId);
        voided.Void("Typed the wrong amount", staffId, DateTimeOffset.UtcNow);
        await InsertAsync(voided);
        await InsertAsync(NewPayment(otherInvoiceId, otherStaffId));

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var payments = await new PaymentRepository(context).ListPendingForUpdateByInvoiceIdAsync(invoiceId);

        Assert.Equal(new[] { firstPending, secondPending }.Order(), payments.Select(p => p.Id));
        Assert.All(payments, p => Assert.Equal(EntityState.Unchanged, context.Entry(p).State));
    }

    [Fact]
    public async Task ListPendingForUpdateByInvoiceIdAsync_LocksTheRowsUntilCommit()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        var paymentId = await InsertAsync(NewPayment(invoiceId, staffId));

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await new PaymentRepository(context).ListPendingForUpdateByInvoiceIdAsync(invoiceId);

        await using var other = fixture.CreateContext();
        await using var otherTransaction = await other.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => other.Database.ExecuteSqlRawAsync(
            "SELECT id FROM payments WHERE id = {0} FOR UPDATE NOWAIT", paymentId));
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
    }

    [Fact]
    public async Task ListPendingForUpdateByInvoiceIdAsync_NoPendingPayments_ReturnsEmpty()
    {
        var (invoiceId, _, _) = await SeedInvoiceAsync();

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.Empty(await new PaymentRepository(context).ListPendingForUpdateByInvoiceIdAsync(invoiceId));
    }

    // Same payment date → the one recorded first; ids are chosen so that ordering by id alone would flip them
    [Fact]
    public async Task ListByInvoiceIdAsync_ReturnsEveryPaymentOfTheInvoice_ByPaymentDateThenCreatedAt_Untracked()
    {
        var (invoiceId, staffId, tenantId) = await SeedInvoiceAsync();
        var (otherInvoiceId, otherStaffId, _) = await SeedInvoiceAsync();
        var sameDay = DateTimeOffset.UtcNow.AddDays(-1);
        var latest = await InsertAsync(NewPayment(invoiceId, staffId, tenantId, DateTimeOffset.UtcNow.AddHours(-1)));
        var recordedFirst = await InsertAsync(TestEntities.Set(
            NewPayment(invoiceId, staffId, paymentDate: sameDay),
            new { Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") }));
        var recordedSecond = await InsertAsync(TestEntities.Set(
            NewPayment(invoiceId, staffId, paymentDate: sameDay),
            new { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") }));
        var voided = NewPayment(invoiceId, staffId, paymentDate: DateTimeOffset.UtcNow.AddDays(-3));
        voided.Void("Typed the wrong amount", staffId, DateTimeOffset.UtcNow);
        await InsertAsync(voided);
        await InsertAsync(NewPayment(otherInvoiceId, otherStaffId));

        await using var context = fixture.CreateContext();
        var payments = await new PaymentRepository(context).ListByInvoiceIdAsync(invoiceId);

        Assert.Equal([voided.Id, recordedFirst, recordedSecond, latest], payments.Select(p => p.Id));
        Assert.All(payments, p => Assert.Equal("INV-2026-11-0001", p.Invoice!.InvoiceNumber));
        Assert.Equal("Tenant", payments[^1].PayerUser!.FullName);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListByInvoiceIdAsync_SoftDeletedInvoice_ReturnsEmpty()
    {
        var (invoiceId, staffId, _) = await SeedInvoiceAsync();
        await InsertAsync(NewPayment(invoiceId, staffId));
        await SoftDeleteAsync("invoices", invoiceId);

        await using var context = fixture.CreateContext();

        Assert.Empty(await new PaymentRepository(context).ListByInvoiceIdAsync(invoiceId));
    }

    private static Payment NewPayment(
        Guid invoiceId, Guid createdBy, Guid? payerUserId = null, DateTimeOffset? paymentDate = null) =>
        Payment.ForInvoice(
            invoiceId,
            new PaymentDetails(500_000, PaymentMethod.Cash, paymentDate ?? DateTimeOffset.UtcNow.AddHours(-1), payerUserId,
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
