using Mapster;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Services;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Services;

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
            ?? new OrganizationSettings { OrganizationId = organizationId };

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
        settings ??= new OrganizationSettings { OrganizationId = organizationId };

        request.Adapt(settings, OrganizationSettingsMappingConfig.UpdateConfig);
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = currentUserAccessor.RequiredUser.Id;

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
                organizationSettingsRepository.Detach(settings);
                logger.LogInformation("Organization settings creation raced with a concurrent write ({OrganizationId}); retrying as update", organizationId);

                settings = await organizationSettingsRepository.GetByOrganizationIdAsync(organizationId, cancellationToken)
                    ?? throw new AppConflictException("Organization settings could not be saved, please retry");

                request.Adapt(settings, OrganizationSettingsMappingConfig.UpdateConfig);
                settings.UpdatedAt = DateTimeOffset.UtcNow;
                settings.UpdatedBy = currentUserAccessor.RequiredUser.Id;
            }
        }

        organizationSettingsRepository.Update(settings);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Organization settings updated ({OrganizationId})", organizationId);

        return settings.Adapt<OrganizationSettingsResponse>();
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
