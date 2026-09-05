using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Caching;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class UserService(
    IUserRepository userRepository,
    AppDbContext context,
    IUserCache userCache,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<UserService> logger) : IUserService
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
            logger.LogWarning("Create user failed: email or phone already in use ({Email})", email);
            throw new ConflictAppException("Email or phone already in use");
        }

        var user = new User
        {
            Email = email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            FullName = request.FullName,
            CreatedBy = currentUserAccessor.User?.Id ?? SentinelActors.System
        };

        try
        {
            await userRepository.AddAsync(user, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Create user failed: email or phone already in use ({Email})", email);
            throw new ConflictAppException("Email or phone already in use");
        }

        logger.LogInformation("User created ({UserId}, {Email})", user.Id, email);

        return user.Adapt<UserResponse>();
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"User '{id}' not found");

        user.Phone = request.Phone;
        user.FullName = request.FullName;
        user.IsActive = request.IsActive;

        userRepository.Update(user);
        await context.SaveChangesAsync(cancellationToken);
        await userCache.InvalidateAsync(user.Id, cancellationToken);

        logger.LogInformation("User updated ({UserId})", user.Id);

        return user.Adapt<UserResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"User '{id}' not found");

        userRepository.SoftDelete(user);
        await context.SaveChangesAsync(cancellationToken);
        await userCache.InvalidateAsync(user.Id, cancellationToken);

        logger.LogInformation("User soft-deleted ({UserId})", user.Id);
    }
}
