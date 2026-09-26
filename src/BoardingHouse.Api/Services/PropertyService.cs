using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class PropertyService(
    IPropertyRepository propertyRepository,
    IOrganizationRepository organizationRepository,
    IRoomRepository roomRepository,
    IUtilityServiceRepository utilityServiceRepository,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentOrganizationAccessor currentOrganizationAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<PropertyService> logger) : IPropertyService
{
    private static readonly Dictionary<string, Expression<Func<Property, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = p => p.Name,
        ["name"] = p => p.Name,
        ["createdAt"] = p => p.CreatedAt,
        ["isActive"] = p => p.IsActive
    };

    public async Task<PagedResult<PropertyResponse>> GetAllAsync(PropertyListQuery query, CancellationToken cancellationToken = default)
    {
        var organizationId = query.OrganizationId ?? currentOrganizationAccessor.OrganizationId;

        var scope = await organizationScopeAccessor.GetScopeAsync("property", "read", cancellationToken);
        if (organizationId is not null && !scope.Includes(organizationId.Value))
        {
            throw new AppNotFoundException($"Organization '{organizationId}' not found");
        }

        var allowedOrganizationIds = organizationId is null && !scope.IsUnrestricted ? scope.OrganizationIds : null;

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await propertyRepository.SearchAsync(
            organizationId, allowedOrganizationIds, query.IsActive, query.Search, query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<PropertyResponse>
        {
            Items = paged.Items.Adapt<List<PropertyResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<PropertyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Property '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            property.OrganizationId, "property", "read", $"Property '{id}' not found", cancellationToken);

        return property.Adapt<PropertyResponse>();
    }

    public async Task<PropertyResponse> CreateAsync(CreatePropertyRequest request, CancellationToken cancellationToken = default)
    {
        var organizationId = request.OrganizationId ?? currentOrganizationAccessor.OrganizationId
            ?? throw new AppValidationException("OrganizationId is required — pass it explicitly or set the X-Organization-Id header");

        var organizationExists = await organizationRepository.ExistsAsync(organizationId, cancellationToken);
        if (!organizationExists)
        {
            throw new AppNotFoundException($"Organization '{organizationId}' not found");
        }

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, "property", "create", cancellationToken: cancellationToken);

        var property = new Property
        {
            OrganizationId = organizationId,
            Name = request.Name,
            Description = request.Description,
            Province = request.Province,
            District = request.District,
            Ward = request.Ward,
            Address = request.Address,
            DefaultBillingDay = request.DefaultBillingDay,
            LateFeeType = request.LateFeeType,
            LateFeeValue = request.LateFeeValue,
            LateFeeGraceDays = request.LateFeeGraceDays,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        try
        {
            await propertyRepository.AddAsync(property, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Create property failed: name already in use in organization ({OrganizationId})", organizationId);
            throw new AppConflictException("A property with this name already exists in the organization");
        }

        logger.LogInformation("Property created ({PropertyId})", property.Id);

        return property.Adapt<PropertyResponse>();
    }

    public async Task<PropertyResponse> UpdateAsync(Guid id, UpdatePropertyRequest request, CancellationToken cancellationToken = default)
    {
        var property = await propertyRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"Property '{id}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            property.OrganizationId, "property", "update", $"Property '{id}' not found", cancellationToken);

        request.Adapt(property, PropertyMappingConfig.UpdateConfig);

        try
        {
            propertyRepository.Update(property);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Update property failed: name already in use in organization ({PropertyId})", id);
            throw new AppConflictException("A property with this name already exists in the organization");
        }

        logger.LogInformation("Property updated ({PropertyId})", property.Id);

        return property.Adapt<PropertyResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var property = await propertyRepository.GetByIdForUpdateAsync(id, ct)
                ?? throw new AppNotFoundException($"Property '{id}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                property.OrganizationId, "property", "delete", $"Property '{id}' not found", ct);

            if (await roomRepository.ExistsByPropertyIdAsync(id, ct))
            {
                logger.LogWarning("Delete property failed: property still has rooms ({PropertyId})", id);
                throw new AppConflictException("Cannot delete a property that still has rooms — delete its rooms first");
            }

            // FOR UPDATE serializes with a concurrent UtilityServiceService.UpdateAsync so the soft-delete below
            // can't overwrite an update committed after our read
            var utilityServices = await utilityServiceRepository.ListByPropertyIdAsync(
                id, type: null, isActive: null, forUpdate: true, ct);
            foreach (var utilityService in utilityServices)
            {
                utilityServiceRepository.SoftDelete(utilityService);
            }

            propertyRepository.SoftDelete(property);
        }, cancellationToken);

        logger.LogInformation("Property soft-deleted ({PropertyId})", id);
    }
}
