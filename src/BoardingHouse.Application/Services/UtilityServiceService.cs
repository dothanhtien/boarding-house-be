using Mapster;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.UtilityServices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Application.Services;

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
            ?? throw new AppNotFoundException(NotFoundMessages.Property(query.PropertyId));

        await organizationScopeAccessor.EnsureScopeAsync(
            property.OrganizationId,
            PermissionResources.UtilityService, PermissionActions.Read,
            NotFoundMessages.Property(query.PropertyId),
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
            ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            service.Property!.OrganizationId, PermissionResources.UtilityService, PermissionActions.Read, NotFoundMessages.UtilityService(id), cancellationToken);

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
                    ?? throw new AppNotFoundException(NotFoundMessages.Property(request.PropertyId));

                await organizationScopeAccessor.EnsureScopeAsync(
                    property.OrganizationId, PermissionResources.UtilityService, PermissionActions.Create, NotFoundMessages.Property(request.PropertyId), ct);

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
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.UtilityServicePropertyIdName)
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
                var (existing, organizationId) = await utilityServiceRepository.GetByIdWithOrganizationIdForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(id));

                await organizationScopeAccessor.EnsureScopeAsync(
                    organizationId, PermissionResources.UtilityService, PermissionActions.Update, NotFoundMessages.UtilityService(id), ct);

                request.Adapt(existing, UtilityServiceMappingConfig.UpdateConfig);

                utilityServiceRepository.Update(existing);

                return existing;
            }, cancellationToken);

            logger.LogInformation("Utility service updated ({UtilityServiceId})", service.Id);

            return service.Adapt<UtilityServiceResponse>();
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.UtilityServicePropertyIdName)
        {
            logger.LogWarning("Update utility service failed: name already in use in property ({UtilityServiceId})", id);
            throw new AppConflictException("A utility service with this name already exists in the property");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var service = await utilityServiceRepository.GetByIdWithPropertyAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            service.Property!.OrganizationId, PermissionResources.UtilityService, PermissionActions.Delete, NotFoundMessages.UtilityService(id), cancellationToken);

        utilityServiceRepository.SoftDelete(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Utility service soft-deleted ({UtilityServiceId})", service.Id);
    }
}
