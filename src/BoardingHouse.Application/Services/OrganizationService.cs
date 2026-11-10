using System.Linq.Expressions;
using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services;

public class OrganizationService(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IOrganizationScopeAccessor organizationScopeAccessor,
    IOrganizationMembershipCache organizationMembershipCache,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    private static readonly Dictionary<string, Expression<Func<Organization, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = o => o.Name,
        ["name"] = o => o.Name,
        ["createdAt"] = o => o.CreatedAt,
        ["isActive"] = o => o.IsActive
    };

    public async Task<PagedResult<OrganizationResponse>> GetAllAsync(OrganizationListQuery query, CancellationToken cancellationToken = default)
    {
        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await organizationRepository.SearchAsync(query.IsActive, query.Search, query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<OrganizationResponse>
        {
            Items = paged.Items.Adapt<List<OrganizationResponse>>().Select(o => o with { Members = null }).ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<OrganizationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(id, PermissionResources.Organization, PermissionActions.Read, cancellationToken: cancellationToken);

        var organization = await organizationRepository.GetByIdWithMembersAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Organization(id));

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var owner = await userRepository.GetByIdAsync(request.OwnerId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(request.OwnerId));

        var ownerRole = await GetOrganizationRoleAsync(RoleSlugs.OrganizationAdmin, cancellationToken);

        var organization = Organization.Create(
            new OrganizationDetails(
                request.Name,
                request.TaxCode,
                request.Phone,
                request.Email,
                request.Province,
                request.District,
                request.Ward,
                request.Address,
                IsActive: true),
            request.OwnerId,
            ownerRole,
            currentUserAccessor.RequiredUser.Id);

        await organizationRepository.AddAsync(organization, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await organizationMembershipCache.InvalidateAsync(request.OwnerId, cancellationToken);

        logger.LogInformation("Organization created ({OrganizationId}) with owner ({OwnerId})", organization.Id, request.OwnerId);

        return organization.Adapt<OrganizationResponse>() with
        {
            Members = [organization.Members.Single().ToResponse(owner, ownerRole)]
        };
    }

    public async Task<OrganizationResponse> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(id, PermissionResources.Organization, PermissionActions.Update, cancellationToken: cancellationToken);

        var affectedMemberUserIds = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // FOR UPDATE + every member: two concurrent ownership transfers cannot both see the old owner
            var organization = await organizationRepository.GetAggregateForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.Organization(id));

            var current = organization.Details;
            organization.Update(new OrganizationDetails(
                request.Name.OrKeep(current.Name)!,
                request.TaxCode.OrKeep(current.TaxCode),
                request.Phone.OrKeep(current.Phone),
                request.Email.OrKeep(current.Email),
                request.Province.OrKeep(current.Province),
                request.District.OrKeep(current.District),
                request.Ward.OrKeep(current.Ward),
                request.Address.OrKeep(current.Address),
                request.IsActive.OrKeep(current.IsActive)));

            if (request.OwnerId is not { } ownerId)
            {
                return [];
            }

            _ = await userRepository.GetByIdAsync(ownerId, ct)
                ?? throw new AppNotFoundException(NotFoundMessages.User(ownerId));

            var ownerRole = await GetOrganizationRoleAsync(RoleSlugs.OrganizationAdmin, ct);
            var staffRole = request.KeepPreviousOwnerAsStaff
                ? await GetOrganizationRoleAsync(RoleSlugs.OrganizationStaff, ct)
                : null;

            var affected = organization.TransferOwnership(ownerId, ownerRole, staffRole, currentUserAccessor.RequiredUser.Id);

            logger.LogInformation(
                "Organization ownership transferred ({OrganizationId}, {OwnerId}, previous owners kept as staff: {KeepAsStaff})",
                id, ownerId, staffRole is not null);

            return affected;
        }, cancellationToken);

        await Task.WhenAll(affectedMemberUserIds.Select(userId => organizationMembershipCache.InvalidateAsync(userId, cancellationToken)));

        logger.LogInformation("Organization updated ({OrganizationId})", id);

        var updated = await organizationRepository.GetByIdWithMembersAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Organization(id));

        return updated.Adapt<OrganizationResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Organization(id));

        organizationRepository.SoftDelete(organization);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization soft-deleted ({OrganizationId})", organization.Id);
    }

    private async Task<Role> GetOrganizationRoleAsync(string slug, CancellationToken cancellationToken) =>
        await roleRepository.GetBySlugAsync(slug, cancellationToken)
            ?? throw new AppInternalException($"Role '{slug}' not found - has RbacSeeder run?");
}
