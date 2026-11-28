using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Entities;

public class OrganizationPaymentGatewayCredentialHistory : Entity
{
    public Guid OrganizationPaymentGatewayId { get; private set; }
    public int Version { get; private set; }
    public string MerchantCode { get; private set; } = null!;
    public string SecretEncrypted { get; private set; } = null!;
    public Guid ReplacedBy { get; private set; }
    public DateTimeOffset ReplacedAt { get; private set; }

    private OrganizationPaymentGatewayCredentialHistory() { }

    public OrganizationPaymentGatewayCredentialHistory(
        Guid organizationPaymentGatewayId,
        int version,
        string merchantCode,
        string secretEncrypted,
        Guid replacedBy,
        DateTimeOffset replacedAt)
    {
        OrganizationPaymentGatewayId = organizationPaymentGatewayId;
        Version = version;
        MerchantCode = merchantCode;
        SecretEncrypted = secretEncrypted;
        ReplacedBy = replacedBy;
        ReplacedAt = replacedAt;
    }
}
