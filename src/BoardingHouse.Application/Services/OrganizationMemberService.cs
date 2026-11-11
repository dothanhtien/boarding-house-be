using System.Linq.Expressions;
using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services;

public class OrganizationMemberService(
    IOrganizationMemberRepository organizationMemberRepository,
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IOrganizationMembershipCache organizationMembershipCache,
    ILogger<OrganizationMemberService> logger) : IOrganizationMemberService
{
    private static readonly Dictionary<string, Expression<Func<OrganizationMember, object>>> SortableFields =
    new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = m => m.CreatedAt,
        ["createdAt"] = m => m.CreatedAt,
        ["userEmail"] = m => m.User!.Email!,
        ["userFullName"] = m => m.User!.FullName,
        ["roleName"] = m => m.Role!.Name
    };

    public async Task<PagedResult<OrganizationMemberResponse>> GetByOrganizationIdAsync(
        Guid organizationId, OrganizationMemberListQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await organizationMemberRepository.SearchByOrganizationIdAsync(
            organizationId,
            query.Search,
            query,
            sortField,
            query.SortOrder,
            cancellationToken);

        return new PagedResult<OrganizationMemberResponse>
        {
            Items = paged.Items.Adapt<List<OrganizationMemberResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<OrganizationMemberResponse> AddAsync(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (member, user, role) = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var organization = await GetAggregateForUpdateAsync(organizationId, ct);
                var user = await userRepository.GetByIdAsync(request.UserId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.User(request.UserId));
                var role = await GetRoleAsync(request.RoleId, ct);
                var member = organization.AddMember(request.UserId, role, currentUserAccessor.RequiredUser.Id);

                return (member, user, role);
            }, cancellationToken);

            await organizationMembershipCache.InvalidateAsync(request.UserId, cancellationToken);

            logger.LogInformation("Organization member added ({OrganizationId}, {MemberId})", organizationId, member.Id);

            return member.ToResponse(user, role);
        }
        catch (UniqueConstraintViolationException)
        {
            logger.LogWarning("Add organization member failed: user already a member ({OrganizationId}, {UserId})", organizationId, request.UserId);
            throw new AppConflictException("User is already a member of this organization");
        }
    }

    public async Task<OrganizationMemberResponse> UpdateRoleAsync(
        Guid organizationId,
        Guid memberId,
        UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var (member, role) = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var organization = await GetAggregateForUpdateAsync(organizationId, ct);
            var ownerRole = await GetOwnerRoleAsync(ct);
            var role = await GetRoleAsync(request.RoleId, ct);

            return (organization.ChangeMemberRole(memberId, role, ownerRole.Id), role);
        }, cancellationToken);

        await organizationMembershipCache.InvalidateAsync(member.UserId, cancellationToken);

        logger.LogInformation("Organization member role updated ({OrganizationId}, {MemberId})", organizationId, memberId);

        var user = await userRepository.GetByIdAsync(member.UserId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(member.UserId));

        return member.ToResponse(user, role);
    }

    public async Task RemoveAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken = default)
    {
        var member = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var organization = await GetAggregateForUpdateAsync(organizationId, ct);
            var ownerRole = await GetOwnerRoleAsync(ct);

            return organization.RemoveMember(memberId, ownerRole.Id);
        }, cancellationToken);

        await organizationMembershipCache.InvalidateAsync(member.UserId, cancellationToken);

        logger.LogInformation("Organization member removed ({OrganizationId}, {MemberId})", organizationId, memberId);
    }

    private async Task EnsureOrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!await organizationRepository.ExistsAsync(organizationId, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId));
        }
    }

    private async Task<Organization> GetAggregateForUpdateAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await organizationRepository.GetAggregateForUpdateAsync(organizationId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Organization(organizationId));

    private async Task<Role> GetOwnerRoleAsync(CancellationToken cancellationToken) =>
        await roleRepository.GetBySlugAsync(RoleSlugs.OrganizationAdmin, cancellationToken)
            ?? throw new AppInternalException($"Role '{RoleSlugs.OrganizationAdmin}' not found - has RbacSeeder run?");

    private async Task<Role> GetRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        await roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Role(roleId));
}
