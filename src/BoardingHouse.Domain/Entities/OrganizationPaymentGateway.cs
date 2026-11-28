using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class OrganizationPaymentGateway : AuditableEntity
{
    public const int MerchantCodeMaxLength = 50;

    public Guid OrganizationId { get; private set; }
    public PaymentGateway Gateway { get; private set; }
    public string MerchantCode { get; private set; } = null!;

    public string SecretEncrypted { get; private set; } = null!;

    public bool IsActive { get; private set; }

    private OrganizationPaymentGateway() { }
}
