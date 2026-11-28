using Microsoft.EntityFrameworkCore;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.Infrastructure.Repositories;

public class OrganizationPaymentGatewayRepository(AppDbContext context) : IOrganizationPaymentGatewayRepository
{
    public async Task AddAsync(OrganizationPaymentGateway account, CancellationToken cancellationToken = default) =>
        await context.OrganizationPaymentGateways.AddAsync(account, cancellationToken);

    public Task<OrganizationPaymentGateway?> GetAsync(
        Guid organizationId, PaymentGateway gateway, CancellationToken cancellationToken = default) =>
        context.OrganizationPaymentGateways
            .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.Gateway == gateway, cancellationToken);

    public Task<List<OrganizationPaymentGateway>> ListByOrganizationIdAsync(
        Guid organizationId, CancellationToken cancellationToken = default) =>
        context.OrganizationPaymentGateways
            .AsNoTracking()
            .Where(g => g.OrganizationId == organizationId)
            .OrderBy(g => g.Gateway)
            .ToListAsync(cancellationToken);

    public Task<OrganizationPaymentGatewayCredentialHistory?> GetReplacedCredentialsAsync(
        Guid organizationPaymentGatewayId, int version, CancellationToken cancellationToken = default) =>
        context.OrganizationPaymentGatewayCredentialHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                h => h.OrganizationPaymentGatewayId == organizationPaymentGatewayId && h.Version == version,
                cancellationToken);
}
