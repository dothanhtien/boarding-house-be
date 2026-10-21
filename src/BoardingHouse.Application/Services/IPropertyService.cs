using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.Services;

public interface IPropertyService
{
    Task<PagedResult<PropertyResponse>> GetAllAsync(PropertyListQuery query, CancellationToken cancellationToken = default);
    Task<PropertyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PropertyResponse> CreateAsync(CreatePropertyRequest request, CancellationToken cancellationToken = default);
    Task<PropertyResponse> UpdateAsync(Guid id, UpdatePropertyRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
