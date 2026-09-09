using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class OrganizationSettingsService(
    IOrganizationSettingsRepository organizationSettingsRepository,
    IOrganizationRepository organizationRepository,
    AppDbContext context,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<OrganizationSettingsService> logger) : IOrganizationSettingsService
{
    public async Task<OrganizationSettingsResponse> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
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
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var settings = await organizationSettingsRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);

        var isNew = settings is null;
        settings ??= new OrganizationSettings { OrganizationId = organizationId };

        ApplyRequest(settings, request);
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = currentUserAccessor.RequiredUser.Id;

        if (isNew)
        {
            try
            {
                await organizationSettingsRepository.AddAsync(settings, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Organization settings created ({OrganizationId})", organizationId);
                return settings.Adapt<OrganizationSettingsResponse>();
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                context.Entry(settings).State = EntityState.Detached;
                logger.LogInformation("Organization settings creation raced with a concurrent write ({OrganizationId}); retrying as update", organizationId);

                settings = await organizationSettingsRepository.GetByOrganizationIdAsync(organizationId, cancellationToken)
                    ?? throw new ConflictAppException("Organization settings could not be saved, please retry");

                ApplyRequest(settings, request);
                settings.UpdatedAt = DateTimeOffset.UtcNow;
                settings.UpdatedBy = currentUserAccessor.RequiredUser.Id;
            }
        }

        organizationSettingsRepository.Update(settings);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Organization settings updated ({OrganizationId})", organizationId);

        return settings.Adapt<OrganizationSettingsResponse>();
    }

    private async Task EnsureOrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var exists = await organizationRepository.ExistsAsync(organizationId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundAppException($"Organization {organizationId} not found");
        }
    }

    private static void ApplyRequest(OrganizationSettings settings, UpdateOrganizationSettingsRequest request)
    {
        settings.DefaultBillingDay = request.DefaultBillingDay;
        settings.LateFeeType = request.LateFeeType;
        settings.LateFeeValue = request.LateFeeValue;
        settings.LateFeeGraceDays = request.LateFeeGraceDays;
        settings.VatRate = request.VatRate;
        settings.BankAccountNumber = request.BankAccountNumber;
        settings.BankName = request.BankName;
        settings.BankAccountName = request.BankAccountName;
    }
}
