using Mapster;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Mappings;

public class InvoiceMappingConfigTests
{
    private static readonly BillingMonth June = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));

    public InvoiceMappingConfigTests() => MapsterTestSupport.EnsureUserMappingRegistered();

    private static Invoice NewInvoice(params InvoiceLine[] lines)
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101");
        TestEntities.Set(room, new { Property = property });
        var lease = TestLeases.Active(new DateOnly(2026, 5, 1), room: room);

        var calculation = new InvoiceCalculation
        {
            BillingMonth = June,
            DueDate = new DateOnly(2026, 6, 8),
            VatRate = 0,
            DiscountAmount = 0,
            Lines = lines,
            Totals = InvoiceTotals.Compute(lines.Select(l => (l.SourceType, l.Amount)), 0, 0, 0),
            Errors = [],
            Warnings = []
        };
        var invoice = Invoice.Create(
            calculation, property.OrganizationId, lease.Id, "INV-202606-0001",
            discountReason: null, notes: null, DateTimeOffset.UtcNow, SentinelActors.System);
        TestEntities.Set(invoice, new { Lease = lease });
        return invoice;
    }

    [Fact]
    public void Adapt_InvoiceToDetail_MapsLeaseRoomAndPropertyAndOrdersItems()
    {
        var invoice = NewInvoice(
            new InvoiceLine("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, Guid.NewGuid()),
            InvoiceLine.Custom("Faucet repair", 1, 50_000));
        // EF does not guarantee the order of a loaded collection
        TestEntities.Set(invoice.Items.First(), new { SortOrder = 5 });
        var lease = invoice.Lease!;
        var room = lease.Room!;

        var response = invoice.Adapt<InvoiceDetailResponse>();

        Assert.Equal(invoice.Id, response.Id);
        Assert.Equal(room.PropertyId, response.PropertyId);
        Assert.Equal("Sunrise House", response.PropertyName);
        Assert.Equal(room.Id, response.RoomId);
        Assert.Equal("101", response.RoomNumber);
        Assert.Equal(lease.Id, response.LeaseId);
        Assert.Equal(lease.LeaseCode, response.LeaseCode);
        Assert.Equal("INV-202606-0001", response.InvoiceNumber);
        Assert.Equal(June.FirstDay, response.BillingMonth);
        Assert.Equal(new DateOnly(2026, 6, 8), response.DueDate);
        Assert.Equal(InvoiceStatus.Pending, response.Status);
        Assert.Equal(3_050_000, response.TotalAmount);
        Assert.Equal(3_050_000, response.RemainingAmount);
        Assert.Equal(["Faucet repair", "Rent 06/2026"], response.Items.Select(i => i.Description));
        Assert.Equal([1, 5], response.Items.Select(i => i.SortOrder));
        Assert.Equal(invoice.Items.Select(i => i.Id).Order(), response.Items.Select(i => i.Id).Order());
    }

    [Fact]
    public void Adapt_InvoiceToResponse_MapsTheSummaryFields()
    {
        var invoice = NewInvoice(new InvoiceLine("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, Guid.NewGuid()));

        var response = invoice.Adapt<InvoiceResponse>();

        Assert.Equal("Sunrise House", response.PropertyName);
        Assert.Equal("101", response.RoomNumber);
        Assert.Equal(3_000_000, response.Subtotal);
    }

    [Fact]
    public void Adapt_InvoiceIssue_MapsCodeMessageAndService()
    {
        var serviceId = Guid.NewGuid();

        var response = new InvoiceIssue(InvoiceIssueCode.MissingReading, "No reading", serviceId).Adapt<InvoiceIssueResponse>();

        Assert.Equal(InvoiceIssueCode.MissingReading, response.Code);
        Assert.Equal("No reading", response.Message);
        Assert.Equal(serviceId, response.ServiceId);
    }
}
