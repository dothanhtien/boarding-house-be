using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.UnitTests.Entities;

public class InvoiceTests
{
    private static readonly BillingMonth June = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid LeaseId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static InvoiceCalculation Calculation(
        decimal discountAmount = 0,
        decimal vatRate = 10,
        DateOnly? dueDate = null,
        IReadOnlyList<InvoiceIssue>? errors = null)
    {
        IReadOnlyList<InvoiceLine> lines =
        [
            new("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, LeaseId),
            new("Deposit", 1, 2_000_000, 2_000_000, InvoiceItemSourceType.Deposit, LeaseId),
            InvoiceLine.Custom("Faucet repair", 1, 50_000)
        ];

        return new InvoiceCalculation
        {
            BillingMonth = June,
            DueDate = dueDate ?? new DateOnly(2026, 6, 8),
            VatRate = vatRate,
            DiscountAmount = discountAmount,
            Lines = lines,
            Totals = InvoiceTotals.Compute(lines.Select(l => (l.SourceType, l.Amount)), vatRate, 0, discountAmount),
            Errors = errors ?? [],
            Warnings = []
        };
    }

    private static Invoice Create(
        InvoiceCalculation? calculation = null, string? discountReason = null, string? notes = null) =>
        Invoice.Create(
            calculation ?? Calculation(), OrganizationId, LeaseId, "INV-202606-0001", discountReason, notes, Now, ActorId);

    [Fact]
    public void Create_CopiesLinesInOrderAndTotalsMatchTheCalculation()
    {
        var calculation = Calculation();

        var invoice = Create(calculation);

        Assert.Equal(OrganizationId, invoice.OrganizationId);
        Assert.Equal(LeaseId, invoice.LeaseId);
        Assert.Equal("INV-202606-0001", invoice.InvoiceNumber);
        Assert.Equal(June, invoice.BillingMonth);
        Assert.Equal(new DateOnly(2026, 6, 8), invoice.DueDate);
        Assert.Equal(10, invoice.VatRate);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(ActorId, invoice.CreatedBy);

        var items = invoice.Items.ToList();
        Assert.Equal(calculation.Lines.Select(l => l.Description), items.Select(i => i.Description));
        Assert.Equal([0, 1, 2], items.Select(i => i.SortOrder));
        Assert.All(items, i => Assert.Equal(invoice.Id, i.InvoiceId));
        Assert.All(items, i => Assert.Equal(Now, i.CreatedAt));
        Assert.Equal(calculation.Lines.Select(l => l.SourceId), items.Select(i => i.SourceId));

        // VAT on everything but the deposit: 10% of 3,050,000
        Assert.Equal(calculation.Totals.Subtotal, invoice.Subtotal);
        Assert.Equal(calculation.Totals.VatAmount, invoice.VatAmount);
        Assert.Equal(calculation.Totals.TotalAmount, invoice.TotalAmount);
        Assert.Equal(305_000, invoice.VatAmount);
        Assert.Equal(5_355_000, invoice.TotalAmount);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(invoice.TotalAmount, invoice.RemainingAmount);
    }

    [Fact]
    public void Create_AddsPendingStatusHistory()
    {
        var invoice = Create();

        var history = Assert.Single(invoice.StatusHistories);
        Assert.Equal(invoice.Id, history.InvoiceId);
        Assert.Null(history.OldStatus);
        Assert.Equal(InvoiceStatus.Pending, history.NewStatus);
        Assert.Equal(ActorId, history.ChangedBy);
        Assert.Equal(Now, history.ChangedAt);
    }

    [Fact]
    public void Create_CalculationWithErrors_Throws()
    {
        var calculation = Calculation(errors: [new InvoiceIssue(InvoiceIssueCode.MissingReading, "No reading")]);

        Assert.Throws<InvalidOperationException>(() => Create(calculation));
    }

    [Fact]
    public void Create_WithDiscount_TrimsTheReasonAndSubtractsIt()
    {
        var invoice = Create(Calculation(discountAmount: 55_000), discountReason: "  Regular tenant  ");

        Assert.Equal(55_000, invoice.DiscountAmount);
        Assert.Equal("Regular tenant", invoice.DiscountReason);
        Assert.Equal(5_300_000, invoice.TotalAmount);
    }

    [Fact]
    public void Create_WithoutDiscount_DropsDiscountReason()
    {
        var invoice = Create(discountReason: "Regular tenant");

        Assert.Null(invoice.DiscountReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_DiscountWithoutReason_Throws(string? reason)
    {
        var ex = Assert.Throws<DomainException>(() => Create(Calculation(discountAmount: 10_000), discountReason: reason));

        Assert.Equal("DiscountReason is required when DiscountAmount is greater than 0", ex.Message);
    }

    [Fact]
    public void Create_DiscountReasonTooLong_Throws()
    {
        Assert.Throws<DomainException>(
            () => Create(Calculation(discountAmount: 10_000), discountReason: new string('a', Invoice.DiscountReasonMaxLength + 1)));
    }

    [Fact]
    public void Create_NotesTooLong_Throws()
    {
        Assert.Throws<DomainException>(() => Create(notes: new string('a', Invoice.NotesMaxLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankNotes_AreStoredAsNull(string? notes)
    {
        Assert.Null(Create(notes: notes).Notes);
    }

    [Fact]
    public void Create_Notes_AreKeptAsWritten()
    {
        const string notes = "  Line 1\nLine 2  ";

        Assert.Equal(notes, Create(notes: notes).Notes);
    }

    [Fact]
    public void Create_DueDateBeforeTheBillingMonth_Throws()
    {
        Assert.Throws<DomainException>(() => Create(Calculation(dueDate: new DateOnly(2026, 5, 31))));
    }

    [Fact]
    public void Create_InvalidCustomLine_Throws()
    {
        var calculation = Calculation() with { Lines = [InvoiceLine.Custom("Refund", 1, 0)] };

        Assert.Throws<DomainException>(() => Create(calculation));
    }

    [Fact]
    public void Details_ReturnsTheEditableFields()
    {
        var invoice = Create(Calculation(discountAmount: 10_000), discountReason: "Reason", notes: "Note");

        Assert.Equal(new InvoiceDetails(10_000, "Reason", "Note", new DateOnly(2026, 6, 8)), invoice.Details);
    }

    [Theory]
    [InlineData(7, "INV-202606-0007")]
    [InlineData(12345, "INV-202606-12345")]
    public void FormatNumber_PadsToFourDigits(int number, string expected)
    {
        Assert.Equal(expected, Invoice.FormatNumber(June, number));
    }

    [Theory]
    [InlineData(null, 1, 100, "Description is required")]
    [InlineData("  ", 1, 100, "Description is required")]
    [InlineData("long", 1, 100, "Description must not exceed 255 characters")]
    [InlineData("Repair", null, 100, "Quantity must be greater than 0")]
    [InlineData("Repair", 0, 100, "Quantity must be greater than 0")]
    [InlineData("Repair", -1, 100, "Quantity must be greater than 0")]
    [InlineData("Repair", 1, null, "UnitPrice is required")]
    [InlineData("Repair", 1, 0, "UnitPrice must not be 0")]
    public void EnsureCustomValid_InvalidValues_Throws(string? description, int? quantity, int? unitPrice, string message)
    {
        if (description == "long")
        {
            description = new string('a', InvoiceItem.DescriptionMaxLength + 1);
        }

        var ex = Assert.Throws<DomainException>(() => InvoiceItem.EnsureCustomValid(description, quantity, unitPrice));

        Assert.Equal(message, ex.Message);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void EnsureCustomValid_ValidValues_DoesNotThrow(int unitPrice)
    {
        InvoiceItem.EnsureCustomValid("  Repair  ", 1.5m, unitPrice);
    }
}
