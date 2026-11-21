using Mapster;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Mappings;

public class PaymentMappingConfigTests
{
    private static readonly DateTimeOffset PaidAt = new(2026, 11, 19, 3, 15, 0, TimeSpan.Zero);

    public PaymentMappingConfigTests() => MapsterTestSupport.EnsureUserMappingRegistered();

    // Invoice → Lease → Room → Property loaded, the way IPaymentRepository *WithDetails will load it
    private static Invoice NewInvoice()
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101");
        TestEntities.Set(room, new { Property = property });
        var lease = TestLeases.Active(new DateOnly(2026, 5, 1), room: room);

        return TestEntities.New<Invoice>(new
        {
            OrganizationId = property.OrganizationId,
            LeaseId = lease.Id,
            Lease = lease,
            InvoiceNumber = "INV-202611-0001"
        });
    }

    private static Payment NewPayment(Invoice invoice, object overrides)
    {
        var payment = TestEntities.New<Payment>(new
        {
            InvoiceId = (Guid?)invoice.Id,
            Invoice = invoice,
            Amount = 1_500_000m,
            Method = PaymentMethod.Cash,
            PaymentDate = PaidAt
        });
        return TestEntities.Set(payment, overrides);
    }

    [Fact]
    public void Adapt_ConfirmedPayment_MapsInvoiceLeaseRoomPropertyAndOwnFields()
    {
        var invoice = NewInvoice();
        var lease = invoice.Lease!;
        var room = lease.Room!;
        var payer = TestEntities.New<User>(new { FullName = "Nguyen Van A" });
        var staffId = Guid.NewGuid();
        var payment = NewPayment(invoice, new
        {
            PayerUserId = (Guid?)payer.Id,
            PayerUser = payer,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Confirmed,
            TransactionReference = "FT2632312345",
            ConfirmedBy = (Guid?)staffId,
            ConfirmedAt = (DateTimeOffset?)PaidAt.AddHours(1),
            Note = "Paid at the office",
            CreatedBy = staffId,
            CreatedAt = PaidAt.AddMinutes(5),
            UpdatedAt = (DateTimeOffset?)PaidAt.AddHours(1)
        });

        var response = payment.Adapt<PaymentResponse>();

        Assert.Equal(payment.Id, response.Id);
        Assert.Equal(invoice.OrganizationId, response.OrganizationId);
        Assert.Equal(room.PropertyId, response.PropertyId);
        Assert.Equal("Sunrise House", response.PropertyName);
        Assert.Equal(room.Id, response.RoomId);
        Assert.Equal("101", response.RoomNumber);
        Assert.Equal(lease.Id, response.LeaseId);
        Assert.Equal(lease.LeaseCode, response.LeaseCode);
        Assert.Equal(invoice.Id, response.InvoiceId);
        Assert.Equal("INV-202611-0001", response.InvoiceNumber);
        Assert.Equal(payer.Id, response.PayerUserId);
        Assert.Null(response.PayerNameOverride);
        Assert.Equal("Nguyen Van A", response.PayerName);
        Assert.Equal(1_500_000m, response.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, response.Method);
        Assert.Equal(PaymentStatus.Confirmed, response.Status);
        Assert.Equal("FT2632312345", response.TransactionReference);
        Assert.Equal(PaidAt, response.PaymentDate);
        Assert.Equal(staffId, response.ConfirmedBy);
        Assert.Equal(PaidAt.AddHours(1), response.ConfirmedAt);
        Assert.Null(response.VoidedBy);
        Assert.Null(response.VoidedAt);
        Assert.Null(response.VoidReason);
        Assert.Equal("Paid at the office", response.Note);
        Assert.Equal(staffId, response.CreatedBy);
        Assert.Equal(PaidAt.AddMinutes(5), response.CreatedAt);
        Assert.Equal(PaidAt.AddHours(1), response.UpdatedAt);
    }

    [Fact]
    public void Adapt_VoidedPayment_MapsTheVoid()
    {
        var staffId = Guid.NewGuid();
        var payment = NewPayment(NewInvoice(), new
        {
            Status = PaymentStatus.Voided,
            VoidedBy = (Guid?)staffId,
            VoidedAt = (DateTimeOffset?)PaidAt.AddDays(1),
            VoidReason = "Wrong amount"
        });

        var response = payment.Adapt<PaymentResponse>();

        Assert.Equal(PaymentStatus.Voided, response.Status);
        Assert.Equal(staffId, response.VoidedBy);
        Assert.Equal(PaidAt.AddDays(1), response.VoidedAt);
        Assert.Equal("Wrong amount", response.VoidReason);
    }

    [Fact]
    public void Adapt_WalkInPayer_UsesTheOverrideAsPayerName()
    {
        var payment = NewPayment(NewInvoice(), new { PayerNameOverride = "Tenant's mother" });

        var response = payment.Adapt<PaymentResponse>();

        Assert.Null(response.PayerUserId);
        Assert.Equal("Tenant's mother", response.PayerNameOverride);
        Assert.Equal("Tenant's mother", response.PayerName);
    }

    [Fact]
    public void Adapt_NoPayer_LeavesPayerNameNull()
    {
        var response = NewPayment(NewInvoice(), new { }).Adapt<PaymentResponse>();

        Assert.Null(response.PayerUserId);
        Assert.Null(response.PayerNameOverride);
        Assert.Null(response.PayerName);
    }
}
