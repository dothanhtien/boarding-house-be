using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.UtilityServices;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Configurations;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class UtilityServiceService(
    IUtilityServiceRepository utilityServiceRepository,
    IPropertyRepository propertyRepository,
    AppDbContext context,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<UtilityServiceService> logger) : IUtilityServiceService
{
    public async Task<List<UtilityServiceResponse>> GetAllAsync(UtilityServiceListQuery query, CancellationToken cancellationToken = default)
    {
        var property = await propertyRepository.GetByIdAsync(query.PropertyId, cancellationToken)
            ?? throw new AppNotFoundException($"Property '{query.PropertyId}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            property.OrganizationId,
            "utility-service", "read",
            $"Property '{query.PropertyId}' not found",
            cancellationToken);

        var services = await utilityServiceRepository.ListByPropertyIdAsync(
            query.PropertyId,
            query.Type,
            query.IsActive,
            cancellationToken: cancellationToken);

        return services.Adapt<List<UtilityServiceResponse>>();
    }

    public async Task<UtilityServiceResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await utilityServiceRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Utility service '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            service.Property!.OrganizationId, "utility-service", "read", $"Utility service '{id}' not found", cancellationToken);

        return service.Adapt<UtilityServiceResponse>();
    }

    public async Task<UtilityServiceResponse> CreateAsync(CreateUtilityServiceRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // FOR SHARE blocks a concurrent PropertyService.DeleteAsync (FOR UPDATE) until this insert commits
        var property = await propertyRepository.GetByIdForShareAsync(request.PropertyId, cancellationToken)
            ?? throw new AppNotFoundException($"Property '{request.PropertyId}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            property.OrganizationId, "utility-service", "create", $"Property '{request.PropertyId}' not found", cancellationToken);

        var service = new UtilityService
        {
            PropertyId = request.PropertyId,
            Name = request.Name,
            Type = request.Type,
            Unit = request.Unit,
            DefaultUnitPrice = request.DefaultUnitPrice,
            IsActive = request.IsActive,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        try
        {
            await utilityServiceRepository.AddAsync(service, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(UtilityServiceConfiguration.PropertyIdNameUniqueIndex))
        {
            logger.LogWarning("Create utility service failed: name already in use in property ({PropertyId})", request.PropertyId);
            throw new AppConflictException("A utility service with this name already exists in the property");
        }

        logger.LogInformation("Utility service created ({UtilityServiceId})", service.Id);

        return service.Adapt<UtilityServiceResponse>();
    }

    public async Task<UtilityServiceResponse> UpdateAsync(Guid id, UpdateUtilityServiceRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // FOR UPDATE serializes with a concurrent soft-delete (direct or via PropertyService.DeleteAsync) so the
        // full-row Update() below can't write back deleted_at = NULL over a delete committed after our read
        var service = await utilityServiceRepository.GetByIdWithPropertyForUpdateAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Utility service '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            service.Property!.OrganizationId, "utility-service", "update", $"Utility service '{id}' not found", cancellationToken);

        request.Adapt(service, UtilityServiceMappingConfig.UpdateConfig);

        try
        {
            utilityServiceRepository.Update(service);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(UtilityServiceConfiguration.PropertyIdNameUniqueIndex))
        {
            logger.LogWarning("Update utility service failed: name already in use in property ({UtilityServiceId})", id);
            throw new AppConflictException("A utility service with this name already exists in the property");
        }

        logger.LogInformation("Utility service updated ({UtilityServiceId})", service.Id);

        return service.Adapt<UtilityServiceResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await utilityServiceRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Utility service '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            service.Property!.OrganizationId, "utility-service", "delete", $"Utility service '{id}' not found", cancellationToken);

        utilityServiceRepository.SoftDelete(service);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Utility service soft-deleted ({UtilityServiceId})", service.Id);
    }
}
