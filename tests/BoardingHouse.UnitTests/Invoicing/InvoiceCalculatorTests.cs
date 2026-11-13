using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Invoicing;

public class InvoiceCalculatorTests
{
    private static readonly BillingMonth May = BillingMonth.FromFirstDay(new DateOnly(2026, 5, 1));
    private static readonly BillingMonth June = May.AddMonths(1);
    private static readonly BillingMonth July = June.AddMonths(1);

    private readonly Room _room = TestRooms.New();
    private readonly Property _property;
    private readonly OrganizationSettings _settings = OrganizationSettings.DefaultsFor(Guid.NewGuid());
    private readonly List<UtilityReading> _readings = [];
    private readonly List<UtilityReading> _monthReadings = [];

    public InvoiceCalculatorTests()
    {
        _property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Property", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
    }

    private UtilityService Service(string name, UtilityBillingMethod method, decimal price) =>
        TestEntities.New<UtilityService>(new
        {
            PropertyId = _room.PropertyId,
            Name = name,
            Type = method == UtilityBillingMethod.Metered ? UtilityType.Electricity : UtilityType.Other,
            BillingMethod = method,
            Unit = method == UtilityBillingMethod.Metered ? "kWh" : "month",
            DefaultUnitPrice = price,
            IsActive = true,
            CreatedBy = SentinelActors.System
        });

    // Billed to billedLeaseId (null = vacant room) and also the room's reading of that month
    private UtilityReading Read(
        Lease lease, UtilityService service, BillingMonth month, decimal? previous, decimal current, Guid? billedLeaseId)
    {
        var reading = TestEntities.New<UtilityReading>(new
        {
            RoomId = lease.RoomId,
            ServiceId = service.Id,
            Service = service,
            ReadOn = month.FirstDay,
            ReadingMonth = month,
            BilledLeaseId = billedLeaseId,
            PreviousValue = previous,
            CurrentValue = current,
            CreatedBy = SentinelActors.System
        });
        if (billedLeaseId == lease.Id)
        {
            _readings.Add(reading);
        }
        _monthReadings.Add(reading);
        return reading;
    }

    private InvoiceCalculation Calculate(
        Lease lease,
        BillingMonth? month = null,
        DateOnly? today = null,
        bool hasDepositItem = false,
        bool hasInvoiceForMonth = false,
        IReadOnlyList<InvoiceLine>? customLines = null,
        decimal discount = 0,
        bool withoutSettings = false)
    {
        var billingMonth = month ?? June;

        return InvoiceCalculator.Calculate(new InvoiceCalculationInput
        {
            Lease = lease,
            Property = _property,
            Settings = withoutSettings ? null : _settings,
            BillingMonth = billingMonth,
            Today = today ?? June.FirstDay,
            LeaseServices = lease.UtilityServices.ToList(),
            UninvoicedReadings = _readings,
            MonthReadings = _monthReadings.Where(r => r.ReadingMonth == billingMonth).ToList(),
            HasDepositItem = hasDepositItem,
            HasInvoiceForMonth = hasInvoiceForMonth,
            CustomLines = customLines ?? [],
            DiscountAmount = discount
        });
    }

    private static IEnumerable<string> Descriptions(InvoiceCalculation calculation) =>
        calculation.Lines.Select(l => l.Description);

    private static IEnumerable<InvoiceIssueCode> Codes(IEnumerable<InvoiceIssue> issues) => issues.Select(i => i.Code);

    // ----- Full month -----

    [Fact]
    public void Calculate_FullMonth_BillsRentFixedPerTenantAndMeteredReading()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var garbage = Service("Garbage", UtilityBillingMethod.PerTenant, 20_000);
        var lease = TestLeases.Active(
            May.FirstDay,
            room: _room,
            utilityServices: [new(electricity, null, 1), new(internet, null, 1), new(garbage, null, 1)],
            tenants: [new(), new()]);
        var reading = Read(lease, electricity, June, previous: 1_200, current: 1_320, lease.Id);

        var calculation = Calculate(lease);

