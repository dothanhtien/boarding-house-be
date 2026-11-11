using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services;

public class OrganizationSettingsService(
    IOrganizationSettingsRepository organizationSettingsRepository,
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ILogger<OrganizationSettingsService> logger) : IOrganizationSettingsService
{
    public async Task<OrganizationSettingsResponse> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(organizationId, PermissionResources.OrganizationSetting, PermissionActions.Read, cancellationToken: cancellationToken);
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var settings = await organizationSettingsRepository.GetByOrganizationIdAsync(organizationId, cancellationToken)
            ?? OrganizationSettings.DefaultsFor(organizationId);

        return settings.Adapt<OrganizationSettingsResponse>();
    }

    public async Task<OrganizationSettingsResponse> UpsertAsync(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(organizationId, PermissionResources.OrganizationSetting, PermissionActions.Update, cancellationToken: cancellationToken);
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var settings = await organizationSettingsRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);

        var isNew = settings is null;
        settings ??= OrganizationSettings.DefaultsFor(organizationId);

        Apply(request, settings);

        if (isNew)
        {
            try
            {
                await organizationSettingsRepository.AddAsync(settings, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Organization settings created ({OrganizationId})", organizationId);
                return settings.Adapt<OrganizationSettingsResponse>();
            }
            catch (UniqueConstraintViolationException)
            {
                unitOfWork.DiscardChanges();
                logger.LogInformation("Organization settings creation raced with a concurrent write ({OrganizationId}); retrying as update", organizationId);

                settings = await organizationSettingsRepository.GetByOrganizationIdAsync(organizationId, cancellationToken)
                    ?? throw new AppConflictException("Organization settings could not be saved, please retry");

                Apply(request, settings);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Organization settings updated ({OrganizationId})", organizationId);

        return settings.Adapt<OrganizationSettingsResponse>();
    }

    private void Apply(UpdateOrganizationSettingsRequest request, OrganizationSettings settings)
    {
        var current = settings.Details;
        settings.Update(
            new OrganizationSettingsDetails(
                request.DefaultBillingDay.OrKeep(current.DefaultBillingDay),
                new LateFeePolicy(
                    request.LateFeeType.OrKeep(current.LateFee.Type),
                    request.LateFeeValue.OrKeep(current.LateFee.Value),
                    request.LateFeeGraceDays.OrKeep(current.LateFee.GraceDays)),
                request.VatRate.OrKeep(current.VatRate),
                request.BankAccountNumber.OrKeep(current.BankAccountNumber),
                request.BankName.OrKeep(current.BankName),
                request.BankAccountName.OrKeep(current.BankAccountName)),
            currentUserAccessor.RequiredUser.Id,
            DateTimeOffset.UtcNow);
    }

    private async Task EnsureOrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var exists = await organizationRepository.ExistsAsync(organizationId, cancellationToken);
        if (!exists)
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId));
        }
    }
}
