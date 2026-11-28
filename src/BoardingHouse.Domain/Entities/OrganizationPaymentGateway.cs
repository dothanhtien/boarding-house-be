using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class OrganizationPaymentGateway : AuditableEntity
{
    public const int MerchantCodeMaxLength = 50;
    public const int FirstCredentialsVersion = 1;
    public const string NotAvailableMessage = "Online payment through this gateway is not available for the organization";
    public const string SecretRequiredMessage = "Secret is required";

    public Guid OrganizationId { get; private set; }
    public PaymentGateway Gateway { get; private set; }
    public string MerchantCode { get; private set; } = null!;

    public string SecretEncrypted { get; private set; } = null!;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Version of <see cref="MerchantCode"/> + <see cref="SecretEncrypted"/>, increased on every change of either; each
    /// payment link records the version it was signed with, the replaced ones stay in <see cref="CredentialHistories"/>.
    /// </summary>
    public int CredentialsVersion { get; private set; }

    private readonly List<OrganizationPaymentGatewayCredentialHistory> _credentialHistories = [];

    public IReadOnlyCollection<OrganizationPaymentGatewayCredentialHistory> CredentialHistories => _credentialHistories;

    private OrganizationPaymentGateway() { }

    public static OrganizationPaymentGateway Create(
        Guid organizationId,
        PaymentGateway gateway,
        string merchantCode,
        string? secretEncrypted,
        bool isActive,
        Guid createdBy,
        DateTimeOffset now)
    {
        var account = new OrganizationPaymentGateway
        {
            OrganizationId = organizationId,
            Gateway = gateway,
            CredentialsVersion = FirstCredentialsVersion,
            CreatedBy = createdBy
        };

        account.Update(merchantCode, secretEncrypted, isActive, createdBy, now);
        return account;
    }

    public void Update(string merchantCode, string? secretEncrypted, bool isActive, Guid changedBy, DateTimeOffset now)
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

        var secretReplaced = !string.IsNullOrEmpty(secretEncrypted);
        var isCreated = !string.IsNullOrEmpty(SecretEncrypted);
        if (isCreated && (secretReplaced || code != MerchantCode))
        {
            _credentialHistories.Add(new OrganizationPaymentGatewayCredentialHistory(
                Id, CredentialsVersion, MerchantCode, SecretEncrypted, changedBy, now));
            CredentialsVersion++;
        }

        MerchantCode = code;
        SecretEncrypted = secretReplaced ? secretEncrypted! : SecretEncrypted;
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
