using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;

namespace BoardingHouse.Api.Services;

public class OrganizationService(
    IOrganizationRepository organizationRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    AppDbContext context,
    ICurrentUserAccessor currentUserAccessor,
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
        var organization = await organizationRepository.GetByIdWithMembersAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Organization '{id}' not found");

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var owner = await userRepository.GetByIdAsync(request.OwnerId, cancellationToken)
            ?? throw new AppNotFoundException($"User '{request.OwnerId}' not found");

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
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization created ({OrganizationId}) with owner ({OwnerId})", organization.Id, request.OwnerId);

        organizationMember.User = owner;
        organizationMember.Role = ownerRole;

        return organization.Adapt<OrganizationResponse>() with { Members = [organizationMember.Adapt<OrganizationMemberResponse>()] };
    }

    public async Task<OrganizationResponse> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Organization '{id}' not found");

        request.Adapt(organization, OrganizationMappingConfig.UpdateConfig);

        if (request.OwnerId is not null)
        {
            await SetOwnerAsync(id, request.OwnerId.Value, request.KeepPreviousOwnerAsStaff, cancellationToken);
        }

        organizationRepository.Update(organization);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization updated ({OrganizationId})", organization.Id);

        var updated = await organizationRepository.GetByIdWithMembersAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Organization '{id}' not found");

        return updated.Adapt<OrganizationResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Organization '{id}' not found");

        organizationRepository.SoftDelete(organization);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization soft-deleted ({OrganizationId})", organization.Id);
    }

    private async Task<Role> GetOrganizationRoleAsync(string slug, CancellationToken cancellationToken) =>
        await roleRepository.GetBySlugAsync(slug, cancellationToken)
            ?? throw new AppInternalException($"Role '{slug}' not found - has RbacSeeder run?");

    private async Task SetOwnerAsync(Guid organizationId, Guid ownerId, bool keepPreviousOwnerAsStaff, CancellationToken cancellationToken)
    {
        _ = await userRepository.GetByIdAsync(ownerId, cancellationToken)
            ?? throw new AppNotFoundException($"User '{ownerId}' not found");

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
            return;
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
    }
}
