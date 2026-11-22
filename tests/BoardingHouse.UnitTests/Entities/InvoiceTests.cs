using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

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
    public void Create_WithoutActor_LeavesCreatedByToTheAuditInterceptorAndTheHistoryWithoutActor()
    {
        var invoice = Invoice.Create(
            Calculation(), OrganizationId, LeaseId, "INV-202606-0001", discountReason: null, notes: null, Now, createdBy: null);

        Assert.Equal(Guid.Empty, invoice.CreatedBy);
        Assert.Null(Assert.Single(invoice.StatusHistories).ChangedBy);
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
    [InlineData(1_000_000_000_000_000L, 1_000_000_000_000_000L)] // product overflows decimal
    [InlineData(100_000_000L, -100_000_000L)] // 10^16: one digit over the column
    public void EnsureCustomValid_AmountOutOfRange_Throws(long quantity, long unitPrice)
    {
        var ex = Assert.Throws<DomainException>(() => InvoiceItem.EnsureCustomValid("Repair", quantity, unitPrice));

        Assert.Equal(InvoiceItem.AmountOutOfRangeMessage, ex.Message);
    }

    [Fact]
    public void EnsureCustomValid_AmountAtLimit_DoesNotThrow()
    {
        InvoiceItem.EnsureCustomValid("Repair", 1, -InvoiceItem.MaxAmount);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public void EnsureCustomValid_ValidValues_DoesNotThrow(int unitPrice)
    {
        InvoiceItem.EnsureCustomValid("  Repair  ", 1.5m, unitPrice);
    }

    private static readonly DateTimeOffset Later = Now.AddDays(1);

    private static InvoiceItem ItemOf(Invoice invoice, InvoiceItemSourceType sourceType) =>
        invoice.Items.Single(i => i.SourceType == sourceType);

    private static InvoiceCustomItemChange Add(string? description = "Cleaning", decimal? quantity = 1, decimal? unitPrice = 25_000) =>
        new(null, description, quantity, unitPrice, IsDeleted: false);

    private static InvoiceCustomItemChange Edit(
        Guid itemId, string? description = null, decimal? quantity = null, decimal? unitPrice = null) =>
        new(itemId, description, quantity, unitPrice, IsDeleted: false);

    private static InvoiceCustomItemChange Delete(Guid itemId) => new(itemId, null, null, null, IsDeleted: true);

    [Theory]
    [InlineData(InvoiceStatus.Pending, 0, true)]
    [InlineData(InvoiceStatus.Overdue, 0, true)]
    [InlineData(InvoiceStatus.Pending, 1, false)]
    [InlineData(InvoiceStatus.Overdue, 1, false)]
    [InlineData(InvoiceStatus.Partial, 0, false)]
    [InlineData(InvoiceStatus.Paid, 0, false)]
    [InlineData(InvoiceStatus.Cancelled, 0, false)]
    public void IsEditable_OnlyUnpaidPendingOrOverdue(InvoiceStatus status, int paidAmount, bool expected)
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = status, PaidAmount = (decimal)paidAmount });

        Assert.Equal(expected, invoice.IsEditable);
    }

    [Fact]
    public void Update_NoChanges_KeepsEverything()
    {
        var invoice = Create(Calculation(discountAmount: 5_000), discountReason: "Reason", notes: "Note");
        var itemsBefore = invoice.Items.Select(i => (i.Id, i.Description, i.Amount, i.SortOrder)).ToList();

        invoice.Update(invoice.Details, [], Later);

        Assert.Equal(itemsBefore, invoice.Items.Select(i => (i.Id, i.Description, i.Amount, i.SortOrder)));
        Assert.Equal(new InvoiceDetails(5_000, "Reason", "Note", new DateOnly(2026, 6, 8)), invoice.Details);
        Assert.Equal(5_350_000, invoice.TotalAmount);
    }

    [Fact]
    public void Update_DiscountAndCustomItems_RecalculatesTotals()
    {
        var invoice = Create();

        invoice.Update(
            invoice.Details with { DiscountAmount = 100_000, DiscountReason = "  Loyal tenant  " },
            [Add("Cleaning", 2, 25_000)],
            Later);

        // Subtotal 5,100,000; VAT 10% of 3,100,000 (the deposit stays excluded) = 310,000
        Assert.Equal(100_000, invoice.DiscountAmount);
        Assert.Equal("Loyal tenant", invoice.DiscountReason);
        Assert.Equal(5_100_000, invoice.Subtotal);
        Assert.Equal(310_000, invoice.VatAmount);
        Assert.Equal(5_310_000, invoice.TotalAmount);
        Assert.Equal(10, invoice.VatRate);
        Assert.Equal(invoice.TotalAmount, invoice.RemainingAmount);
    }

    [Fact]
    public void Update_DetailsOnly_ChangesNotesAndDueDate()
    {
        var invoice = Create(notes: "Old");

        invoice.Update(invoice.Details with { Notes = "  New note  ", DueDate = new DateOnly(2026, 7, 15) }, [], Later);

        Assert.Equal("  New note  ", invoice.Notes);
        Assert.Equal(new DateOnly(2026, 7, 15), invoice.DueDate);
        Assert.Equal(5_355_000, invoice.TotalAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_BlankNotes_AreStoredAsNull(string? notes)
    {
        var invoice = Create(notes: "Old");

        invoice.Update(invoice.Details with { Notes = notes }, [], Later);

        Assert.Null(invoice.Notes);
    }

    [Fact]
    public void Update_DueDateOnTheFirstDayOfTheBillingMonth_IsAllowed()
    {
        var invoice = Create();

        invoice.Update(invoice.Details with { DueDate = June.FirstDay }, [], Later);

        Assert.Equal(June.FirstDay, invoice.DueDate);
    }

    [Fact]
    public void Update_AddsCustomItemsAfterTheLastSortOrder()
    {
        var invoice = Create();

        invoice.Update(invoice.Details, [Add("  Cleaning  ", 2, 25_000), Add("Parking", 1, 100_000)], Later);

        var added = invoice.Items.Where(i => i.CreatedAt == Later).OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(5, invoice.Items.Count);
        Assert.Equal(["Cleaning", "Parking"], added.Select(i => i.Description));
        Assert.Equal([3, 4], added.Select(i => i.SortOrder));
        Assert.Equal([50_000m, 100_000m], added.Select(i => i.Amount));
        Assert.All(added, i =>
        {
            Assert.True(i.IsCustom);
            Assert.Null(i.SourceId);
            Assert.Equal(invoice.Id, i.InvoiceId);
        });
    }

    [Fact]
    public void Update_AfterDeletingTheLastItem_AddsAfterTheHighestRemainingSortOrder()
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);

        invoice.Update(invoice.Details, [Delete(faucet.Id), Add()], Later);

        // SortOrder is computed before the delete is applied: the new line goes after the old maximum
        var added = Assert.Single(invoice.Items, i => i.IsCustom);
        Assert.Equal(3, added.SortOrder);
    }

    [Fact]
    public void Update_NegativeCustomItem_IsAllowedAsARefund()
    {
        var invoice = Create();

        invoice.Update(invoice.Details, [Add("Refund", 1, -50_000)], Later);

        Assert.Equal(5_000_000, invoice.Subtotal);
        Assert.Equal(300_000, invoice.VatAmount);
        Assert.Equal(5_300_000, invoice.TotalAmount);
    }

    [Fact]
    public void Update_EditsCustomItem_RecomputesItsAmount()
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);

        invoice.Update(invoice.Details, [Edit(faucet.Id, quantity: 3)], Later);

        Assert.Equal("Faucet repair", faucet.Description);
        Assert.Equal(3, faucet.Quantity);
        Assert.Equal(50_000, faucet.UnitPrice);
        Assert.Equal(150_000, faucet.Amount);
        Assert.Equal(2, faucet.SortOrder);
        Assert.Equal(Now, faucet.CreatedAt);
        // Subtotal 5,150,000; VAT 10% of 3,150,000 = 315,000
        Assert.Equal(5_150_000, invoice.Subtotal);
        Assert.Equal(315_000, invoice.VatAmount);
        Assert.Equal(5_465_000, invoice.TotalAmount);
        Assert.Equal(invoice.Subtotal, invoice.Items.Sum(i => i.Amount));
    }

    [Fact]
    public void Update_EditsEveryFieldOfACustomItem_TrimsTheDescription()
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);

        invoice.Update(invoice.Details, [Edit(faucet.Id, "  Sink repair  ", 1.5m, 30_000)], Later);

        Assert.Equal("Sink repair", faucet.Description);
        Assert.Equal(1.5m, faucet.Quantity);
        Assert.Equal(30_000, faucet.UnitPrice);
        Assert.Equal(45_000, faucet.Amount);
    }

    [Fact]
    public void Update_DeletesCustomItem()
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);

        invoice.Update(invoice.Details, [Delete(faucet.Id)], Later);

        Assert.DoesNotContain(invoice.Items, i => i.Id == faucet.Id);
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal(5_000_000, invoice.Subtotal);
        Assert.Equal(300_000, invoice.VatAmount);
        Assert.Equal(5_300_000, invoice.TotalAmount);
    }

    [Fact]
    public void Update_DeletesEditsAndAddsInOneCall()
    {
        var calculation = Calculation() with
        {
            Lines =
            [
                new("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, LeaseId),
                InvoiceLine.Custom("Faucet repair", 1, 50_000),
                InvoiceLine.Custom("Light bulb", 2, 20_000)
            ]
        };
        var invoice = Create(calculation);
        var faucet = invoice.Items.Single(i => i.Description == "Faucet repair");
        var bulb = invoice.Items.Single(i => i.Description == "Light bulb");

        invoice.Update(invoice.Details, [Delete(faucet.Id), Edit(bulb.Id, unitPrice: 30_000), Add("Cleaning", 1, 100_000)], Later);

        Assert.Equal(["Rent 06/2026", "Light bulb", "Cleaning"], invoice.Items.OrderBy(i => i.SortOrder).Select(i => i.Description));
        Assert.Equal(60_000, bulb.Amount);
        // Subtotal 3,160,000; VAT 316,000
        Assert.Equal(3_160_000, invoice.Subtotal);
        Assert.Equal(316_000, invoice.VatAmount);
        Assert.Equal(3_476_000, invoice.TotalAmount);
    }

    [Theory]
    [InlineData(InvoiceItemSourceType.Rent, false)]
    [InlineData(InvoiceItemSourceType.Rent, true)]
    [InlineData(InvoiceItemSourceType.Deposit, false)]
    [InlineData(InvoiceItemSourceType.Deposit, true)]
    public void Update_NonCustomItem_ThrowsConflict(InvoiceItemSourceType sourceType, bool delete)
    {
        var invoice = Create();
        var item = ItemOf(invoice, sourceType);

        var ex = Assert.Throws<DomainException>(() => invoice.Update(
            invoice.Details, [delete ? Delete(item.Id) : Edit(item.Id, unitPrice: 1)], Later));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Contains(invoice.Items, i => i.Id == item.Id);
    }

    [Fact]
    public void Update_UnknownItem_ThrowsNotFound()
    {
        var invoice = Create();
        var unknownId = Guid.NewGuid();

        var ex = Assert.Throws<DomainException>(() => invoice.Update(invoice.Details, [Edit(unknownId, quantity: 2)], Later));

        Assert.Equal(DomainErrorKind.NotFound, ex.Kind);
        Assert.Equal($"Invoice item '{unknownId}' not found", ex.Message);
    }

    [Theory]
    [InlineData(InvoiceStatus.Partial, 0)]
    [InlineData(InvoiceStatus.Paid, 0)]
    [InlineData(InvoiceStatus.Cancelled, 0)]
    [InlineData(InvoiceStatus.Pending, 1)]
    [InlineData(InvoiceStatus.Overdue, 1)]
    public void Update_NotEditable_ThrowsConflict(InvoiceStatus status, int paidAmount)
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = status, PaidAmount = (decimal)paidAmount });

        var ex = Assert.Throws<DomainException>(() => invoice.Update(invoice.Details with { Notes = "x" }, [], Later));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("Only unpaid pending or overdue invoices can be edited", ex.Message);
        Assert.Null(invoice.Notes);
    }

    [Fact]
    public void Update_OverdueUnpaid_IsEditable()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Overdue });

        invoice.Update(invoice.Details with { Notes = "Reminder sent" }, [Add()], Later);

        Assert.Equal("Reminder sent", invoice.Notes);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(4, invoice.Items.Count);
    }

    [Fact]
    public void Update_DueDateBeforeBillingMonth_Throws()
    {
        var invoice = Create();

        var ex = Assert.Throws<DomainException>(
            () => invoice.Update(invoice.Details with { DueDate = new DateOnly(2026, 5, 31) }, [], Later));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal("DueDate must not be before the billing month", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Update_DiscountWithoutReason_Throws(string? reason)
    {
        var invoice = Create();

        var ex = Assert.Throws<DomainException>(
            () => invoice.Update(invoice.Details with { DiscountAmount = 10_000, DiscountReason = reason }, [], Later));

        Assert.Equal("DiscountReason is required when DiscountAmount is greater than 0", ex.Message);
    }

    [Fact]
    public void Update_NegativeDiscount_Throws()
    {
        var invoice = Create();

        Assert.Throws<DomainException>(
            () => invoice.Update(invoice.Details with { DiscountAmount = -1, DiscountReason = "Reason" }, [], Later));
    }

    [Fact]
    public void Update_NotesTooLong_Throws()
    {
        var invoice = Create();

        Assert.Throws<DomainException>(
            () => invoice.Update(invoice.Details with { Notes = new string('a', Invoice.NotesMaxLength + 1) }, [], Later));
    }

    [Fact]
    public void Update_ZeroDiscount_DropsDiscountReason()
    {
        var invoice = Create(Calculation(discountAmount: 10_000), discountReason: "Reason");

        invoice.Update(invoice.Details with { DiscountAmount = 0 }, [], Later);

        Assert.Equal(0, invoice.DiscountAmount);
        Assert.Null(invoice.DiscountReason);
        Assert.Equal(5_355_000, invoice.TotalAmount);
    }

    [Fact]
    public void Update_RemovingEveryItem_Throws()
    {
        var invoice = Create(Calculation() with { Lines = [InvoiceLine.Custom("Faucet repair", 1, 50_000)] });
        var faucet = invoice.Items.Single();

        var ex = Assert.Throws<DomainException>(() => invoice.Update(invoice.Details, [Delete(faucet.Id)], Later));

        Assert.Equal("An invoice must keep at least one item", ex.Message);
        Assert.Single(invoice.Items);
    }

    [Fact]
    public void Update_ReplacingTheOnlyItem_IsAllowed()
    {
        var invoice = Create(Calculation() with { Lines = [InvoiceLine.Custom("Faucet repair", 1, 50_000)] });
        var faucet = invoice.Items.Single();

        invoice.Update(invoice.Details, [Delete(faucet.Id), Add("Sink repair", 1, 80_000)], Later);

        Assert.Equal("Sink repair", Assert.Single(invoice.Items).Description);
    }

    [Fact]
    public void Update_NegativeTotal_Throws()
    {
        var invoice = Create();

        var ex = Assert.Throws<DomainException>(() => invoice.Update(
            invoice.Details with { DiscountAmount = 6_000_000, DiscountReason = "Too much" }, [], Later));

        Assert.Equal("The invoice total must not be negative", ex.Message);
        Assert.Equal(0, invoice.DiscountAmount);
    }

    [Fact]
    public void Update_DiscountEqualToTotal_LeavesAZeroTotal()
    {
        var invoice = Create();

        invoice.Update(invoice.Details with { DiscountAmount = 5_355_000, DiscountReason = "Free month" }, [], Later);

        Assert.Equal(0, invoice.TotalAmount);
    }

    [Fact]
    public void Update_TooManyCustomItems_Throws()
    {
        var invoice = Create();

        var ex = Assert.Throws<DomainException>(() => invoice.Update(
            invoice.Details, Enumerable.Range(0, Invoice.MaxCustomItems).Select(i => Add($"Item {i}")).ToList(), Later));

        Assert.Equal($"At most {Invoice.MaxCustomItems} custom items are allowed", ex.Message);
        Assert.Equal(3, invoice.Items.Count);
    }

    [Fact]
    public void Update_UpToTheCustomItemLimit_IsAllowed()
    {
        var invoice = Create();

        invoice.Update(
            invoice.Details, Enumerable.Range(0, Invoice.MaxCustomItems - 1).Select(i => Add($"Item {i}")).ToList(), Later);

        Assert.Equal(Invoice.MaxCustomItems, invoice.Items.Count(i => i.IsCustom));
    }

    [Fact]
    public void Update_DeletedItemsFreeTheirCustomSlot()
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);
        List<InvoiceCustomItemChange> changes = [Delete(faucet.Id), .. Enumerable.Range(0, Invoice.MaxCustomItems).Select(i => Add($"Item {i}"))];

        invoice.Update(invoice.Details, changes, Later);

        Assert.Equal(Invoice.MaxCustomItems, invoice.Items.Count(i => i.IsCustom));
    }

    [Theory]
    [InlineData(null, 1, 100)]
    [InlineData("Cleaning", null, 100)]
    [InlineData("Cleaning", 1, null)]
    [InlineData("Cleaning", 0, 100)]
    [InlineData("Cleaning", 1, 0)]
    public void Update_InvalidNewItem_Throws(string? description, int? quantity, int? unitPrice)
    {
        var invoice = Create();

        Assert.Throws<DomainException>(() => invoice.Update(invoice.Details, [Add(description, quantity, unitPrice)], Later));
        Assert.Equal(3, invoice.Items.Count);
    }

    [Theory]
    [InlineData("  ", null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, 0)]
    public void Update_InvalidEdit_Throws(string? description, int? quantity, int? unitPrice)
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);

        Assert.Throws<DomainException>(() => invoice.Update(invoice.Details, [Edit(faucet.Id, description, quantity, unitPrice)], Later));
        Assert.Equal("Faucet repair", faucet.Description);
        Assert.Equal(50_000, faucet.Amount);
    }

    [Fact]
    public void Update_InvalidChange_LeavesTheInvoiceUnchanged()
    {
        var invoice = Create(notes: "Old");
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);
        var rent = ItemOf(invoice, InvoiceItemSourceType.Rent);
        var itemsBefore = invoice.Items.Select(i => (i.Id, i.Description, i.Quantity, i.UnitPrice, i.Amount)).ToList();

        Assert.Throws<DomainException>(() => invoice.Update(
            invoice.Details with { DiscountAmount = 10_000, DiscountReason = "Reason", Notes = "New" },
            [Edit(faucet.Id, "Changed", 5, 1_000), Add(), Edit(rent.Id, unitPrice: 1)],
            Later));

        Assert.Equal(itemsBefore, invoice.Items.Select(i => (i.Id, i.Description, i.Quantity, i.UnitPrice, i.Amount)));
        Assert.Equal(0, invoice.DiscountAmount);
        Assert.Null(invoice.DiscountReason);
        Assert.Equal("Old", invoice.Notes);
        Assert.Equal(5_050_000, invoice.Subtotal);
        Assert.Equal(5_355_000, invoice.TotalAmount);
    }

    [Fact]
    public void Update_InvalidNewItemAfterValidEdit_LeavesTheEditedItemUnchanged()
    {
        var invoice = Create();
        var faucet = ItemOf(invoice, InvoiceItemSourceType.Custom);

        Assert.Throws<DomainException>(() => invoice.Update(
            invoice.Details, [Edit(faucet.Id, quantity: 10), Add(unitPrice: 0)], Later));

        Assert.Equal(1, faucet.Quantity);
        Assert.Equal(3, invoice.Items.Count);
    }

    [Fact]
    public void Update_DoesNotAddStatusHistory()
    {
        var invoice = Create();

        invoice.Update(invoice.Details with { Notes = "x" }, [Add()], Later);

        Assert.Single(invoice.StatusHistories);
    }

    // Create(): total 5,355,000, due 2026-06-08
    private static readonly DateOnly InvoiceDueDate = new(2026, 6, 8);
    private static readonly LateFeePolicy FivePercent = new(LateFeeType.Percent, 5, graceDays: null);

    [Theory]
    [InlineData(0, "2026-06-08", InvoiceStatus.Pending)]
    [InlineData(0, "2026-06-09", InvoiceStatus.Overdue)]
    [InlineData(1_000, "2026-06-08", InvoiceStatus.Partial)]
    [InlineData(1_000, "2026-06-09", InvoiceStatus.Overdue)]
    [InlineData(5_355_000, "2026-06-09", InvoiceStatus.Paid)]
    [InlineData(6_000_000, "2026-06-08", InvoiceStatus.Paid)]
    public void RefreshStatus_DerivesTheStatusFromPaidTotalAndDueDate(int paidAmount, string today, InvoiceStatus expected)
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { PaidAmount = (decimal)paidAmount });

        invoice.RefreshStatus(DateOnly.Parse(today), LateFeePolicy.None, ActorId, Later);

        Assert.Equal(expected, invoice.Status);
    }

    [Fact]
    public void RefreshStatus_BecomingOverdue_AddsTheLateFeeAndRecordsTheChange()
    {
        var invoice = Create();

        var changed = invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);

        Assert.True(changed);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(267_750, invoice.LateFeeAmount);
        Assert.Equal(5_622_750, invoice.TotalAmount);
        Assert.Equal(5_622_750, invoice.RemainingAmount);
        // Only the fee moves: total = subtotal + vat + late fee - discount still holds (DB CHECK)
        Assert.Equal(5_050_000, invoice.Subtotal);
        Assert.Equal(invoice.Subtotal + invoice.VatAmount + invoice.LateFeeAmount - invoice.DiscountAmount, invoice.TotalAmount);

        var history = invoice.StatusHistories.Last();
        Assert.Equal(invoice.Id, history.InvoiceId);
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(InvoiceStatus.Overdue, history.NewStatus);
        Assert.Equal(Invoice.OverdueReason, history.Reason);
        Assert.Null(history.ChangedBy);
        Assert.Equal(Later, history.ChangedAt);
    }

    [Fact]
    public void RefreshStatus_BecomingOverdueWithFixedFee_AddsTheFixedAmount()
    {
        var invoice = Create();

        invoice.RefreshStatus(
            InvoiceDueDate.AddDays(1), new LateFeePolicy(LateFeeType.Fixed, 100_000, graceDays: null), changedBy: null, Later);

        Assert.Equal(100_000, invoice.LateFeeAmount);
        Assert.Equal(5_455_000, invoice.TotalAmount);
    }

    [Fact]
    public void RefreshStatus_PartialBecomingOverdue_ChargesTheFeeOnTheWholeTotal()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Partial, PaidAmount = 1_000_000m });

        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(267_750, invoice.LateFeeAmount);
        Assert.Equal(4_622_750, invoice.RemainingAmount);
        Assert.Equal(InvoiceStatus.Partial, invoice.StatusHistories.Last().OldStatus);
    }

    [Fact]
    public void RefreshStatus_WithoutLateFeePolicy_BecomesOverdueWithoutAFee()
    {
        var invoice = Create();

        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), LateFeePolicy.None, changedBy: null, Later);

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.Equal(5_355_000, invoice.TotalAmount);
    }

    [Fact]
    public void RefreshStatus_LeavingAndReturningToOverdue_AddsTheLateFeeOnlyOnce()
    {
        var invoice = Create();
        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);

        invoice.RefreshStatus(InvoiceDueDate, FivePercent, ActorId, Later);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);

        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(267_750, invoice.LateFeeAmount);
        Assert.Equal(5_622_750, invoice.TotalAmount);
        Assert.Equal(
            new[] { InvoiceStatus.Pending, InvoiceStatus.Overdue, InvoiceStatus.Pending, InvoiceStatus.Overdue },
            invoice.StatusHistories.Select(h => h.NewStatus));
        Assert.Null(invoice.StatusHistories.ElementAt(2).Reason);
        Assert.Equal(ActorId, invoice.StatusHistories.ElementAt(2).ChangedBy);
    }

    [Fact]
    public void RefreshStatus_OverduePaidInFull_BecomesPaidAndKeepsTheFee()
    {
        var invoice = Create();
        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);
        TestEntities.Set(invoice, new { PaidAmount = invoice.TotalAmount });

        Assert.True(invoice.RefreshStatus(InvoiceDueDate.AddDays(2), FivePercent, ActorId, Later));

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(267_750, invoice.LateFeeAmount);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(InvoiceStatus.Overdue, history.OldStatus);
        Assert.Equal(InvoiceStatus.Paid, history.NewStatus);
        Assert.Null(history.Reason);
        Assert.Equal(ActorId, history.ChangedBy);
    }

    [Fact]
    public void RefreshStatus_OverduePaidOnlyTheTotalBeforeTheFee_StaysOverdue()
    {
        var invoice = Create();
        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);
        TestEntities.Set(invoice, new { PaidAmount = 5_355_000m });

        Assert.False(invoice.RefreshStatus(InvoiceDueDate.AddDays(2), FivePercent, ActorId, Later));

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(267_750, invoice.RemainingAmount);
    }

    [Fact]
    public void RefreshStatus_PaidThenPaymentVoided_GoesBackToPartial()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Paid, PaidAmount = 1_000_000m });

        Assert.True(invoice.RefreshStatus(InvoiceDueDate, FivePercent, ActorId, Later));

        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(InvoiceStatus.Paid, history.OldStatus);
        Assert.Null(history.Reason);
    }

    [Fact]
    public void RefreshStatus_SameStatus_ReturnsFalseWithoutHistory()
    {
        var invoice = Create();

        Assert.False(invoice.RefreshStatus(InvoiceDueDate, FivePercent, ActorId, Later));

        Assert.Single(invoice.StatusHistories);
        Assert.Equal(0, invoice.LateFeeAmount);
    }

    [Fact]
    public void RefreshStatus_AlreadyOverdue_DoesNotAddTheFee()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Overdue });

        Assert.False(invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later));

        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.Single(invoice.StatusHistories);
    }

    [Fact]
    public void RefreshStatus_CancelledInvoice_NeverChanges()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Cancelled });

        Assert.False(invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later));

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.Single(invoice.StatusHistories);
    }

    [Fact]
    public void RefreshStatus_ZeroTotal_IsPaid()
    {
        var invoice = Create();
        invoice.Update(invoice.Details with { DiscountAmount = 5_355_000, DiscountReason = "Free month" }, [], Later);

        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, ActorId, Later);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
    }

    [Theory]
    [InlineData(InvoiceStatus.Pending)]
    [InlineData(InvoiceStatus.Overdue)]
    public void Cancel_UnpaidInvoice_CancelsWithTheTrimmedReason(InvoiceStatus status)
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = status });

        invoice.Cancel("  Wrong meter reading  ", ActorId, Later);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(invoice.Id, history.InvoiceId);
        Assert.Equal(status, history.OldStatus);
        Assert.Equal(InvoiceStatus.Cancelled, history.NewStatus);
        Assert.Equal("Wrong meter reading", history.Reason);
        Assert.Equal(ActorId, history.ChangedBy);
        Assert.Equal(Later, history.ChangedAt);
    }

    [Fact]
    public void Cancel_KeepsTheNumberAmountsAndItems()
    {
        var invoice = Create();
        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, changedBy: null, Later);

        invoice.Cancel("Wrong meter reading", ActorId, Later);

        Assert.Equal("INV-202606-0001", invoice.InvoiceNumber);
        Assert.Equal(267_750, invoice.LateFeeAmount);
        Assert.Equal(5_622_750, invoice.TotalAmount);
        Assert.Equal(3, invoice.Items.Count);
        Assert.False(invoice.IsEditable);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ThrowsConflict()
    {
        var invoice = Create();
        invoice.Cancel("Wrong meter reading", ActorId, Later);

        var ex = Assert.Throws<DomainException>(() => invoice.Cancel("Again", ActorId, Later));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("The invoice is already cancelled", ex.Message);
        Assert.Equal(2, invoice.StatusHistories.Count);
    }

    [Theory]
    [InlineData(InvoiceStatus.Partial, 1_000)]
    [InlineData(InvoiceStatus.Overdue, 1_000)]
    [InlineData(InvoiceStatus.Paid, 5_355_000)]
    public void Cancel_WithConfirmedPayments_ThrowsConflict(InvoiceStatus status, int paidAmount)
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = status, PaidAmount = (decimal)paidAmount });

        var ex = Assert.Throws<DomainException>(() => invoice.Cancel("Wrong meter reading", ActorId, Later));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("An invoice with confirmed payments cannot be cancelled", ex.Message);
        Assert.Equal(status, invoice.Status);
        Assert.Single(invoice.StatusHistories);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_BlankReason_Throws(string reason)
    {
        var invoice = Create();

        var ex = Assert.Throws<DomainException>(() => invoice.Cancel(reason, ActorId, Later));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal("Reason is required", ex.Message);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Single(invoice.StatusHistories);
    }

    [Fact]
    public void Cancel_ReasonTooLong_Throws()
    {
        var invoice = Create();

        var ex = Assert.Throws<DomainException>(
            () => invoice.Cancel(new string('a', Invoice.CancelReasonMaxLength + 1), ActorId, Later));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal($"Reason must not exceed {Invoice.CancelReasonMaxLength} characters", ex.Message);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
    }

    [Fact]
    public void Cancel_ReasonAtMaxLengthAfterTrim_IsAllowed()
    {
        var invoice = Create();

        invoice.Cancel($"  {new string('a', Invoice.CancelReasonMaxLength)}  ", ActorId, Later);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(Invoice.CancelReasonMaxLength, invoice.StatusHistories.Last().Reason!.Length);
    }

    private static PaymentDetails Cash(decimal amount) =>
        new(amount, PaymentMethod.Cash, Now, PayerUserId: null, PayerNameOverride: null, TransactionReference: null, Note: null);

    [Fact]
    public void RecordPayment_ReturnsAPendingPaymentOfTheInvoiceAndLeavesPaidAmount()
    {
        var invoice = Create();

        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);

        Assert.Equal(invoice.Id, payment.InvoiceId);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(ActorId, payment.CreatedBy);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
    }

    [Fact]
    public void RecordPayment_TheWholeRemainingAmount_IsAccepted()
    {
        var invoice = Create();

        var payment = invoice.RecordPayment(Cash(5_355_000), ActorId, Now);

        Assert.Equal(5_355_000, payment.Amount);
    }

    [Fact]
    public void RecordPayment_MoreThanTheRemainingAmount_Throws()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Partial, PaidAmount = 5_000_000m });

        var ex = Assert.Throws<DomainException>(() => invoice.RecordPayment(Cash(355_001), ActorId, Now));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal("Amount must not exceed the remaining amount of the invoice (355000)", ex.Message);
    }

    [Fact]
    public void RecordPayment_CancelledInvoice_ThrowsConflict()
    {
        var invoice = Create();
        invoice.Cancel("Wrong meter reading", ActorId, Later);

        var ex = Assert.Throws<DomainException>(() => invoice.RecordPayment(Cash(1_000_000), ActorId, Now));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("A cancelled invoice cannot take payments", ex.Message);
    }

    [Fact]
    public void ConfirmPayment_AddsTheAmountButLeavesTheStatusToRefreshStatus()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);

        invoice.ConfirmPayment(payment, ActorId, Later);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(ActorId, payment.ConfirmedBy);
        Assert.Equal(Later, payment.ConfirmedAt);
        Assert.Equal(1_000_000, invoice.PaidAmount);
        Assert.Equal(4_355_000, invoice.RemainingAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
    }

    [Theory]
    [InlineData(1_000_000, InvoiceStatus.Partial)]
    [InlineData(5_355_000, InvoiceStatus.Paid)]
    public void ConfirmPayment_ThenRefreshStatus_RecordsPartialOrPaid(int amount, InvoiceStatus expected)
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(amount), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);

        var changed = invoice.RefreshStatus(InvoiceDueDate, LateFeePolicy.None, ActorId, Later);

        Assert.True(changed);
        Assert.Equal(expected, invoice.Status);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(expected, history.NewStatus);
        Assert.Equal(ActorId, history.ChangedBy);
    }

    [Fact]
    public void ConfirmPayment_ExceedingTheRemainingAmount_ThrowsAndLeavesThePaymentPending()
    {
        var invoice = Create();
        var first = invoice.RecordPayment(Cash(3_000_000), ActorId, Now);
        var second = invoice.RecordPayment(Cash(3_000_000), ActorId, Now);
        invoice.ConfirmPayment(first, ActorId, Later);

        var ex = Assert.Throws<DomainException>(() => invoice.ConfirmPayment(second, ActorId, Later));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal("Amount must not exceed the remaining amount of the invoice (2355000)", ex.Message);
        Assert.Equal(PaymentStatus.Pending, second.Status);
        Assert.Equal(3_000_000, invoice.PaidAmount);
    }

    [Fact]
    public void ConfirmPayment_AlreadyConfirmed_ThrowsConflictBeforeCheckingTheAmount()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(5_355_000), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);

        var ex = Assert.Throws<DomainException>(() => invoice.ConfirmPayment(payment, ActorId, Later));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("Only pending payments can be confirmed", ex.Message);
        Assert.Equal(5_355_000, invoice.PaidAmount);
    }

    [Fact]
    public void ConfirmPayment_CancelledInvoice_ThrowsConflict()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.Cancel("Wrong meter reading", ActorId, Later);

        var ex = Assert.Throws<DomainException>(() => invoice.ConfirmPayment(payment, ActorId, Later));

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal("A cancelled invoice cannot take payments", ex.Message);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void ConfirmPayment_PaymentOfAnotherInvoice_Throws()
    {
        var invoice = Create();
        var payment = Create().RecordPayment(Cash(1_000_000), ActorId, Now);

        Assert.Throws<InvalidOperationException>(() => invoice.ConfirmPayment(payment, ActorId, Later));
        Assert.Equal(0, invoice.PaidAmount);
    }

    [Fact]
    public void VoidPayment_Confirmed_TakesTheAmountOff()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);

        invoice.VoidPayment(payment, "Bounced transfer", ActorId, Later);

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(0, invoice.PaidAmount);
    }

    [Fact]
    public void VoidPayment_Pending_LeavesPaidAmount()
    {
        var invoice = Create();
        var confirmed = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.ConfirmPayment(confirmed, ActorId, Later);
        var pending = invoice.RecordPayment(Cash(2_000_000), ActorId, Now);

        invoice.VoidPayment(pending, "Typed the wrong amount", ActorId, Later);

        Assert.Equal(PaymentStatus.Voided, pending.Status);
        Assert.Equal(1_000_000, invoice.PaidAmount);
    }

    [Theory]
    [InlineData("2026-06-08", InvoiceStatus.Partial)]
    [InlineData("2026-06-09", InvoiceStatus.Overdue)]
    public void VoidPayment_OfAPaidInvoice_ThenRefreshStatus_GoesBackToPartialOrOverdue(string today, InvoiceStatus expected)
    {
        var invoice = Create();
        var first = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        var second = invoice.RecordPayment(Cash(4_355_000), ActorId, Now);
        invoice.ConfirmPayment(first, ActorId, Later);
        invoice.ConfirmPayment(second, ActorId, Later);
        invoice.RefreshStatus(InvoiceDueDate, LateFeePolicy.None, ActorId, Later);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        invoice.VoidPayment(second, "Bounced transfer", ActorId, Later);
        invoice.RefreshStatus(DateOnly.Parse(today), LateFeePolicy.None, ActorId, Later);

        Assert.Equal(1_000_000, invoice.PaidAmount);
        Assert.Equal(expected, invoice.Status);
        Assert.Equal(InvoiceStatus.Paid, invoice.StatusHistories.Last().OldStatus);
    }

    [Fact]
    public void VoidPayment_TheOnlyPaymentPastTheDueDate_BecomesOverdueAndChargesTheLateFee()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(5_355_000), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);
        invoice.RefreshStatus(InvoiceDueDate, FivePercent, ActorId, Later);

        invoice.VoidPayment(payment, "Bounced transfer", ActorId, Later);
        invoice.RefreshStatus(InvoiceDueDate.AddDays(1), FivePercent, ActorId, Later);

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(267_750, invoice.LateFeeAmount);
        Assert.Equal(5_622_750, invoice.TotalAmount);
    }

    [Fact]
    public void VoidPayment_TheOnlyPaymentBeforeTheDueDate_GoesBackToPending()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);
        invoice.RefreshStatus(InvoiceDueDate, LateFeePolicy.None, ActorId, Later);

        invoice.VoidPayment(payment, "Bounced transfer", ActorId, Later);
        invoice.RefreshStatus(InvoiceDueDate, LateFeePolicy.None, ActorId, Later);

        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
    }

    [Fact]
    public void VoidPayment_PaidAmountOutOfSync_ThrowsAndLeavesThePaymentConfirmed()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);
        TestEntities.Set(invoice, new { PaidAmount = 0m });

        Assert.Throws<InvalidOperationException>(() => invoice.VoidPayment(payment, "Bounced transfer", ActorId, Later));
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
    }

    [Fact]
    public void VoidPayment_PendingPaymentOfACancelledInvoice_Voids()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.Cancel("Wrong meter reading", ActorId, Later);

        invoice.VoidPayment(payment, Payment.InvoiceCancelledReason, ActorId, Later);

        Assert.Equal(PaymentStatus.Voided, payment.Status);
    }

    [Fact]
    public void VoidPayment_PaymentOfAnotherInvoice_ThrowsAndLeavesItConfirmed()
    {
        var invoice = Create();
        var other = Create();
        var payment = other.RecordPayment(Cash(1_000_000), ActorId, Now);
        other.ConfirmPayment(payment, ActorId, Later);

        Assert.Throws<InvalidOperationException>(() => invoice.VoidPayment(payment, "Wrong invoice", ActorId, Later));

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(1_000_000, other.PaidAmount);
    }

    [Fact]
    public void GetAmountToRequest_UnpaidInvoice_ReturnsTheTotal()
    {
        var invoice = Create();

        Assert.Equal(5_355_000, invoice.GetAmountToRequest());
    }

    [Fact]
    public void GetAmountToRequest_AfterAConfirmedPayment_ReturnsTheRemainingAmount()
    {
        var invoice = Create();
        var payment = invoice.RecordPayment(Cash(1_000_000), ActorId, Now);
        invoice.ConfirmPayment(payment, ActorId, Later);

        Assert.Equal(4_355_000, invoice.GetAmountToRequest());
    }

    [Fact]
    public void GetAmountToRequest_RemainingAmountWithAFraction_RoundsDown()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Partial, PaidAmount = 0.5m });

        Assert.Equal(5_354_999, invoice.GetAmountToRequest());
    }

    [Fact]
    public void GetAmountToRequest_OverdueInvoice_ReturnsTheRemainingAmount()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Overdue });

        Assert.Equal(5_355_000, invoice.GetAmountToRequest());
    }

    [Fact]
    public void GetAmountToRequest_DoesNotChangeTheInvoice()
    {
        var invoice = Create();
        var historyCount = invoice.StatusHistories.Count;

        invoice.GetAmountToRequest();

        Assert.Equal((InvoiceStatus.Pending, 0m), (invoice.Status, invoice.PaidAmount));
        Assert.Equal(historyCount, invoice.StatusHistories.Count);
    }

    [Fact]
    public void GetAmountToRequest_PaidInvoice_ThrowsConflict()
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Paid, PaidAmount = 5_355_000m });

        var ex = Assert.Throws<DomainException>(() => invoice.GetAmountToRequest());

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal(Invoice.AlreadyPaidMessage, ex.Message);
    }

    [Fact]
    public void GetAmountToRequest_CancelledInvoice_ThrowsTheSameConflictAsRecordPayment()
    {
        var invoice = Create();
        invoice.Cancel("Wrong meter reading", ActorId, Later);

        var ex = Assert.Throws<DomainException>(() => invoice.GetAmountToRequest());

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal(Invoice.CancelledMessage, ex.Message);
        Assert.Equal(ex.Message, Assert.Throws<DomainException>(() => invoice.RecordPayment(Cash(1_000), ActorId, Now)).Message);
    }

    [Theory]
    [InlineData(5_355_000)] // fully paid, status not refreshed yet
    [InlineData(5_354_999.5)] // less than 1 VND left
    public void GetAmountToRequest_NothingWholeLeftToPay_ThrowsConflict(decimal paidAmount)
    {
        var invoice = Create();
        TestEntities.Set(invoice, new { Status = InvoiceStatus.Partial, PaidAmount = paidAmount });

        var ex = Assert.Throws<DomainException>(() => invoice.GetAmountToRequest());

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal(Invoice.NothingLeftToPayMessage, ex.Message);
    }
}
