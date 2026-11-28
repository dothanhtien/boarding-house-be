using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Repositories;

public interface IOrganizationPaymentGatewayRepository
{
    Task AddAsync(OrganizationPaymentGateway account, CancellationToken cancellationToken = default);
    Task<OrganizationPaymentGateway?> GetAsync(
        Guid organizationId, PaymentGateway gateway, CancellationToken cancellationToken = default);
    Task<List<OrganizationPaymentGateway>> ListByOrganizationIdAsync(
        Guid organizationId, CancellationToken cancellationToken = default);
}
