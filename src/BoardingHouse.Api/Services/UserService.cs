using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BoardingHouse.Api.Services;

public class UserService(IUserRepository userRepository, AppDbContext context) : IUserService
{
    public async Task<List<UserResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await context.Users.ToListAsync(cancellationToken);
        return users.Adapt<List<UserResponse>>();
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundAppException($"User '{id}' not found");

        return user.Adapt<UserResponse>();
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, request.Phone, cancellationToken))
        {
            throw new ConflictAppException("Email or phone already in use");
        }

        var user = new User
        {
            Email = email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName
        };

        try
        {
            await userRepository.AddAsync(user, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictAppException("Email or phone already in use");
        }

        return user.Adapt<UserResponse>();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"User '{id}' not found");

        user.Phone = request.Phone;
        user.FullName = request.FullName;
        user.IsActive = request.IsActive;

        userRepository.Update(user);
        await context.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"User '{id}' not found");

        userRepository.SoftDelete(user);
        await context.SaveChangesAsync(cancellationToken);
    }
}
