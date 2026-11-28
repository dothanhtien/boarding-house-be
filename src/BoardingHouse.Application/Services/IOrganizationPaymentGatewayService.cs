using BoardingHouse.Application.DTOs.PaymentGateways;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Services;

public interface IOrganizationPaymentGatewayService
{
    Task<List<OrganizationPaymentGatewayResponse>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationPaymentGatewayResponse> UpsertAsync(
        Guid organizationId,
        PaymentGateway gateway,
        UpsertOrganizationPaymentGatewayRequest request,
        CancellationToken cancellationToken = default);
}
