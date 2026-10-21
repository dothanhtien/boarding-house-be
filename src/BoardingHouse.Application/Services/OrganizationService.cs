using System.Linq.Expressions;
using Mapster;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Application.Services;

public class OrganizationService(
    IOrganizationRepository organizationRepository,
    IOrganizationMemberRepository organizationMemberRepository,
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

        var organization = new Organization
        {
            Name = request.Name,
            TaxCode = request.TaxCode,
            Phone = request.Phone,
            Email = request.Email,
            Province = request.Province,
            District = request.District,
            Ward = request.Ward,
            Address = request.Address,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        var organizationMember = new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = request.OwnerId,
            RoleId = ownerRole.Id,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        await organizationRepository.AddAsync(organization, cancellationToken);
        await organizationMemberRepository.AddAsync(organizationMember, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await organizationMembershipCache.InvalidateAsync(request.OwnerId, cancellationToken);

        logger.LogInformation("Organization created ({OrganizationId}) with owner ({OwnerId})", organization.Id, request.OwnerId);

        organizationMember.User = owner;
        organizationMember.Role = ownerRole;

        return organization.Adapt<OrganizationResponse>() with { Members = [organizationMember.Adapt<OrganizationMemberResponse>()] };
    }

    public async Task<OrganizationResponse> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        await organizationScopeAccessor.EnsureScopeAsync(id, PermissionResources.Organization, PermissionActions.Update, cancellationToken: cancellationToken);

        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Organization(id));

        request.Adapt(organization, OrganizationMappingConfig.UpdateConfig);

        List<Guid> affectedMemberUserIds = [];
        if (request.OwnerId is not null)
        {
            affectedMemberUserIds = await SetOwnerAsync(id, request.OwnerId.Value, request.KeepPreviousOwnerAsStaff, cancellationToken);
        }

        organizationRepository.Update(organization);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await Task.WhenAll(affectedMemberUserIds.Select(userId => organizationMembershipCache.InvalidateAsync(userId, cancellationToken)));

        logger.LogInformation("Organization updated ({OrganizationId})", organization.Id);

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

    private async Task<List<Guid>> SetOwnerAsync(Guid organizationId, Guid ownerId, bool keepPreviousOwnerAsStaff, CancellationToken cancellationToken)
    {
        _ = await userRepository.GetByIdAsync(ownerId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(ownerId));

        var ownerRole = await GetOrganizationRoleAsync(RoleSlugs.OrganizationAdmin, cancellationToken);

        var membership = await organizationMemberRepository.GetByOrganizationAndUserIdAsync(organizationId, ownerId, cancellationToken);

        if (membership is null)
        {
            await organizationMemberRepository.AddAsync(new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = ownerId,
                RoleId = ownerRole.Id,
                CreatedBy = currentUserAccessor.RequiredUser.Id
            }, cancellationToken);

            logger.LogInformation("Organization owner set ({OrganizationId}, {OwnerId})", organizationId, ownerId);
        }
        else if (membership.RoleId != ownerRole.Id)
        {
            membership.RoleId = ownerRole.Id;
            organizationMemberRepository.Update(membership);

            logger.LogInformation("Organization owner changed ({OrganizationId}, {OwnerId})", organizationId, ownerId);
        }

        var previousOwners = await organizationMemberRepository.GetByOrganizationAndRoleIdAsync(organizationId, ownerRole.Id, cancellationToken);
        if (previousOwners.Count == 0)
        {
            return [ownerId];
        }

        var staffRole = keepPreviousOwnerAsStaff
            ? await GetOrganizationRoleAsync(RoleSlugs.OrganizationStaff, cancellationToken)
            : null;

        foreach (var previousOwner in previousOwners.Where(m => m.UserId != ownerId))
        {
            if (staffRole is not null)
            {
                previousOwner.RoleId = staffRole.Id;
                organizationMemberRepository.Update(previousOwner);

                logger.LogInformation("Organization owner demoted to staff ({OrganizationId}, {UserId})", organizationId, previousOwner.UserId);
            }
            else
            {
                organizationMemberRepository.SoftDelete(previousOwner);

                logger.LogInformation("Organization owner removed ({OrganizationId}, {UserId})", organizationId, previousOwner.UserId);
            }
        }

        return [ownerId, .. previousOwners.Where(m => m.UserId != ownerId).Select(m => m.UserId)];
    }
}
