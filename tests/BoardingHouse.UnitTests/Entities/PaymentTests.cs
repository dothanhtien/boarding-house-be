using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid InvoiceId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static PaymentDetails Details(
        decimal amount = 1_000_000,
        DateTimeOffset? paymentDate = null,
        Guid? payerUserId = null,
        string? payerNameOverride = null,
        string? transactionReference = null,
        string? note = null) =>
        new(amount, PaymentMethod.BankTransfer, paymentDate ?? Now.AddDays(-1), payerUserId, payerNameOverride,
            transactionReference, note);

    private static Payment Record(PaymentDetails? details = null) =>
        Payment.ForInvoice(InvoiceId, details ?? Details(), ActorId, Now);

    [Fact]
    public void ForInvoice_CopiesTheDetailsAndStartsPending()
    {
        var payerUserId = Guid.NewGuid();

        var payment = Record(Details(payerUserId: payerUserId, transactionReference: "FT2606010001", note: "Paid at the office"));

        Assert.Equal(InvoiceId, payment.InvoiceId);
        Assert.Null(payment.DepositReturnId);
        Assert.Equal(payerUserId, payment.PayerUserId);
        Assert.Null(payment.PayerNameOverride);
        Assert.Equal(1_000_000, payment.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("FT2606010001", payment.TransactionReference);
        Assert.Equal(Now.AddDays(-1), payment.PaymentDate);
        Assert.Equal("Paid at the office", payment.Note);
        Assert.Equal(ActorId, payment.CreatedBy);
        Assert.Null(payment.ConfirmedAt);
        Assert.Null(payment.VoidedAt);
    }

    [Fact]
    public void ForInvoice_TrimsTheTextAndTurnsBlankIntoNull()
    {
        var payment = Record(Details(payerNameOverride: "  Tenant's mother  ", transactionReference: "   ", note: " "));

        Assert.Equal("Tenant's mother", payment.PayerNameOverride);
        Assert.Null(payment.TransactionReference);
        Assert.Null(payment.Note);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ForInvoice_AmountNotPositive_Throws(int amount)
    {
        var ex = Assert.Throws<DomainException>(() => Record(Details(amount: amount)));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal("Amount must be greater than 0", ex.Message);
    }

    [Fact]
    public void ForInvoice_AmountWithMoreThanTwoDecimals_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => Record(Details(amount: 1_000.005m)));

        Assert.Equal(Payment.AmountPrecisionMessage, ex.Message);
    }

    [Fact]
    public void ForInvoice_PaymentDateInTheFuture_Throws()
    {
        var ex = Assert.Throws<DomainException>(
            () => Record(Details(paymentDate: Now + Payment.MaxClockSkew + TimeSpan.FromSeconds(1))));

        Assert.Equal("PaymentDate must not be in the future", ex.Message);
    }

    [Fact]
    public void ForInvoice_PaymentDateWithinTheClockSkew_IsAccepted()
    {
        var payment = Record(Details(paymentDate: Now + Payment.MaxClockSkew));

        Assert.Equal(Now + Payment.MaxClockSkew, payment.PaymentDate);
    }

    [Fact]
    public void ForInvoice_PayerUserAndPayerName_Throws()
    {
        var ex = Assert.Throws<DomainException>(
            () => Record(Details(payerUserId: Guid.NewGuid(), payerNameOverride: "Walk-in")));

        Assert.Equal("Send either PayerUserId or PayerNameOverride, not both", ex.Message);
    }

    [Fact]
    public void ForInvoice_TextTooLong_Throws()
    {
        Assert.Equal(
            $"PayerNameOverride must not exceed {Payment.PayerNameOverrideMaxLength} characters",
            Assert.Throws<DomainException>(
                () => Record(Details(payerNameOverride: new string('a', Payment.PayerNameOverrideMaxLength + 1)))).Message);
        Assert.Equal(
            $"TransactionReference must not exceed {Payment.TransactionReferenceMaxLength} characters",
            Assert.Throws<DomainException>(
                () => Record(Details(transactionReference: new string('a', Payment.TransactionReferenceMaxLength + 1)))).Message);
        Assert.Equal(
            $"Note must not exceed {Payment.NoteMaxLength} characters",
            Assert.Throws<DomainException>(
                () => Record(Details(note: new string('a', Payment.NoteMaxLength + 1)))).Message);
    }

    [Fact]
    public void Confirm_Pending_RecordsWhoAndWhen()
    {
        var payment = Record();

        payment.Confirm(ActorId, Now);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(ActorId, payment.ConfirmedBy);
        Assert.Equal(Now, payment.ConfirmedAt);
    }

    [Fact]
    public void Confirm_WithoutActor_IsConfirmedByTheSystem()
    {
        var payment = Record();

        payment.Confirm(confirmedBy: null, Now);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Null(payment.ConfirmedBy);
        Assert.Equal(Now, payment.ConfirmedAt);
    }

    [Fact]
    public void Confirm_AlreadyConfirmed_ThrowsConflictAndKeepsTheFirstConfirmation()
    {
        var payment = Record();
        payment.Confirm(ActorId, Now);

        var ex = Assert.Throws<DomainException>(() => payment.Confirm(Guid.NewGuid(), Now.AddMinutes(1)));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("Only pending payments can be confirmed", ex.Message);
        Assert.Equal(ActorId, payment.ConfirmedBy);
        Assert.Equal(Now, payment.ConfirmedAt);
    }
}