        Assert.True(calculation.CanCreate);
        Assert.Empty(calculation.Warnings);
        // Recurring services are ordered by name
        Assert.Equal(
            ["Rent 06/2026", "Electricity: 1,200 → 1,320 (120 kWh)", "Garbage 06/2026 (2 tenants)", "Internet 06/2026"],
            Descriptions(calculation));
        Assert.Equal([3_000_000m, 420_000m, 40_000m, 100_000m], calculation.Lines.Select(l => l.Amount));
        Assert.Equal(
            [lease.Id, reading.Id, garbage.Id, internet.Id],
            calculation.Lines.Select(l => l.SourceId!.Value));
        Assert.Equal(
            [InvoiceItemSourceType.Rent, InvoiceItemSourceType.Utility, InvoiceItemSourceType.Utility, InvoiceItemSourceType.Utility],
            calculation.Lines.Select(l => l.SourceType));
        Assert.Equal(3_560_000, calculation.Totals.TotalAmount);
    }

    [Fact]
    public void Calculate_FixedServiceWithQuantity_BillsQuantityTimesPrice()
    {
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(internet, null, 2)]);

        var line = Calculate(lease).Lines.Single(l => l.SourceId == internet.Id);

        Assert.Equal("Internet 06/2026 (x2)", line.Description);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(100_000, line.UnitPrice);
        Assert.Equal(200_000, line.Amount);
    }

    [Fact]
    public void Calculate_UnitPriceOverride_IsUsedForEveryBillingMethod()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var garbage = Service("Garbage", UtilityBillingMethod.PerTenant, 20_000);
        var lease = TestLeases.Active(
            May.FirstDay,
            room: _room,
            utilityServices: [new(electricity, 4_000, 1), new(internet, 80_000, 1), new(garbage, 15_000, 1)]);
        Read(lease, electricity, June, previous: 100, current: 110, lease.Id);

        var lines = Calculate(lease).Lines.Where(l => l.SourceType == InvoiceItemSourceType.Utility).ToList();

        Assert.Equal([4_000m, 15_000m, 80_000m], lines.Select(l => l.UnitPrice));
        Assert.Equal([40_000m, 15_000m, 80_000m], lines.Select(l => l.Amount));
    }

    [Fact]
    public void Calculate_LeaseStartsMidMonth_ProratesRentAndRecurringServices()
    {
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var garbage = Service("Garbage", UtilityBillingMethod.PerTenant, 20_000);
        var lease = TestLeases.Active(
            new DateOnly(2026, 6, 16),
            room: _room,
            utilityServices: [new(internet, null, 1), new(garbage, null, 1)],
            tenants: [new(), new()]);

        var calculation = Calculate(lease, today: new DateOnly(2026, 6, 16));

        Assert.Equal(
            ["Rent 06/2026 (15/30 days)", "Garbage 06/2026 (2 tenants) (15/30 days)", "Internet 06/2026 (15/30 days)"],
            Descriptions(calculation));
        Assert.Equal([1_500_000m, 20_000m, 50_000m], calculation.Lines.Select(l => l.Amount));
    }

    [Theory]
    [InlineData("2028-02-15", "15/29", 1_551_724)]
    [InlineData("2027-02-15", "14/28", 1_500_000)]
    public void Calculate_LeaseStartsInFebruary_ProratesByTheDaysOfThatFebruary(
        string startDate, string days, decimal expectedRent)
    {
        var start = DateOnly.Parse(startDate);
        var lease = TestLeases.Active(start, room: _room);

        var rent = Calculate(lease, BillingMonth.Of(start), today: start).Lines.Single();

        Assert.Equal($"Rent 02/{start.Year} ({days} days)", rent.Description);
        Assert.Equal(expectedRent, rent.Amount);
    }

    [Fact]
    public void Calculate_LeaseExpiresMidMonth_BillsUpToTheEndDate()
    {
        var lease = TestLeases.Active(May.FirstDay, endDate: new DateOnly(2026, 6, 10), room: _room);
        TestLeases.Expire(lease);

        var rent = Calculate(lease, today: new DateOnly(2026, 6, 11)).Lines.Single();

        Assert.Equal("Rent 06/2026 (10/30 days)", rent.Description);
        Assert.Equal(1_000_000, rent.Amount);
    }

    [Fact]
    public void Calculate_LeaseStartsAndIsTerminatedInTheSameMonth_BillsTheDaysInBetween()
    {
        var lease = TestLeases.Active(new DateOnly(2026, 6, 16), endDate: new DateOnly(2027, 1, 1), room: _room);
        TestLeases.Terminate(lease, new DateOnly(2026, 6, 20));

        var rent = Calculate(lease, today: new DateOnly(2026, 6, 20)).Lines.Single();

        Assert.Equal("Rent 06/2026 (5/30 days)", rent.Description);
        Assert.Equal(500_000, rent.Amount);
    }

    [Theory]
    [InlineData(1_000_001, 500_001)] // 500,000.5 rounds away from zero
    [InlineData(999_999, 500_000)] // 499,999.5
    [InlineData(1_000_000, 500_000)]
    public void Calculate_ProratedAmount_RoundsHalfAwayFromZero(decimal monthlyRent, decimal expected)
    {
        var lease = TestLeases.Active(new DateOnly(2026, 6, 16), monthlyRent: monthlyRent, room: _room);

        Assert.Equal(expected, Calculate(lease, today: new DateOnly(2026, 6, 16)).Lines.Single().Amount);
    }

    [Theory]
    [InlineData("2026-06-20", null, 2)]
    [InlineData(null, "2026-05-31", 1)]
    [InlineData(null, "2026-06-01", 2)]
    public void Calculate_PerTenantService_CountsTenantsWhoLiveThereThatMonth(
        string? joinedDate, string? leftDate, int expectedTenants)
    {
        var garbage = Service("Garbage", UtilityBillingMethod.PerTenant, 20_000);
        var tenant = new TestLeases.TenantSpec(
            joinedDate is null ? null : DateOnly.Parse(joinedDate),
            leftDate is null ? null : DateOnly.Parse(leftDate));
        var lease = TestLeases.Active(
            May.FirstDay, room: _room, utilityServices: [new(garbage, null, 1)], tenants: [new(), tenant]);

        var line = Calculate(lease).Lines.Single(l => l.SourceId == garbage.Id);

        Assert.Equal(expectedTenants, line.Quantity);
        Assert.Equal(expectedTenants * 20_000m, line.Amount);
    }

    [Fact]
    public void Calculate_PerTenantServiceWithoutTenantsThatMonth_HasNoLine()
    {
        var garbage = Service("Garbage", UtilityBillingMethod.PerTenant, 20_000);
        var lease = TestLeases.Active(
            May.FirstDay,
            room: _room,
            utilityServices: [new(garbage, null, 1)],
            tenants: [new(JoinedDate: new DateOnly(2026, 7, 1))]);

        Assert.DoesNotContain(Calculate(lease).Lines, l => l.SourceId == garbage.Id);
    }

    [Fact]
    public void Calculate_InactiveLeaseService_IsNotBilled()
    {
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(internet, null, 1)]);
        TestEntities.Set(lease.UtilityServices.Single(), new { IsActive = false });

        Assert.Equal(["Rent 06/2026"], Descriptions(Calculate(lease)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calculate_DeactivatedOrDeletedPropertyService_IsNotBilledNorRequiresAReading(bool deleted)
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var lease = TestLeases.Active(
            May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1), new(internet, null, 1)]);
        foreach (var service in new[] { electricity, internet })
        {
            if (deleted)
            {
                TestEntities.Set(service, new { DeletedAt = (DateTimeOffset?)DateTimeOffset.UtcNow });
            }
            else
            {
                TestEntities.Set(service, new { IsActive = false });
            }
        }

        var calculation = Calculate(lease);

        Assert.True(calculation.CanCreate);
        Assert.Equal(["Rent 06/2026"], Descriptions(calculation));
    }

    [Fact]
    public void Calculate_ReadingOfAnInactiveLeaseService_IsSkippedWithAWarning()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        TestEntities.Set(lease.UtilityServices.Single(), new { IsActive = false });
        Read(lease, electricity, June, previous: 100, current: 110, lease.Id);

        var calculation = Calculate(lease);

        Assert.True(calculation.CanCreate);
        Assert.Equal(["Rent 06/2026"], Descriptions(calculation));
        var warning = Assert.Single(calculation.Warnings);
        Assert.Equal(InvoiceIssueCode.ServiceNotOnLease, warning.Code);
        Assert.Equal(electricity.Id, warning.ServiceId);
    }

    [Fact]
    public void Calculate_ReadingOfADeactivatedPropertyServiceStillOnTheLease_IsBilled()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        TestEntities.Set(electricity, new { IsActive = false });
        var reading = Read(lease, electricity, June, previous: 100, current: 110, lease.Id);

        var calculation = Calculate(lease);

        Assert.Contains(calculation.Lines, l => l.SourceId == reading.Id && l.Amount == 35_000);
        Assert.Empty(calculation.Warnings);
    }

    [Fact]
    public void Calculate_SeveralUninvoicedReadings_BillsEachWithItsPeriod()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        Read(lease, electricity, June, previous: 150, current: 200, lease.Id);
        Read(lease, electricity, May, previous: 100, current: 150, lease.Id);

        var lines = Calculate(lease).Lines.Where(l => l.SourceType == InvoiceItemSourceType.Utility);

        Assert.Equal(
            ["Electricity: 100 → 150 (50 kWh) (period 05/2026)", "Electricity: 150 → 200 (50 kWh)"],
            lines.Select(l => l.Description));
    }

    [Fact]
    public void Calculate_ReadingWithoutConsumption_BillsAZeroLine()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        var reading = Read(lease, electricity, June, previous: 100, current: 100, lease.Id);

        var line = Calculate(lease).Lines.Single(l => l.SourceId == reading.Id);

        Assert.Equal(0, line.Quantity);
        Assert.Equal(0, line.Amount);
    }

    [Fact]
    public void Calculate_FractionalConsumption_RoundsTheAmount()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_333);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        var reading = Read(lease, electricity, June, previous: 100, current: 112.5m, lease.Id);

        var line = Calculate(lease).Lines.Single(l => l.SourceId == reading.Id);

        Assert.Equal("Electricity: 100 → 112.5 (12.5 kWh)", line.Description);
        Assert.Equal(41_663, line.Amount); // 41,662.5
    }

    [Fact]
    public void Calculate_FirstReadingWithoutPreviousValue_IsSkippedWithAWarning()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        Read(lease, electricity, June, previous: null, current: 1_320, lease.Id);

        var calculation = Calculate(lease);

        Assert.True(calculation.CanCreate);
        Assert.DoesNotContain(calculation.Lines, l => l.SourceType == InvoiceItemSourceType.Utility);
        Assert.Equal(InvoiceIssueCode.ReadingWithoutConsumption, Assert.Single(calculation.Warnings).Code);
    }

    [Fact]
    public void Calculate_MissingReadingInTheFirstMonth_IsNotAnError()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);

        var calculation = Calculate(lease, May, today: May.FirstDay);

        Assert.True(calculation.CanCreate);
    }

    [Fact]
    public void Calculate_MissingReadingAfterFirstMonth_IsAnError()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);

        var calculation = Calculate(lease);

        Assert.False(calculation.CanCreate);
        var error = Assert.Single(calculation.Errors);
        Assert.Equal(InvoiceIssueCode.MissingReading, error.Code);
        Assert.Equal(electricity.Id, error.ServiceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calculate_MonthReadingBilledToAnotherLeaseOrNobody_IsAWarning(bool vacant)
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(May.FirstDay, room: _room, utilityServices: [new(electricity, null, 1)]);
        Read(lease, electricity, June, previous: 100, current: 110, billedLeaseId: vacant ? null : Guid.NewGuid());

        var calculation = Calculate(lease);

        Assert.True(calculation.CanCreate);
        Assert.DoesNotContain(calculation.Lines, l => l.SourceType == InvoiceItemSourceType.Utility);
        var warning = Assert.Single(calculation.Warnings);
        Assert.Equal(InvoiceIssueCode.ReadingBilledElsewhere, warning.Code);
        Assert.Equal(electricity.Id, warning.ServiceId);
    }

    [Fact]
    public void Calculate_TerminatedLeaseWithoutReading_IsNotAnError()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(
            May.FirstDay, endDate: new DateOnly(2027, 1, 1), room: _room, utilityServices: [new(electricity, null, 1)]);
        TestLeases.Terminate(lease, new DateOnly(2026, 6, 10));

        Assert.True(Calculate(lease, today: new DateOnly(2026, 6, 10)).CanCreate);
    }

    [Fact]
    public void Calculate_FirstInvoice_BillsTheDeposit()
    {
        var lease = TestLeases.Active(May.FirstDay, depositAmount: 3_000_000, room: _room);

        var deposit = Calculate(lease).Lines.Single(l => l.SourceType == InvoiceItemSourceType.Deposit);

        Assert.Equal("Deposit", deposit.Description);
        Assert.Equal(3_000_000, deposit.Amount);
        Assert.Equal(lease.Id, deposit.SourceId);
    }

    [Fact]
    public void Calculate_DepositAlreadyInvoiced_IsNotBilledAgain()
    {
        var lease = TestLeases.Active(May.FirstDay, depositAmount: 3_000_000, room: _room);

        Assert.DoesNotContain(
            Calculate(lease, hasDepositItem: true).Lines, l => l.SourceType == InvoiceItemSourceType.Deposit);
    }

    [Fact]
    public void Calculate_RenewalLease_DoesNotBillTheDeposit()
    {
        var lease = TestLeases.Active(
            May.FirstDay, depositAmount: 3_000_000, previousLeaseId: Guid.NewGuid(), room: _room);

        Assert.DoesNotContain(Calculate(lease).Lines, l => l.SourceType == InvoiceItemSourceType.Deposit);
    }

    [Fact]
    public void Calculate_NoDepositAmount_HasNoDepositLine()
    {
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        Assert.DoesNotContain(Calculate(lease).Lines, l => l.SourceType == InvoiceItemSourceType.Deposit);
    }

    [Fact]
    public void Calculate_MonthAfterTheLeaseEnded_DoesNotBillTheDeposit()
    {
        var lease = TestLeases.Active(
            May.FirstDay, endDate: new DateOnly(2026, 6, 15), depositAmount: 3_000_000, room: _room);
        TestLeases.Expire(lease);

        var calculation = Calculate(
            lease, July, today: July.FirstDay, customLines: [InvoiceLine.Custom("Cleaning", 1, 50_000)]);

        Assert.DoesNotContain(calculation.Lines, l => l.SourceType == InvoiceItemSourceType.Deposit);
    }

    [Fact]
    public void Calculate_WithVatRate_AppliesVatToEverythingButTheDeposit()
    {
        TestEntities.Set(_settings, new { VatRate = (decimal?)10 });
        var lease = TestLeases.Active(May.FirstDay, depositAmount: 3_000_000, room: _room);

        var calculation = Calculate(lease);

        Assert.Equal(10, calculation.VatRate);
        Assert.Equal(6_000_000, calculation.Totals.Subtotal);
        Assert.Equal(300_000, calculation.Totals.VatAmount);
        Assert.Equal(6_300_000, calculation.Totals.TotalAmount);
    }

    [Fact]
    public void Calculate_FractionalVat_IsRounded()
    {
        TestEntities.Set(_settings, new { VatRate = (decimal?)8.5m });
        var lease = TestLeases.Active(May.FirstDay, monthlyRent: 1_000_010, room: _room);

        Assert.Equal(85_001, Calculate(lease).Totals.VatAmount); // 85,000.85
    }

    [Fact]
    public void Calculate_WithoutSettings_HasNoVat()
    {
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        var calculation = Calculate(lease, withoutSettings: true);

        Assert.True(calculation.CanCreate);
        Assert.Equal(0, calculation.VatRate);
        Assert.Equal(0, calculation.Totals.VatAmount);
    }

    [Fact]
    public void Calculate_NegativeNonDepositPart_HasNoNegativeVat()
    {
        TestEntities.Set(_settings, new { VatRate = (decimal?)10 });
        var lease = TestLeases.Active(May.FirstDay, depositAmount: 3_000_000, room: _room);

        var calculation = Calculate(lease, customLines: [InvoiceLine.Custom("Refund", 1, -4_000_000)]);

        Assert.Equal(2_000_000, calculation.Totals.Subtotal);
        Assert.Equal(0, calculation.Totals.VatAmount);
        Assert.Equal(2_000_000, calculation.Totals.TotalAmount);
    }

    [Fact]
    public void Calculate_Discount_IsSubtractedFromTheTotal()
    {
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        var calculation = Calculate(lease, discount: 500_000);

        Assert.True(calculation.CanCreate);
        Assert.Equal(500_000, calculation.DiscountAmount);
        Assert.Equal(2_500_000, calculation.Totals.TotalAmount);
    }

    [Fact]
    public void Calculate_DiscountAboveTheTotal_IsAnError()
    {
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        var calculation = Calculate(lease, discount: 4_000_000);

        Assert.Equal(InvoiceIssueCode.NegativeTotal, Assert.Single(calculation.Errors).Code);
    }

    [Fact]
    public void Calculate_Totals_MatchInvoiceTotalsOfTheLines()
    {
        TestEntities.Set(_settings, new { VatRate = (decimal?)8 });
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var lease = TestLeases.Active(
            May.FirstDay, depositAmount: 2_000_000, room: _room, utilityServices: [new(internet, null, 1)]);

        var calculation = Calculate(lease, discount: 10_000);

        Assert.Equal(
            InvoiceTotals.Compute(calculation.Lines.Select(l => (l.SourceType, l.Amount)), 8, 0, 10_000),
            calculation.Totals);
    }

    [Fact]
    public void Calculate_CustomLines_AreAddedLastAsGiven()
    {
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        var calculation = Calculate(
            lease,
            customLines: [InvoiceLine.Custom("  Repair  ", 1.5m, 33_333), InvoiceLine.Custom("Refund", 1, -100_000)]);

        Assert.Equal(["Rent 06/2026", "Repair", "Refund"], Descriptions(calculation));
        var repair = calculation.Lines[1];
        Assert.Equal(InvoiceItemSourceType.Custom, repair.SourceType);
        Assert.Null(repair.SourceId);
        Assert.Equal(50_000, repair.Amount); // 49,999.5
        Assert.Equal(-100_000, calculation.Lines[2].Amount);
        Assert.Equal(2_950_000, calculation.Totals.TotalAmount);
    }

    [Fact]
    public void Calculate_DraftLease_IsAnError()
    {
        var lease = TestLeases.Draft(May.FirstDay, room: _room);

        Assert.Contains(InvoiceIssueCode.LeaseNotBillable, Codes(Calculate(lease).Errors));
    }

    [Fact]
    public void Calculate_MonthBeforeTheLeaseStarts_IsAnError()
    {
        var lease = TestLeases.Active(June.FirstDay, room: _room);

        var calculation = Calculate(lease, May, today: June.FirstDay);

        Assert.Contains(InvoiceIssueCode.MonthBeforeStart, Codes(calculation.Errors));
        Assert.DoesNotContain(calculation.Lines, l => l.SourceType == InvoiceItemSourceType.Rent);
    }

    [Fact]
    public void Calculate_MonthAfterAnExpiredLease_BillsOnlyItsReadingsAndCustomLines()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var internet = Service("Internet", UtilityBillingMethod.Fixed, 100_000);
        var lease = TestLeases.Active(
            May.FirstDay,
            endDate: new DateOnly(2026, 6, 15),
            depositAmount: 3_000_000,
            room: _room,
            utilityServices: [new(electricity, null, 1), new(internet, null, 1)]);
        TestLeases.Expire(lease);
        var reading = Read(lease, electricity, July, previous: 100, current: 120, lease.Id);

        var calculation = Calculate(
            lease, July, today: July.FirstDay, customLines: [InvoiceLine.Custom("Cleaning", 1, 50_000)]);

        Assert.True(calculation.CanCreate);
        Assert.Equal([reading.Id, (Guid?)null], calculation.Lines.Select(l => l.SourceId));
        Assert.Equal(120_000, calculation.Totals.TotalAmount);
    }

    [Fact]
    public void Calculate_MonthAfterAnExpiredLeaseWithNothingToBill_IsAnError()
    {
        var lease = TestLeases.Active(May.FirstDay, endDate: new DateOnly(2026, 6, 15), room: _room);
        TestLeases.Expire(lease);

        var calculation = Calculate(lease, July, today: July.FirstDay);

        Assert.Empty(calculation.Lines);
        Assert.Equal(InvoiceIssueCode.NoItems, Assert.Single(calculation.Errors).Code);
    }

    [Fact]
    public void Calculate_TwoMonthsAfterTheLeaseEnded_IsAnError()
    {
        var lease = TestLeases.Active(May.FirstDay, endDate: new DateOnly(2026, 6, 15), room: _room);
        TestLeases.Expire(lease);

        var calculation = Calculate(lease, July.AddMonths(1), today: July.FirstDay);

        Assert.Contains(InvoiceIssueCode.MonthAfterEnd, Codes(calculation.Errors));
    }

    [Fact]
    public void Calculate_MonthAlreadyInvoiced_IsAnError()
    {
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        Assert.Equal(
            InvoiceIssueCode.AlreadyInvoiced, Assert.Single(Calculate(lease, hasInvoiceForMonth: true).Errors).Code);
    }

    [Fact]
    public void Calculate_OrganizationCurrencyOtherThanVnd_IsAnError()
    {
        TestEntities.Set(_settings, new { Currency = "USD" });
        var lease = TestLeases.Active(May.FirstDay, room: _room);

        Assert.Equal(InvoiceIssueCode.UnsupportedCurrency, Assert.Single(Calculate(lease).Errors).Code);
    }

    [Fact]
    public void Calculate_TerminatedLease_BillsUpToTheDayItWasTerminatedThenOnlyReadingsAndCustomLines()
    {
        var electricity = Service("Electricity", UtilityBillingMethod.Metered, 3_500);
        var lease = TestLeases.Active(
            May.FirstDay, endDate: new DateOnly(2026, 12, 31), room: _room, utilityServices: [new(electricity, null, 1)]);
        TestLeases.Terminate(lease, new DateOnly(2026, 6, 10));

        var june = Calculate(lease, today: new DateOnly(2026, 6, 10));
        var rent = june.Lines.Single();
        Assert.Equal("Rent 06/2026 (10/30 days)", rent.Description);
        Assert.Equal(1_000_000, rent.Amount);

        var reading = Read(lease, electricity, July, previous: 100, current: 110, lease.Id);
        var july = Calculate(lease, July, today: July.FirstDay, customLines: [InvoiceLine.Custom("Cleaning", 1, 50_000)]);
        Assert.True(july.CanCreate);
        Assert.Equal([reading.Id, (Guid?)null], july.Lines.Select(l => l.SourceId));
    }

    [Theory]
    [InlineData(10, 5, 7, "2026-05-01", "2026-06-01", "2026-06-10")] // lease
    [InlineData(null, 5, 7, "2026-05-01", "2026-06-01", "2026-06-05")] // property
    [InlineData(null, null, 7, "2026-05-01", "2026-06-01", "2026-06-07")] // organization
    [InlineData(null, null, null, "2026-01-31", "2026-06-01", "2026-06-30")] // day of StartDate, capped
    [InlineData(null, null, null, "2026-01-31", "2026-02-01", "2026-02-28")]
    [InlineData(null, null, null, "2027-01-31", "2028-02-01", "2028-02-29")]
    public void ResolveDueDate_UsesTheFirstBillingDaySetCappedAtTheEndOfTheMonth(
        int? leaseDay, int? propertyDay, int? organizationDay, string startDate, string month, string expected)
    {
        TestEntities.Set(_property, new { DefaultBillingDay = propertyDay });
        TestEntities.Set(_settings, new { DefaultBillingDay = organizationDay });
        var billingMonth = BillingMonth.FromFirstDay(DateOnly.Parse(month));
        var lease = TestLeases.Active(DateOnly.Parse(startDate), billingDay: leaseDay, room: _room);

        var dueDate = InvoiceCalculator.ResolveDueDate(lease, _property, _settings, billingMonth, billingMonth.FirstDay);

        Assert.Equal(DateOnly.Parse(expected), dueDate);
    }

    [Theory]
    [InlineData(3, 5, "2026-06-13")] // property grace days
    [InlineData(null, 5, "2026-06-15")] // organization grace days
    [InlineData(null, null, "2026-06-10")]
    public void ResolveDueDate_AddsTheGraceDays(int? propertyGraceDays, int? organizationGraceDays, string expected)
    {
        TestEntities.Set(_property, new { LateFeeGraceDays = propertyGraceDays });
        TestEntities.Set(_settings, new { LateFeeGraceDays = organizationGraceDays });
        var lease = TestLeases.Active(May.FirstDay, billingDay: 10, room: _room);

        Assert.Equal(DateOnly.Parse(expected), Calculate(lease).DueDate);
    }

    [Fact]
    public void ResolveDueDate_TodayAfterTheBillingDay_StartsFromToday()
    {
        TestEntities.Set(_property, new { LateFeeGraceDays = (int?)3 });
        var lease = TestLeases.Active(May.FirstDay, billingDay: 5, room: _room);

        Assert.Equal(new DateOnly(2026, 6, 23), Calculate(lease, today: new DateOnly(2026, 6, 20)).DueDate);
    }
}
