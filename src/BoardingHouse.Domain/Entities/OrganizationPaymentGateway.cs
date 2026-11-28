using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class OrganizationPaymentGateway : AuditableEntity
{
    public const int MerchantCodeMaxLength = 50;
    public const string NotAvailableMessage = "Online payment through this gateway is not available for the organization";
    public const string SecretRequiredMessage = "Secret is required";

    public Guid OrganizationId { get; private set; }
    public PaymentGateway Gateway { get; private set; }
    public string MerchantCode { get; private set; } = null!;

    public string SecretEncrypted { get; private set; } = null!;

    public bool IsActive { get; private set; }

    private OrganizationPaymentGateway() { }

    public static OrganizationPaymentGateway Create(
        Guid organizationId,
        PaymentGateway gateway,
        string merchantCode,
        string? secretEncrypted,
        bool isActive,
        Guid createdBy)
    {
        var account = new OrganizationPaymentGateway
        {
            OrganizationId = organizationId,
            Gateway = gateway,
            CreatedBy = createdBy
        };

        account.Update(merchantCode, secretEncrypted, isActive);
        return account;
    }

    public void Update(string merchantCode, string? secretEncrypted, bool isActive)
    {
        var code = merchantCode?.Trim();

        if (string.IsNullOrEmpty(code))
        {
            throw new DomainException("MerchantCode is required");
        }

        if (code.Length > MerchantCodeMaxLength)
        {
            throw new DomainException($"MerchantCode must not exceed {MerchantCodeMaxLength} characters");
        }

        if (string.IsNullOrEmpty(secretEncrypted) && string.IsNullOrEmpty(SecretEncrypted))
        {
            throw new DomainException(SecretRequiredMessage);
        }

        MerchantCode = code;
        SecretEncrypted = string.IsNullOrEmpty(secretEncrypted) ? SecretEncrypted : secretEncrypted;
        IsActive = isActive;
    }

    public void EnsureActive()
    {
        if (!IsActive)
        {
            throw DomainException.Conflict(NotAvailableMessage);
        }
    }
}
