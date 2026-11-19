using Microsoft.EntityFrameworkCore;
using Npgsql;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Persistence;

public class PaymentPersistenceTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset PaidAt = new(2026, 11, 19, 3, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt = PaidAt.AddHours(2);
    private static readonly DateTimeOffset VoidedAt = PaidAt.AddDays(1);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConfirmedInvoicePayment_RoundTrips()
    {
        var seed = await SeedAsync();
        var payment = NewPayment(seed.InvoiceId, new
        {
            PayerUserId = (Guid?)seed.TenantId,
            Amount = 1_234_567.89m,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Confirmed,
            TransactionReference = "FT2632312345",
            ConfirmedBy = (Guid?)seed.StaffId,
            ConfirmedAt = (DateTimeOffset?)ConfirmedAt,
            Note = "Transferred by the tenant",
            CreatedBy = seed.StaffId
        });
        await InsertAsync(payment);

        await using var context = fixture.CreateContext();
        var saved = await context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.PayerUser)
            .SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(seed.InvoiceId, saved.InvoiceId);
        Assert.Equal(seed.InvoiceId, saved.Invoice!.Id);
        Assert.Null(saved.DepositReturnId);
        Assert.Equal(seed.TenantId, saved.PayerUserId);
        Assert.Equal("Tenant", saved.PayerUser!.FullName);
        Assert.Null(saved.PayerNameOverride);
        Assert.Equal(1_234_567.89m, saved.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, saved.Method);
        Assert.Equal(PaymentStatus.Confirmed, saved.Status);
        Assert.Equal("FT2632312345", saved.TransactionReference);
        Assert.Equal(PaidAt, saved.PaymentDate);
        Assert.Equal(seed.StaffId, saved.ConfirmedBy);
        Assert.Equal(ConfirmedAt, saved.ConfirmedAt);
        Assert.Null(saved.VoidedBy);
        Assert.Null(saved.VoidedAt);
        Assert.Null(saved.VoidReason);
        Assert.Equal("Transferred by the tenant", saved.Note);
        Assert.Equal(seed.StaffId, saved.CreatedBy);
        Assert.NotEqual(default, saved.CreatedAt);
    }

    [Fact]
    public async Task VoidedPayment_KeepsItsConfirmationAndStoresTheVoid()
    {
        var seed = await SeedAsync();
        var payment = NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Voided,
            ConfirmedBy = (Guid?)seed.StaffId,
            ConfirmedAt = (DateTimeOffset?)ConfirmedAt,
            VoidedBy = (Guid?)seed.StaffId,
            VoidedAt = (DateTimeOffset?)VoidedAt,
            VoidReason = "Wrong amount",
            CreatedBy = seed.StaffId
        });
        await InsertAsync(payment);

        await using var context = fixture.CreateContext();
        var saved = await context.Payments.SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(PaymentStatus.Voided, saved.Status);
        Assert.Equal(ConfirmedAt, saved.ConfirmedAt);
        Assert.Equal(seed.StaffId, saved.VoidedBy);
        Assert.Equal(VoidedAt, saved.VoidedAt);
        Assert.Equal("Wrong amount", saved.VoidReason);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, "CASH")]
    [InlineData(PaymentMethod.BankTransfer, "BANK_TRANSFER")]
    [InlineData(PaymentMethod.Momo, "MOMO")]
    [InlineData(PaymentMethod.ZaloPay, "ZALOPAY")]
    [InlineData(PaymentMethod.VnPay, "VNPAY")]
    public async Task Method_IsStoredInPaymentMethodColumnAsDbValue(PaymentMethod method, string dbValue)
    {
        var seed = await SeedAsync();
        var payment = NewPayment(seed.InvoiceId, new { Method = method, CreatedBy = seed.StaffId });
        await InsertAsync(payment);

        await using var context = fixture.CreateContext();
        var stored = await context.Database
            .SqlQueryRaw<string>("SELECT payment_method AS \"Value\" FROM payments WHERE id = {0}", payment.Id)
            .SingleAsync();
        var saved = await context.Payments.SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(dbValue, stored);
        Assert.Equal(method, saved.Method);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, "PENDING")]
    [InlineData(PaymentStatus.Confirmed, "CONFIRMED")]
    [InlineData(PaymentStatus.Voided, "VOIDED")]
    public async Task Status_IsStoredAsDbValue(PaymentStatus status, string dbValue)
    {
        var seed = await SeedAsync();
        var payment = NewPayment(seed.InvoiceId, new
        {
            Status = status,
            ConfirmedAt = (DateTimeOffset?)ConfirmedAt,
            VoidedAt = (DateTimeOffset?)VoidedAt,
            VoidReason = "Wrong amount",
            CreatedBy = seed.StaffId
        });
        await InsertAsync(payment);

        await using var context = fixture.CreateContext();
        var stored = await context.Database
            .SqlQueryRaw<string>("SELECT status AS \"Value\" FROM payments WHERE id = {0}", payment.Id)
            .SingleAsync();
        var saved = await context.Payments.SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(dbValue, stored);
        Assert.Equal(status, saved.Status);
    }

    [Fact]
    public async Task RowWithoutStatus_GetsTheDatabaseDefaultPending()
    {
        var seed = await SeedAsync();

        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO payments (invoice_id, amount, payment_method, payment_date, created_at, created_by)
            VALUES ({0}, 100000, 'CASH', now(), now(), {1})
            """,
            seed.InvoiceId, seed.StaffId);
        var saved = await context.Payments.SingleAsync(p => p.InvoiceId == seed.InvoiceId);

        Assert.Equal(PaymentStatus.Pending, saved.Status);
        Assert.NotEqual(Guid.Empty, saved.Id);
    }

    [Fact]
    public async Task WalkInPayerName_WithoutPayerUser_IsAllowed()
    {
        var seed = await SeedAsync();
        var payment = NewPayment(seed.InvoiceId, new { PayerNameOverride = "Tenant's mother", CreatedBy = seed.StaffId });
        await InsertAsync(payment);

        await using var context = fixture.CreateContext();
        var saved = await context.Payments.SingleAsync(p => p.Id == payment.Id);

        Assert.Null(saved.PayerUserId);
        Assert.Equal("Tenant's mother", saved.PayerNameOverride);
    }

    // ConfirmedBy / VoidedBy are null when the system (payment gateway) acts: only the timestamps are required
    [Fact]
    public async Task ConfirmedAndVoidedBySystem_WithoutActor_IsAllowed()
    {
        var seed = await SeedAsync();

        await InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Confirmed,
            ConfirmedAt = (DateTimeOffset?)ConfirmedAt,
            CreatedBy = seed.StaffId
        }));
        await InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Voided,
            VoidedAt = (DateTimeOffset?)VoidedAt,
            VoidReason = "Gateway timeout",
            CreatedBy = seed.StaffId
        }));
    }

    // deposit_returns does not exist yet: the column has no FK until TICKET-056
    [Fact]
    public async Task DepositRefundPayment_WithoutInvoice_IsAllowed()
    {
        var seed = await SeedAsync();
        var depositReturnId = Guid.NewGuid();
        var payment = NewPayment(invoiceId: null, new { DepositReturnId = (Guid?)depositReturnId, CreatedBy = seed.StaffId });
        await InsertAsync(payment);

        await using var context = fixture.CreateContext();
        var saved = await context.Payments.SingleAsync(p => p.Id == payment.Id);

        Assert.Null(saved.InvoiceId);
        Assert.Equal(depositReturnId, saved.DepositReturnId);
    }

    [Fact]
    public async Task SeveralPaymentsForOneInvoice_AreAllowed()
    {
        var seed = await SeedAsync();

        await InsertAsync(NewPayment(seed.InvoiceId, new { CreatedBy = seed.StaffId }));
        await InsertAsync(NewPayment(seed.InvoiceId, new { TransactionReference = "SAME", CreatedBy = seed.StaffId }));
        await InsertAsync(NewPayment(seed.InvoiceId, new { TransactionReference = "SAME", CreatedBy = seed.StaffId }));

        await using var context = fixture.CreateContext();
        Assert.Equal(3, await context.Payments.CountAsync(p => p.InvoiceId == seed.InvoiceId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveAmount_ViolatesCheckConstraint(int amount)
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewPayment(seed.InvoiceId, new { Amount = (decimal)amount, CreatedBy = seed.StaffId })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_amount");
    }

    [Fact]
    public async Task NoTarget_ViolatesCheckConstraint()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewPayment(invoiceId: null, new { CreatedBy = seed.StaffId })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_target");
    }

    [Fact]
    public async Task InvoiceAndDepositReturn_ViolatesCheckConstraint()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(
            NewPayment(seed.InvoiceId, new { DepositReturnId = (Guid?)Guid.NewGuid(), CreatedBy = seed.StaffId })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_target");
    }

    [Fact]
    public async Task PayerUserAndPayerName_ViolatesCheckConstraint()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewPayment(seed.InvoiceId, new
        {
            PayerUserId = (Guid?)seed.TenantId,
            PayerNameOverride = "Someone else",
            CreatedBy = seed.StaffId
        })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_payer");
    }

    [Fact]
    public async Task ConfirmedWithoutConfirmedAt_ViolatesCheckConstraint()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Confirmed,
            ConfirmedBy = (Guid?)seed.StaffId,
            CreatedBy = seed.StaffId
        })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_confirmed");
    }

    [Fact]
    public async Task VoidedWithoutVoidedAt_ViolatesCheckConstraint()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Voided,
            VoidReason = "Wrong amount",
            CreatedBy = seed.StaffId
        })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_voided");
    }

    [Fact]
    public async Task VoidedWithoutReason_ViolatesCheckConstraint()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Voided,
            VoidedAt = (DateTimeOffset?)VoidedAt,
            CreatedBy = seed.StaffId
        })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_payments_voided");
    }

    [Fact]
    public async Task UnknownInvoice_ViolatesForeignKey()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewPayment(Guid.NewGuid(), new { CreatedBy = seed.StaffId })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation, "fk_payments_invoices_invoice_id");
    }

    [Fact]
    public async Task UnknownPayer_ViolatesForeignKey()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(
            NewPayment(seed.InvoiceId, new { PayerUserId = (Guid?)Guid.NewGuid(), CreatedBy = seed.StaffId })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation, "fk_payments_users_payer_user_id");
    }

    [Fact]
    public async Task UnknownConfirmer_ViolatesForeignKey()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Confirmed,
            ConfirmedBy = (Guid?)Guid.NewGuid(),
            ConfirmedAt = (DateTimeOffset?)ConfirmedAt,
            CreatedBy = seed.StaffId
        })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation, "fk_payments_users_confirmed_by");
    }

    [Fact]
    public async Task UnknownVoider_ViolatesForeignKey()
    {
        var seed = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewPayment(seed.InvoiceId, new
        {
            Status = PaymentStatus.Voided,
            VoidedBy = (Guid?)Guid.NewGuid(),
            VoidedAt = (DateTimeOffset?)VoidedAt,
            VoidReason = "Wrong amount",
            CreatedBy = seed.StaffId
        })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation, "fk_payments_users_voided_by");
    }

    // Invoices are soft-deleted, but a hard delete must never orphan its payments
    [Fact]
    public async Task HardDeletingAnInvoiceWithPayments_IsRestricted()
    {
        var seed = await SeedAsync();
        await InsertAsync(NewPayment(seed.InvoiceId, new { CreatedBy = seed.StaffId }));

        await using var context = fixture.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM invoices WHERE id = {0}", seed.InvoiceId));

        AssertViolates(exception, PostgresErrorCodes.ForeignKeyViolation, "fk_payments_invoices_invoice_id");
    }

    // Step 1 has no factory yet (Invoice.RecordPayment comes in step 3): build the row the way EF does.
    // overrides: an anonymous object whose properties replace the defaults below
    private static Payment NewPayment(Guid? invoiceId, object overrides)
    {
        var payment = TestEntities.New<Payment>(new
        {
            InvoiceId = invoiceId,
            Amount = 500_000m,
            Method = PaymentMethod.Cash,
            PaymentDate = PaidAt
        });
        return TestEntities.Set(payment, overrides);
    }

    private async Task InsertAsync(Payment payment)
    {
        await using var context = fixture.CreateContext();
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
    }

    private static void AssertViolates(Exception? exception, string sqlState, string constraintName)
    {
        var postgresException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    private async Task<(Guid InvoiceId, Guid StaffId, Guid TenantId)> SeedAsync()
    {
        await using var context = fixture.CreateContext();
        return await TestInvoices.SeedAsync(context);
    }
}
