using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class OrganizationService(
    IOrganizationRepository organizationRepository,
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
        var organizations = context.Organizations.AsQueryable();

        if (query.IsActive is not null)
        {
            organizations = organizations.Where(o => o.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = LikePattern.Contains(query.Search.Trim());
            organizations = organizations.Where(o => EF.Functions.ILike(o.Name, pattern, LikePattern.EscapeCharacter));
        }

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await organizations.ToPagedResultAsync(query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<OrganizationResponse>
        {
            Items = paged.Items.Adapt<List<OrganizationResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<OrganizationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"Organization '{id}' not found");

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
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

        await organizationRepository.AddAsync(organization, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization created ({OrganizationId})", organization.Id);

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task<OrganizationResponse> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"Organization '{id}' not found");

        request.Adapt(organization, OrganizationMappingConfig.UpdateConfig);

        organizationRepository.Update(organization);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization updated ({OrganizationId})", organization.Id);

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"Organization '{id}' not found");

        organizationRepository.SoftDelete(organization);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization soft-deleted ({OrganizationId})", organization.Id);
    }
}
