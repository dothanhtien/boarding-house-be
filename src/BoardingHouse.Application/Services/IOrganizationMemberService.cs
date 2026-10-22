using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Services;

public interface IOrganizationMemberService
{
    Task<PagedResult<OrganizationMemberResponse>> GetByOrganizationIdAsync(
        Guid organizationId,
        OrganizationMemberListQuery query,
        CancellationToken cancellationToken = default);

    Task<OrganizationMemberResponse> AddAsync(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken = default);

    Task<OrganizationMemberResponse> UpdateRoleAsync(
        Guid organizationId,
        Guid memberId,
        UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken = default);
}
