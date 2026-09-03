using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailOrPhoneAsync(string email, string? phone, CancellationToken cancellationToken = default);
}
