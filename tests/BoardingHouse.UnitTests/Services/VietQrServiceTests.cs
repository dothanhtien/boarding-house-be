using Moq;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.VietQr;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

// The paid / cancelled / amount / bank-account rules live on Invoice and OrganizationSettings (see their tests):
// here only that the service wires them together and maps the response
public class VietQrServiceTests
{
    private const string VietcombankPayload3M =
        "00020101021238570010A00000072701270006970436011300110012345670208QRIBFTTA5303704540730000005802VN62170813INV202611000163044191";
    private const string VietcombankPayload1M8 =
        "00020101021238570010A00000072701270006970436011300110012345670208QRIBFTTA5303704540718000005802VN62170813INV202611000163044A39";

    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IOrganizationSettingsRepository> _organizationSettingsRepository = new();
    private readonly Mock<IOrganizationScopeAccessor> _organizationScopeAccessor = new();
    private readonly Mock<ITenantPortalQueries> _tenantPortalQueries = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _tenantUserId = Guid.NewGuid();
    private readonly VietQrService _vietQrService;

    public VietQrServiceTests()
    {
        _organizationScopeAccessor
            .Setup(a => a.GetScopeAsync(PermissionResources.Invoice, PermissionActions.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Restricted(_organizationId));

        _currentUserAccessor
            .SetupGet(a => a.RequiredUser)
            .Returns(TestEntities.New<User>(new { Id = _tenantUserId, FullName = "Tenant" }));

        _vietQrService = new VietQrService(
            _invoiceRepository.Object,
            _organizationSettingsRepository.Object,
            _organizationScopeAccessor.Object,
            _tenantPortalQueries.Object,
            _currentUserAccessor.Object);
    }

    private static OrganizationScope Restricted(params Guid[] organizationIds) =>
        new() { IsUnrestricted = false, OrganizationIds = organizationIds.ToHashSet() };

    // 3,000,000 invoice INV-202611-0001 of _organizationId
    private Invoice SetupInvoice(decimal paidAmount = 0, InvoiceStatus status = InvoiceStatus.Pending, Guid? organizationId = null)
    {
        var invoice = TestEntities.New<Invoice>(new
        {
            OrganizationId = organizationId ?? _organizationId,
            LeaseId = Guid.NewGuid(),
            InvoiceNumber = "INV-202611-0001",
            BillingMonth = BillingMonth.FromFirstDay(new DateOnly(2026, 11, 1)),
            DueDate = new DateOnly(2026, 11, 10),
            Subtotal = 3_000_000m,
            TotalAmount = 3_000_000m,
            PaidAmount = paidAmount,
            Status = status,
            CreatedBy = SentinelActors.System
        });

        _invoiceRepository
            .Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        return invoice;
    }

    private void SetupSettings(string? bankBin = "970436", string? accountNumber = "0011001234567")
    {
        var settings = TestEntities.Set(OrganizationSettings.DefaultsFor(_organizationId), new
        {
            BankBin = bankBin,
            BankAccountNumber = accountNumber,
            BankAccountName = "NGUYEN VAN A"
        });

        _organizationSettingsRepository
            .Setup(r => r.GetByOrganizationIdAsync(_organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
    }

    [Fact]
    public void GetBanks_ReturnsEveryBankOfTheList()
    {
        var banks = _vietQrService.GetBanks();

        Assert.Equal(VietQrBanks.All.Select(b => b.Bin), banks.Select(b => b.Bin));
        Assert.Contains(banks, b => b is { Bin: "970436", Code: "VCB", ShortName: "Vietcombank" });
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_UnpaidInvoice_ReturnsTheWholeAmount()
    {
        var invoice = SetupInvoice();
        SetupSettings();

        var response = await _vietQrService.GetInvoicePaymentQrAsync(invoice.Id);

        Assert.Equal(invoice.Id, response.InvoiceId);
        Assert.Equal("INV-202611-0001", response.InvoiceNumber);
        Assert.Equal(VietcombankPayload3M, response.Payload);
        Assert.Equal(("970436", "VCB", "Vietcombank"), (response.BankBin, response.BankCode, response.BankShortName));
        Assert.Equal(("0011001234567", "NGUYEN VAN A"), (response.AccountNumber, response.AccountName));
        Assert.Equal(3_000_000m, response.Amount);
        Assert.Equal("INV2026110001", response.TransferContent);
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_PartiallyPaidInvoice_ReturnsTheRemainingAmount()
    {
        var invoice = SetupInvoice(paidAmount: 1_200_000, status: InvoiceStatus.Partial);
        SetupSettings();

        var response = await _vietQrService.GetInvoicePaymentQrAsync(invoice.Id);

        Assert.Equal(1_800_000m, response.Amount);
        Assert.Equal(VietcombankPayload1M8, response.Payload);
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_PaidInvoice_ThrowsTheInvoiceConflictWithoutReadingSettings()
    {
        var invoice = SetupInvoice(paidAmount: 3_000_000, status: InvoiceStatus.Paid);

        var exception = await Assert.ThrowsAsync<DomainException>(() => _vietQrService.GetInvoicePaymentQrAsync(invoice.Id));

        Assert.Equal(Invoice.AlreadyPaidMessage, exception.Message);
        _organizationSettingsRepository.Verify(
            r => r.GetByOrganizationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_NoSettingsRow_ThrowsConflict()
    {
        var invoice = SetupInvoice();

        var exception = await Assert.ThrowsAsync<AppConflictException>(() => _vietQrService.GetInvoicePaymentQrAsync(invoice.Id));

        Assert.Equal(OrganizationSettings.BankAccountNotConfiguredMessage, exception.Message);
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_SettingsWithoutBankBin_ThrowsTheSettingsConflict()
    {
        var invoice = SetupInvoice();
        SetupSettings(bankBin: null);

        var exception = await Assert.ThrowsAsync<DomainException>(() => _vietQrService.GetInvoicePaymentQrAsync(invoice.Id));

        Assert.Equal(DomainErrorKind.Conflict, exception.Kind);
        Assert.Equal(OrganizationSettings.BankAccountNotConfiguredMessage, exception.Message);
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_UnknownInvoice_ThrowsNotFound()
    {
        var id = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<AppNotFoundException>(() => _vietQrService.GetInvoicePaymentQrAsync(id));

        Assert.Equal(NotFoundMessages.Invoice(id), exception.Message);
    }

    [Fact]
    public async Task GetInvoicePaymentQrAsync_InvoiceOfAnotherOrganization_ThrowsNotFoundWithoutReadingSettings()
    {
        var invoice = SetupInvoice(organizationId: Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<AppNotFoundException>(() => _vietQrService.GetInvoicePaymentQrAsync(invoice.Id));

        Assert.Equal(NotFoundMessages.Invoice(invoice.Id), exception.Message);
        _organizationSettingsRepository.Verify(
            r => r.GetByOrganizationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyInvoicePaymentQrAsync_InvoiceNotVisible_ThrowsNotFound_WithoutLoadingIt()
    {
        var invoiceId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<AppNotFoundException>(() => _vietQrService.GetMyInvoicePaymentQrAsync(invoiceId));

        Assert.Equal(NotFoundMessages.Invoice(invoiceId), exception.Message);
        _tenantPortalQueries.Verify(q => q.IsInvoiceVisibleAsync(_tenantUserId, invoiceId, It.IsAny<CancellationToken>()));
        _invoiceRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyInvoicePaymentQrAsync_VisibleInvoice_ReturnsQr_WithoutOrganizationScope()
    {
        var invoice = SetupInvoice();
        SetupSettings();
        _tenantPortalQueries
            .Setup(q => q.IsInvoiceVisibleAsync(_tenantUserId, invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await _vietQrService.GetMyInvoicePaymentQrAsync(invoice.Id);

        // same QR as the staff endpoint; a tenant has no invoice:read grant, so no scope check
        Assert.Equal(VietcombankPayload3M, response.Payload);
        Assert.Equal(3_000_000m, response.Amount);
        _organizationScopeAccessor.Verify(
            a => a.GetScopeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
