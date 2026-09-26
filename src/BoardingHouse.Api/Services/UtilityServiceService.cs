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
    IUnitOfWork unitOfWork,
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
        try
        {
            var service = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // FOR SHARE blocks a concurrent PropertyService.DeleteAsync (FOR UPDATE) until this insert commits
                var property = await propertyRepository.GetByIdForShareAsync(request.PropertyId, ct)
                    ?? throw new AppNotFoundException($"Property '{request.PropertyId}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    property.OrganizationId, "utility-service", "create", $"Property '{request.PropertyId}' not found", ct);

                var created = new UtilityService
                {
                    PropertyId = request.PropertyId,
                    Name = request.Name,
                    Type = request.Type,
                    Unit = request.Unit,
                    DefaultUnitPrice = request.DefaultUnitPrice,
                    IsActive = request.IsActive,
                    CreatedBy = currentUserAccessor.RequiredUser.Id
                };

                await utilityServiceRepository.AddAsync(created, ct);

                return created;
            }, cancellationToken);

            logger.LogInformation("Utility service created ({UtilityServiceId})", service.Id);

            return service.Adapt<UtilityServiceResponse>();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(UtilityServiceConfiguration.PropertyIdNameUniqueIndex))
        {
            logger.LogWarning("Create utility service failed: name already in use in property ({PropertyId})", request.PropertyId);
            throw new AppConflictException("A utility service with this name already exists in the property");
        }
    }

    public async Task<UtilityServiceResponse> UpdateAsync(Guid id, UpdateUtilityServiceRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // FOR UPDATE serializes with a concurrent soft-delete (direct or via PropertyService.DeleteAsync) so the
                // full-row Update() below can't write back deleted_at = NULL over a delete committed after our read
                var existing = await utilityServiceRepository.GetByIdWithPropertyForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException($"Utility service '{id}' not found");

                await organizationScopeAccessor.EnsureScopeAsync(
                    existing.Property!.OrganizationId, "utility-service", "update", $"Utility service '{id}' not found", ct);

                request.Adapt(existing, UtilityServiceMappingConfig.UpdateConfig);

                utilityServiceRepository.Update(existing);

                return existing;
            }, cancellationToken);

            logger.LogInformation("Utility service updated ({UtilityServiceId})", service.Id);

            return service.Adapt<UtilityServiceResponse>();
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(UtilityServiceConfiguration.PropertyIdNameUniqueIndex))
        {
            logger.LogWarning("Update utility service failed: name already in use in property ({UtilityServiceId})", id);
            throw new AppConflictException("A utility service with this name already exists in the property");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await utilityServiceRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Utility service '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            service.Property!.OrganizationId, "utility-service", "delete", $"Utility service '{id}' not found", cancellationToken);

        utilityServiceRepository.SoftDelete(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Utility service soft-deleted ({UtilityServiceId})", service.Id);
    }
}
