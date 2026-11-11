using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.UtilityServices;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public class UtilityServiceService(
    IUtilityServiceRepository utilityServiceRepository,
    IPropertyRepository propertyRepository,
    ILeaseRepository leaseRepository,
    IUtilityReadingRepository utilityReadingRepository,
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
        var (service, organizationId) = await utilityServiceRepository.GetByIdWithOrganizationIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(id));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.UtilityService, PermissionActions.Read, NotFoundMessages.UtilityService(id), cancellationToken);

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

                var created = UtilityService.Create(
                    request.PropertyId,
                    request.Type,
                    new UtilityServiceDetails(
                        request.Name, request.BillingMethod, request.Unit, request.DefaultUnitPrice, request.IsActive),
                    currentUserAccessor.RequiredUser.Id);

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
                // FOR UPDATE serializes with a concurrent soft-delete (direct or via PropertyService.DeleteAsync), so
                // this update never lands on a service deleted after our read
                var (existing, organizationId) = await utilityServiceRepository.GetByIdWithOrganizationIdForUpdateAsync(id, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(id));

                await organizationScopeAccessor.EnsureScopeAsync(
                    organizationId, PermissionResources.UtilityService, PermissionActions.Update, NotFoundMessages.UtilityService(id), ct);

                var current = existing.Details;
                var details = new UtilityServiceDetails(
                    request.Name.OrKeep(current.Name)!,
                    request.BillingMethod.OrKeep(current.BillingMethod),
                    request.Unit.OrKeep(current.Unit)!,
                    request.DefaultUnitPrice.OrKeep(current.DefaultUnitPrice),
                    request.IsActive.OrKeep(current.IsActive));

                // Usage needs 2 queries: only look it up when the billing method actually changes
                var usage = details.BillingMethod == current.BillingMethod
                    ? UtilityServiceUsage.None
                    : await GetUsageAsync(existing.Id, ct);

                existing.Update(details, usage);

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
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var (service, organizationId) = await utilityServiceRepository.GetByIdWithOrganizationIdForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(id));

            await organizationScopeAccessor.EnsureScopeAsync(
                organizationId, PermissionResources.UtilityService, PermissionActions.Delete, NotFoundMessages.UtilityService(id), ct);

            if (await leaseRepository.ExistsActiveUtilityServiceInOpenLeaseAsync(service.Id, ct))
            {
                logger.LogWarning("Delete utility service failed: still used by an open lease ({UtilityServiceId})", id);
                throw new AppConflictException(
                    "Cannot delete a utility service used by a draft or active lease — set IsActive to false to stop offering it, or deactivate it on those leases first");
            }

            utilityServiceRepository.SoftDelete(service);
        }, cancellationToken);

        logger.LogInformation("Utility service soft-deleted ({UtilityServiceId})", id);
    }

    private async Task<UtilityServiceUsage> GetUsageAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var usage = UtilityServiceUsage.None;

        if (await leaseRepository.ExistsUtilityServiceUsageAsync(serviceId, cancellationToken))
        {
            usage |= UtilityServiceUsage.Leases;
        }

        if (await utilityReadingRepository.ExistsByServiceIdAsync(serviceId, cancellationToken))
        {
            usage |= UtilityServiceUsage.Readings;
        }

        return usage;
    }
}
