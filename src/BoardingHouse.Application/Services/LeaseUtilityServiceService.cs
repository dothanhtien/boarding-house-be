using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services;

public class LeaseUtilityServiceService(
    ILeaseRepository leaseRepository,
    IUtilityServiceRepository utilityServiceRepository,
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

        var services = await leaseRepository.ListUtilityServicesAsync(leaseId, cancellationToken);

        return services.Adapt<List<LeaseUtilityServiceResponse>>();
    }

    public async Task<LeaseUtilityServiceResponse> UpsertAsync(
        Guid leaseId, Guid serviceId, UpsertLeaseUtilityServiceRequest request, CancellationToken cancellationToken = default)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // FOR UPDATE serializes concurrent upserts of the same (lease, service) pair and status transitions
            var lease = await leaseRepository.GetAggregateForUpdateAsync(leaseId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(leaseId));

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Update,
                NotFoundMessages.Lease(leaseId),
                ct);

            var service = await utilityServiceRepository.GetByIdForShareAsync(serviceId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.UtilityService(serviceId));

            return lease.UpsertUtilityService(
                service,
                new LeaseUtilityServiceChange
                {
                    UnitPriceOverride = request.UnitPriceOverride.ToChange(),
                    Quantity = request.Quantity.IsSet ? request.Quantity.Value : null,
                    IsActive = request.IsActive.IsSet ? request.IsActive.Value : null
                },
                currentUserAccessor.RequiredUser.Id);
        }, cancellationToken);

        logger.LogInformation(
            "Lease utility service upserted (lease {LeaseId}, service {UtilityServiceId})", leaseId, serviceId);

        return result.Adapt<LeaseUtilityServiceResponse>();
    }

    public async Task RemoveAsync(Guid leaseId, Guid serviceId, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var lease = await leaseRepository.GetAggregateForUpdateAsync(leaseId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Lease(leaseId));

            await organizationScopeAccessor.EnsureScopeAsync(
                lease.OrganizationId,
                PermissionResources.Lease,
                PermissionActions.Update,
                NotFoundMessages.Lease(leaseId),
                ct);

            lease.RemoveUtilityService(serviceId);
        }, cancellationToken);

        logger.LogInformation(
            "Lease utility service removed (lease {LeaseId}, service {UtilityServiceId})", leaseId, serviceId);
    }
}
