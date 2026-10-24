using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using Mapster;

namespace BoardingHouse.Application.Services;

public class LeaseUtilityServiceService(
    ILeaseRepository leaseRepository,
    IUtilityServiceRepository utilityServiceRepository,
    ILeaseUtilityServiceRepository leaseUtilityServiceRepository,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<LeaseUtilityServiceService> logger) : ILeaseUtilityServiceService
{
    public async Task<List<LeaseUtilityServiceResponse>> GetAllAsync(Guid leaseId, CancellationToken cancellationToken = default)
    {
        var organizationId = await leaseRepository.GetOrganizationIdAsync(leaseId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Lease(leaseId));

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId,
            PermissionResources.Lease,
            PermissionActions.Read,
            NotFoundMessages.Lease(leaseId),
            cancellationToken);

        var services = await leaseUtilityServiceRepository.ListByLeaseIdAsync(leaseId, cancellationToken);

        return services.Adapt<List<LeaseUtilityServiceResponse>>();
    }

    public async Task<LeaseUtilityServiceResponse> UpsertAsync(
        Guid leaseId, Guid serviceId, UpsertLeaseUtilityServiceRequest request, CancellationToken cancellationToken = default)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // FOR UPDATE serializes concurrent upserts of the same (lease, service) pair and status transitions
            var lease = await leaseRepository.GetByIdWithRoomForUpdateAsync(leaseId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(leaseId));

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Update,
                NotFoundMessages.Lease(leaseId),
                ct);

            if (lease.Status is not (LeaseStatus.Draft or LeaseStatus.Active))
            {
                throw new AppConflictException("Utility services can only be changed on draft or active leases");
            }

            var service = await utilityServiceRepository.GetByIdForShareAsync(serviceId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(serviceId));

            if (service.PropertyId != lease.Room!.PropertyId)
            {
                throw new AppValidationException("The utility service must belong to the property of the lease's room");
            }

            var leaseUtilityService = await leaseUtilityServiceRepository.GetAsync(leaseId, serviceId, ct)
                ?? await AttachAsync(leaseId, service, ct);

            request.UnitPriceOverride.ApplyIfSet(v => leaseUtilityService.UnitPriceOverride = v);
            request.IsActive.ApplyIfSet(v => leaseUtilityService.IsActive = v);

            if (leaseUtilityService.EffectiveUnitPrice is null)
            {
                throw new AppValidationException(
                    "UnitPriceOverride is required because the utility service has no default unit price");
            }

            return leaseUtilityService;
        }, cancellationToken);

        logger.LogInformation(
            "Lease utility service upserted (lease {LeaseId}, service {UtilityServiceId})", leaseId, serviceId);

        return result.Adapt<LeaseUtilityServiceResponse>();
    }

    public async Task RemoveAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var lease = await leaseRepository.GetByIdForUpdateAsync(leaseId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(leaseId));

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Update,
                NotFoundMessages.Lease(leaseId),
                ct);

            if (!lease.IsEditable)
            {
                throw new AppConflictException(
                    "Utility services can only be removed from draft leases — deactivate the service instead");
            }

            var leaseUtilityService = await leaseUtilityServiceRepository.GetAsync(leaseId, serviceId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.LeaseUtilityService(serviceId));

            leaseUtilityServiceRepository.Remove(leaseUtilityService);
        }, cancellationToken);

        logger.LogInformation(
            "Lease utility service removed (lease {LeaseId}, service {UtilityServiceId})", leaseId, serviceId);
    }

    private async Task<LeaseUtilityService> AttachAsync(Guid leaseId, UtilityService service, CancellationToken cancellationToken)
    {
        if (!service.IsActive)
        {
            throw new AppValidationException("An inactive utility service cannot be attached to a lease");
        }

        var attached = new LeaseUtilityService
        {
            LeaseId = leaseId,
            ServiceId = service.Id,
            Service = service,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        await leaseUtilityServiceRepository.AddAsync(attached, cancellationToken);

        return attached;
    }
}
