using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Properties;

namespace BoardingHouse.Api.Services;

public interface IPropertyService
{
    Task<PagedResult<PropertyResponse>> GetAllAsync(PropertyListQuery query, CancellationToken cancellationToken = default);
    Task<PropertyResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PropertyResponse> CreateAsync(CreatePropertyRequest request, CancellationToken cancellationToken = default);
    Task<PropertyResponse> UpdateAsync(Guid id, UpdatePropertyRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
