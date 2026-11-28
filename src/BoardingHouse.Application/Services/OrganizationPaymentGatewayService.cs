using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Security;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public class OrganizationPaymentGatewayService(
    IOrganizationPaymentGatewayRepository organizationPaymentGatewayRepository,
    IOrganizationRepository organizationRepository,
    ISecretProtector secretProtector,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ILogger<OrganizationPaymentGatewayService> logger) : IOrganizationPaymentGatewayService
{
    public const string ConcurrentChangeMessage =
        "The payment gateway was changed by someone else at the same time, reload it and try again";

    public async Task<List<OrganizationPaymentGatewayResponse>> ListAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.OrganizationSetting, PermissionActions.Read, cancellationToken: cancellationToken);
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var accounts = await organizationPaymentGatewayRepository.ListByOrganizationIdAsync(organizationId, cancellationToken);

        return accounts.Adapt<List<OrganizationPaymentGatewayResponse>>();
    }

    public async Task<OrganizationPaymentGatewayResponse> UpsertAsync(
        Guid organizationId,
        PaymentGateway gateway,
        UpsertOrganizationPaymentGatewayRequest request,
        CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.OrganizationSetting, PermissionActions.Update, cancellationToken: cancellationToken);
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var secretEncrypted = request.Secret is null ? null : secretProtector.Protect(request.Secret.Trim());
        var account = await organizationPaymentGatewayRepository.GetAsync(organizationId, gateway, cancellationToken);

        if (account is null)
        {
            account = OrganizationPaymentGateway.Create(
                organizationId,
                gateway,
                request.MerchantCode,
                secretEncrypted,
                request.IsActive!.Value,
                currentUserAccessor.RequiredUser.Id,
                DateTimeOffset.UtcNow);
            await organizationPaymentGatewayRepository.AddAsync(account, cancellationToken);
        }
        else
        {
            account.Update(
                request.MerchantCode,
                secretEncrypted,
                request.IsActive!.Value,
                currentUserAccessor.RequiredUser.Id,
                DateTimeOffset.UtcNow);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
            when (ex.ConstraintName == UniqueIndexNames.OrganizationPaymentGatewayCredentialHistoryGatewayIdVersion)
        {
            throw new AppConflictException(ConcurrentChangeMessage);
        }

        logger.LogInformation(
            "Payment gateway {Gateway} saved for organization {OrganizationId} (active: {IsActive}, secret changed: {SecretChanged}, credentials version: {CredentialsVersion})",
            gateway, organizationId, account.IsActive, secretEncrypted is not null, account.CredentialsVersion);

        return account.Adapt<OrganizationPaymentGatewayResponse>();
    }

    private async Task EnsureOrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!await organizationRepository.ExistsAsync(organizationId, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId));
        }
    }
}
