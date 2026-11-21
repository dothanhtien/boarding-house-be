using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentOrganization;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class PaymentServiceTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Mock<IPaymentRepository> _paymentRepository = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<ILeaseRepository> _leaseRepository = new();
    private readonly Mock<IPropertyRepository> _propertyRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOrganizationScopeAccessor> _organizationScopeAccessor = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly Mock<ICurrentOrganizationAccessor> _currentOrganizationAccessor = new();
    private readonly User _actor = NewUser("Actor");
    private readonly Dictionary<Guid, Invoice> _invoices = [];
    private readonly Dictionary<Guid, Payment> _payments = [];
    private readonly List<string> _locks = [];
    private readonly PaymentService _paymentService;

    public PaymentServiceTests()
    {
        MapsterTestSupport.EnsureUserMappingRegistered();
        _currentUserAccessor.SetupGet(a => a.RequiredUser).Returns(_actor);

        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken ct) => operation(ct));
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct));

        _paymentRepository
            .Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .Callback((Payment payment, CancellationToken _) => _payments[payment.Id] = payment)
            .Returns(Task.CompletedTask);

        // What the repository's Include does: the payment comes back with its invoice (→ lease → room → property)
        _paymentRepository
            .Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _payments.TryGetValue(id, out var payment)
                ? TestEntities.Set(payment, new { Invoice = _invoices[payment.InvoiceId!.Value] })
                : null);

        _paymentService = new PaymentService(
            _paymentRepository.Object,
            _invoiceRepository.Object,
            _leaseRepository.Object,
            new InvoiceLateFeeResolver(_propertyRepository.Object, Mock.Of<IOrganizationSettingsRepository>()),
            _unitOfWork.Object,
            _organizationScopeAccessor.Object,
            _currentUserAccessor.Object,
            _currentOrganizationAccessor.Object,
            NullLogger<PaymentService>.Instance);

        SetupSearchReturns([]);
    }

    private static User NewUser(string fullName) => TestEntities.New<User>(new
    {
        Email = $"{Guid.NewGuid():N}@test.com",
        PasswordHash = "hashed-password",
        FullName = fullName,
        CreatedBy = SentinelActors.System
    });

    private static OrganizationScope Unrestricted() => new() { IsUnrestricted = true, OrganizationIds = new HashSet<Guid>() };

    private static OrganizationScope Restricted(params Guid[] organizationIds) =>
        new() { IsUnrestricted = false, OrganizationIds = organizationIds.ToHashSet() };

    private void SetupScope(string action, OrganizationScope scope) =>
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Payment, action, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

    // Rent 3,000,000, due in 30 days: PENDING whatever today is
    private Invoice SetupInvoice()
    {
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, LateFeePolicy.None),
            SentinelActors.System);
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101");
        TestEntities.Set(room, new { Property = property });
        var lease = TestLeases.Active(new DateOnly(2026, 5, 1), room: room);

        InvoiceLine[] lines = [new("Rent 06/2026", 1, 3_000_000, 3_000_000, InvoiceItemSourceType.Rent, lease.Id)];
        var calculation = new InvoiceCalculation
        {
            BillingMonth = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1)),
            DueDate = Today.AddDays(30),
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

        _invoices[invoice.Id] = invoice;
        _invoiceRepository
            .Setup(r => r.GetAggregateForUpdateAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .Callback(() => _locks.Add("invoice"))
            .ReturnsAsync(invoice);

        return invoice;
    }

    private void SetupTenants(Invoice invoice, params LeaseTenant[] tenants) =>
        _leaseRepository
            .Setup(r => r.ListTenantsAsync(invoice.LeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenants.ToList());

    private static LeaseTenant Tenant(Invoice invoice, User user, bool isRepresentative) =>
        new(invoice.LeaseId, user, isRepresentative, joinedDate: null, SentinelActors.System);

    private static RecordPaymentRequest Cash(decimal amount, bool confirm = false) => new()
    {
        Amount = amount,
        Method = PaymentMethod.Cash,
        PaymentDate = DateTimeOffset.UtcNow.AddMinutes(-1),
        Confirm = confirm
    };

    // A pending payment already saved for the invoice
    private Payment AddPayment(Invoice invoice, decimal amount = 1_000_000, Guid? payerUserId = null)
    {
        var payment = invoice.RecordPayment(
            new PaymentDetails(amount, PaymentMethod.Cash, DateTimeOffset.UtcNow.AddMinutes(-1), payerUserId, null, null, null),
            SentinelActors.System,
            DateTimeOffset.UtcNow);
        _payments[payment.Id] = payment;
        return payment;
    }

    private sealed record SearchCall(
        Guid? OrganizationId,
        IReadOnlySet<Guid>? AllowedOrganizationIds,
        Guid? PropertyId,
        PaymentMethod? Method,
        PaymentStatus? Status,
        DateTimeOffset? PaidFrom,
        DateTimeOffset? PaidBefore,
        string? Search,
        Expression<Func<Payment, object>> SortField,
        SortOrder SortOrder);

    private SearchCall? _search;

    private void SetupSearchReturns(List<Payment> items) =>
        _paymentRepository
            .Setup(r => r.SearchAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<PaymentMethod?>(),
                It.IsAny<PaymentStatus?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(),
                It.IsAny<PageRequest>(), It.IsAny<Expression<Func<Payment, object>>>(), It.IsAny<SortOrder>(),
                It.IsAny<CancellationToken>()))
            .Callback((Guid? organizationId, IReadOnlySet<Guid>? allowedOrganizationIds, Guid? propertyId,
                PaymentMethod? method, PaymentStatus? status, DateTimeOffset? paidFrom, DateTimeOffset? paidBefore,
                string? search, PageRequest _, Expression<Func<Payment, object>> sortField, SortOrder sortOrder,
                CancellationToken _) =>
                _search = new SearchCall(organizationId, allowedOrganizationIds, propertyId, method, status,
                    paidFrom, paidBefore, search, sortField, sortOrder))
            .ReturnsAsync(new PagedResult<Payment> { Items = items, Page = 1, PageSize = 20, TotalItems = items.Count });

    private void SetupPaymentForUpdate(Payment payment)
    {
        _paymentRepository
            .Setup(r => r.GetInvoiceIdAsync(payment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment.InvoiceId);
        _paymentRepository
            .Setup(r => r.GetForUpdateAsync(payment.Id, It.IsAny<CancellationToken>()))
            .Callback(() => _locks.Add("payment"))
            .ReturnsAsync(payment);
    }

    [Fact]
    public async Task RecordAsync_UnknownInvoice_ThrowsNotFound()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.RecordAsync(id, Cash(1_000_000)));

        Assert.Equal(NotFoundMessages.Invoice(id), ex.Message);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task RecordAsync_InvoiceOfAnotherOrganization_ThrowsNotFoundAndAddsNothing()
    {
        var invoice = SetupInvoice();
        SetupScope(PermissionActions.Create, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.RecordAsync(invoice.Id, Cash(1_000_000)));

        Assert.Equal(NotFoundMessages.Invoice(invoice.Id), ex.Message);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task RecordAsync_WithoutConfirm_AddsAPendingPaymentAsTheCurrentUser()
    {
        var invoice = SetupInvoice();
        SetupTenants(invoice, Tenant(invoice, NewUser("Representative"), isRepresentative: true));
        SetupScope(PermissionActions.Create, Restricted(invoice.OrganizationId));

        var result = await _paymentService.RecordAsync(invoice.Id, Cash(1_000_000));

        var payment = Assert.Single(_payments.Values);
        Assert.Equal(payment.Id, result.Id);
        Assert.Equal(PaymentStatus.Pending, result.Status);
        Assert.Equal(1_000_000, result.Amount);
        Assert.Equal(_actor.Id, payment.CreatedBy);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        _organizationScopeAccessor.Verify(
            a => a.GetScopeAsync(PermissionResources.Payment, PermissionActions.Confirm, It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(
            u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordAsync_ConfirmWithoutConfirmPermission_ThrowsForbiddenAndAddsNothing()
    {
        var invoice = SetupInvoice();
        SetupScope(PermissionActions.Create, Restricted(invoice.OrganizationId));
        SetupScope(PermissionActions.Confirm, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppForbiddenException>(
            () => _paymentService.RecordAsync(invoice.Id, Cash(1_000_000, confirm: true)));

        Assert.Equal("Confirm = true requires the payment:confirm permission", ex.Message);
        Assert.Empty(_payments);
        Assert.Equal(0, invoice.PaidAmount);
    }

    [Fact]
    public async Task RecordAsync_Confirm_ConfirmsAndRefreshesTheInvoiceStatus()
    {
        var invoice = SetupInvoice();
        SetupTenants(invoice, Tenant(invoice, NewUser("Representative"), isRepresentative: true));
        SetupScope(PermissionActions.Create, Unrestricted());
        SetupScope(PermissionActions.Confirm, Unrestricted());

        var result = await _paymentService.RecordAsync(invoice.Id, Cash(1_000_000, confirm: true));

        Assert.Equal(PaymentStatus.Confirmed, result.Status);
        Assert.Equal(_actor.Id, result.ConfirmedBy);
        Assert.Equal(1_000_000, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        var history = invoice.StatusHistories.Last();
        Assert.Equal(InvoiceStatus.Partial, history.NewStatus);
        Assert.Equal(_actor.Id, history.ChangedBy);
    }

    [Fact]
    public async Task RecordAsync_MoreThanTheRemainingAmount_ThrowsAndAddsNothing()
    {
        var invoice = SetupInvoice();
        SetupTenants(invoice, Tenant(invoice, NewUser("Representative"), isRepresentative: true));
        SetupScope(PermissionActions.Create, Unrestricted());

        var ex = await Assert.ThrowsAsync<DomainException>(() => _paymentService.RecordAsync(invoice.Id, Cash(3_000_001)));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task RecordAsync_NoPayer_DefaultsToTheRepresentative()
    {
        var invoice = SetupInvoice();
        var representative = NewUser("Representative");
        SetupTenants(
            invoice,
            Tenant(invoice, NewUser("Roommate"), isRepresentative: false),
            Tenant(invoice, representative, isRepresentative: true));
        SetupScope(PermissionActions.Create, Unrestricted());

        var result = await _paymentService.RecordAsync(invoice.Id, Cash(1_000_000));

        Assert.Equal(representative.Id, result.PayerUserId);
        Assert.Null(result.PayerNameOverride);
    }

    [Fact]
    public async Task RecordAsync_PayerWhoIsATenant_KeepsThem()
    {
        var invoice = SetupInvoice();
        var roommate = NewUser("Roommate");
        SetupTenants(
            invoice,
            Tenant(invoice, roommate, isRepresentative: false),
            Tenant(invoice, NewUser("Representative"), isRepresentative: true));
        SetupScope(PermissionActions.Create, Unrestricted());

        var result = await _paymentService.RecordAsync(invoice.Id, Cash(1_000_000) with { PayerUserId = roommate.Id });

        Assert.Equal(roommate.Id, result.PayerUserId);
    }

    [Fact]
    public async Task RecordAsync_PayerWhoIsNotATenant_ThrowsValidationAndAddsNothing()
    {
        var invoice = SetupInvoice();
        SetupTenants(invoice, Tenant(invoice, NewUser("Representative"), isRepresentative: true));
        SetupScope(PermissionActions.Create, Unrestricted());

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => _paymentService.RecordAsync(invoice.Id, Cash(1_000_000) with { PayerUserId = Guid.NewGuid() }));

        Assert.Equal(["PayerUserId must be a tenant of the lease"], ex.Errors!["payerUserId"]);
        Assert.Empty(_payments);
    }

    [Fact]
    public async Task RecordAsync_PayerName_DoesNotLookUpTheTenants()
    {
        var invoice = SetupInvoice();
        SetupScope(PermissionActions.Create, Unrestricted());

        var result = await _paymentService.RecordAsync(invoice.Id, Cash(1_000_000) with { PayerNameOverride = " Walk-in " });

        Assert.Null(result.PayerUserId);
        Assert.Equal("Walk-in", result.PayerNameOverride);
        Assert.Equal("Walk-in", result.PayerName);
        _leaseRepository.Verify(r => r.ListTenantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownPayment_ThrowsNotFound()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.GetByIdAsync(id));

        Assert.Equal(NotFoundMessages.Payment(id), ex.Message);
    }

    [Fact]
    public async Task GetByIdAsync_PaymentOfAnotherOrganization_ThrowsNotFound()
    {
        var invoice = SetupInvoice();
        var payment = AddPayment(invoice);
        SetupScope(PermissionActions.Read, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.GetByIdAsync(payment.Id));

        Assert.Equal(NotFoundMessages.Payment(payment.Id), ex.Message);
    }

    [Fact]
    public async Task GetByIdAsync_MapsTheInvoiceLeaseAndPayer()
    {
        var invoice = SetupInvoice();
        var payer = NewUser("Representative");
        var payment = AddPayment(invoice, payerUserId: payer.Id);
        TestEntities.Set(payment, new { PayerUser = payer });
        SetupScope(PermissionActions.Read, Restricted(invoice.OrganizationId));

        var result = await _paymentService.GetByIdAsync(payment.Id);

        Assert.Equal(payment.Id, result.Id);
        Assert.Equal(invoice.OrganizationId, result.OrganizationId);
        Assert.Equal(invoice.Id, result.InvoiceId);
        Assert.Equal("INV-202606-0001", result.InvoiceNumber);
        Assert.Equal(invoice.LeaseId, result.LeaseId);
        Assert.Equal(invoice.Lease!.LeaseCode, result.LeaseCode);
        Assert.Equal("101", result.RoomNumber);
        Assert.Equal("Sunrise House", result.PropertyName);
        Assert.Equal(payer.Id, result.PayerUserId);
        Assert.Equal("Representative", result.PayerName);
        Assert.Equal(1_000_000, result.Amount);
        Assert.Equal(PaymentMethod.Cash, result.Method);
    }

    [Fact]
    public async Task ConfirmAsync_UnknownPayment_ThrowsNotFoundWithoutLockingAnything()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.ConfirmAsync(id));

        Assert.Equal(NotFoundMessages.Payment(id), ex.Message);
        Assert.Empty(_locks);
    }

    [Fact]
    public async Task ConfirmAsync_PaymentOfAnotherOrganization_ThrowsNotFoundWithoutLockingThePayment()
    {
        var invoice = SetupInvoice();
        var payment = AddPayment(invoice);
        SetupPaymentForUpdate(payment);
        SetupScope(PermissionActions.Confirm, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.ConfirmAsync(payment.Id));

        Assert.Equal(NotFoundMessages.Payment(payment.Id), ex.Message);
        Assert.Equal(["invoice"], _locks);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task ConfirmAsync_LocksTheInvoiceThenThePaymentAndRefreshesTheStatus()
    {
        var invoice = SetupInvoice();
        var payment = AddPayment(invoice, amount: 3_000_000);
        SetupPaymentForUpdate(payment);
        SetupScope(PermissionActions.Confirm, Restricted(invoice.OrganizationId));

        var result = await _paymentService.ConfirmAsync(payment.Id);

        Assert.Equal(["invoice", "payment"], _locks);
        Assert.Equal(PaymentStatus.Confirmed, result.Status);
        Assert.Equal(_actor.Id, payment.ConfirmedBy);
        Assert.Equal(3_000_000, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(_actor.Id, invoice.StatusHistories.Last().ChangedBy);
    }

    // Due yesterday, mark-overdue-invoices has not run yet: the 100,000 late fee is owed on top of the 3,000,000
    private Invoice SetupPastDueInvoiceNotYetMarkedOverdue()
    {
        var invoice = SetupInvoice();
        TestEntities.Set(invoice, new { DueDate = Today.AddDays(-1) });
        var property = Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Sunrise House", null, null, null, null, null, IsActive: true, DefaultBillingDay: null,
                new LateFeePolicy(LateFeeType.Fixed, 100_000, null)),
            SentinelActors.System);
        _propertyRepository
            .Setup(r => r.GetByLeaseIdAsync(invoice.LeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        return invoice;
    }

    [Fact]
    public async Task ConfirmAsync_PastDueInvoiceNotYetMarkedOverdue_AddsTheLateFeeBeforeTakingThePayment()
    {
        var invoice = SetupPastDueInvoiceNotYetMarkedOverdue();
        var payment = AddPayment(invoice, amount: 3_000_000);
        SetupPaymentForUpdate(payment);
        SetupScope(PermissionActions.Confirm, Unrestricted());

        await _paymentService.ConfirmAsync(payment.Id);

        Assert.Equal(100_000, invoice.LateFeeAmount);
        Assert.Equal(3_100_000, invoice.TotalAmount);
        Assert.Equal(3_000_000, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
    }

    [Fact]
    public async Task RecordAsync_PastDueInvoiceNotYetMarkedOverdue_AcceptsTheAmountIncludingTheLateFee()
    {
        var invoice = SetupPastDueInvoiceNotYetMarkedOverdue();
        SetupTenants(invoice);
        SetupScope(PermissionActions.Create, Unrestricted());
        SetupScope(PermissionActions.Confirm, Unrestricted());

        await _paymentService.RecordAsync(invoice.Id, Cash(3_100_000, confirm: true));

        Assert.Equal(3_100_000, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task ConfirmAsync_ExceedingTheRemainingAmount_ThrowsAndLeavesThePaymentPending()
    {
        var invoice = SetupInvoice();
        var first = AddPayment(invoice, amount: 2_000_000);
        var second = AddPayment(invoice, amount: 2_000_000);
        SetupPaymentForUpdate(first);
        SetupPaymentForUpdate(second);
        SetupScope(PermissionActions.Confirm, Unrestricted());
        await _paymentService.ConfirmAsync(first.Id);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _paymentService.ConfirmAsync(second.Id));

        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
        Assert.Equal(PaymentStatus.Pending, second.Status);
        Assert.Equal(2_000_000, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

    [Fact]
    public async Task VoidAsync_ConfirmedPayment_TakesItOffAndRefreshesTheStatus()
    {
        var invoice = SetupInvoice();
        var payment = AddPayment(invoice);
        SetupPaymentForUpdate(payment);
        SetupScope(PermissionActions.Confirm, Unrestricted());
        SetupScope(PermissionActions.Void, Restricted(invoice.OrganizationId));
        await _paymentService.ConfirmAsync(payment.Id);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);

        var result = await _paymentService.VoidAsync(payment.Id, new VoidPaymentRequest { Reason = "Bounced transfer" });

        Assert.Equal(PaymentStatus.Voided, result.Status);
        Assert.Equal("Bounced transfer", result.VoidReason);
        Assert.Equal(_actor.Id, result.VoidedBy);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(["invoice", "payment", "invoice", "payment"], _locks);
    }

    [Fact]
    public async Task VoidAsync_PaymentOfAnotherOrganization_ThrowsNotFound()
    {
        var invoice = SetupInvoice();
        var payment = AddPayment(invoice);
        SetupPaymentForUpdate(payment);
        SetupScope(PermissionActions.Void, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _paymentService.VoidAsync(payment.Id, new VoidPaymentRequest { Reason = "Bounced transfer" }));

        Assert.Equal(NotFoundMessages.Payment(payment.Id), ex.Message);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task VoidAsync_UnknownPayment_ThrowsNotFoundWithoutLockingAnything()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _paymentService.VoidAsync(id, new VoidPaymentRequest { Reason = "Bounced transfer" }));

        Assert.Equal(NotFoundMessages.Payment(id), ex.Message);
        Assert.Empty(_locks);
    }

    [Fact]
    public async Task VoidAsync_PendingPayment_VoidsItAndLeavesThePaidAmount()
    {
        var invoice = SetupInvoice();
        var payment = AddPayment(invoice);
        SetupPaymentForUpdate(payment);
        SetupScope(PermissionActions.Void, Restricted(invoice.OrganizationId));

        var result = await _paymentService.VoidAsync(payment.Id, new VoidPaymentRequest { Reason = "Typed the wrong amount" });

        Assert.Equal(PaymentStatus.Voided, result.Status);
        Assert.Null(result.ConfirmedAt);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(["invoice", "payment"], _locks);
    }

    [Fact]
    public async Task GetByInvoiceAsync_UnknownInvoice_ThrowsNotFound()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.GetByInvoiceAsync(id));

        Assert.Equal(NotFoundMessages.Invoice(id), ex.Message);
    }

    [Fact]
    public async Task GetByInvoiceAsync_InvoiceOfAnotherOrganization_ThrowsNotFoundWithoutListing()
    {
        var invoice = SetupInvoice();
        _invoiceRepository
            .Setup(r => r.GetOrganizationIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice.OrganizationId);
        SetupScope(PermissionActions.Read, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(() => _paymentService.GetByInvoiceAsync(invoice.Id));

        Assert.Equal(NotFoundMessages.Invoice(invoice.Id), ex.Message);
        _paymentRepository.Verify(
            r => r.ListByInvoiceIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByInvoiceAsync_MapsThePaymentsInTheRepositoryOrder()
    {
        var invoice = SetupInvoice();
        var first = TestEntities.Set(AddPayment(invoice, amount: 1_000_000), new { Invoice = invoice });
        var second = TestEntities.Set(AddPayment(invoice, amount: 500_000), new { Invoice = invoice });
        _invoiceRepository
            .Setup(r => r.GetOrganizationIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice.OrganizationId);
        _paymentRepository
            .Setup(r => r.ListByInvoiceIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment> { first, second });
        SetupScope(PermissionActions.Read, Restricted(invoice.OrganizationId));

        var result = await _paymentService.GetByInvoiceAsync(invoice.Id);

        Assert.Equal([first.Id, second.Id], result.Select(p => p.Id));
        Assert.Equal([1_000_000m, 500_000m], result.Select(p => p.Amount));
        Assert.All(result, p => Assert.Equal("INV-202606-0001", p.InvoiceNumber));
    }

    [Fact]
    public async Task GetAllAsync_UnrestrictedWithoutOrganization_SearchesEveryOrganization()
    {
        SetupScope(PermissionActions.Read, Unrestricted());

        await _paymentService.GetAllAsync(new PaymentListQuery());

        Assert.Null(_search!.OrganizationId);
        Assert.Null(_search.AllowedOrganizationIds);
        Assert.Equal(SortOrder.Desc, _search.SortOrder);
    }

    [Fact]
    public async Task GetAllAsync_RestrictedWithoutOrganization_SearchesTheAllowedOrganizations()
    {
        var organizationId = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Restricted(organizationId));

        await _paymentService.GetAllAsync(new PaymentListQuery());

        Assert.Null(_search!.OrganizationId);
        Assert.Equal([organizationId], _search.AllowedOrganizationIds!);
    }

    [Fact]
    public async Task GetAllAsync_OrganizationOutsideTheScope_ThrowsNotFound()
    {
        var organizationId = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Restricted(Guid.NewGuid()));

        var ex = await Assert.ThrowsAsync<AppNotFoundException>(
            () => _paymentService.GetAllAsync(new PaymentListQuery { OrganizationId = organizationId }));

        Assert.Equal(NotFoundMessages.Organization(organizationId), ex.Message);
        Assert.Null(_search);
    }

    [Fact]
    public async Task GetAllAsync_OrganizationFromTheHeader_FiltersByIt()
    {
        var organizationId = Guid.NewGuid();
        _currentOrganizationAccessor.SetupGet(a => a.OrganizationId).Returns(organizationId);
        SetupScope(PermissionActions.Read, Restricted(organizationId));

        await _paymentService.GetAllAsync(new PaymentListQuery());

        Assert.Equal(organizationId, _search!.OrganizationId);
        Assert.Null(_search.AllowedOrganizationIds);
    }

    [Fact]
    public async Task GetAllAsync_PassesTheFiltersAndTurnsTheDatesIntoAHalfOpenUtcRange()
    {
        var propertyId = Guid.NewGuid();
        SetupScope(PermissionActions.Read, Unrestricted());

        await _paymentService.GetAllAsync(new PaymentListQuery
        {
            PropertyId = propertyId,
            Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Confirmed,
            From = new DateOnly(2026, 6, 1),
            To = new DateOnly(2026, 6, 30),
            Search = "INV-202606",
            SortBy = "amount",
            SortOrder = SortOrder.Asc
        });

        Assert.Equal(propertyId, _search!.PropertyId);
        Assert.Equal(PaymentMethod.BankTransfer, _search.Method);
        Assert.Equal(PaymentStatus.Confirmed, _search.Status);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), _search.PaidFrom);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), _search.PaidBefore);
        Assert.Equal("INV-202606", _search.Search);
        Assert.Equal(SortOrder.Asc, _search.SortOrder);
        Assert.Equal("p => Convert(p.Amount, Object)", _search.SortField.ToString());
    }

    [Fact]
    public async Task GetAllAsync_MapsTheItems()
    {
        var invoice = SetupInvoice();
        var payment = TestEntities.Set(AddPayment(invoice), new { Invoice = invoice });
        SetupSearchReturns([payment]);
        SetupScope(PermissionActions.Read, Unrestricted());

        var result = await _paymentService.GetAllAsync(new PaymentListQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal(payment.Id, item.Id);
        Assert.Equal("INV-202606-0001", item.InvoiceNumber);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task GetAllAsync_UnknownSortByAndOnlyFrom_SortsByPaymentDateWithAnOpenEndedRange()
    {
        SetupScope(PermissionActions.Read, Unrestricted());

        await _paymentService.GetAllAsync(new PaymentListQuery { From = new DateOnly(2026, 6, 1), SortBy = "payerName" });

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), _search!.PaidFrom);
        Assert.Null(_search.PaidBefore);
        Assert.Equal("p => Convert(p.PaymentDate, Object)", _search.SortField.ToString());
    }

    [Fact]
    public async Task GetAllAsync_ToIsTheLastRepresentableDate_SearchesWithoutAnUpperBound()
    {
        SetupScope(PermissionActions.Read, Unrestricted());

        await _paymentService.GetAllAsync(new PaymentListQuery { To = DateOnly.MaxValue });

        Assert.Null(_search!.PaidBefore);
    }
}
