using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Configurations;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Caching;
using BoardingHouse.Api.Services.Storage;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class UserService(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IUserCache userCache,
    IMediaAttachmentService mediaAttachmentService,
    IStorageProvider storageProvider,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<UserService> logger) : IUserService
{
    private static readonly Dictionary<string, Expression<Func<User, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = u => u.CreatedAt,
        ["email"] = u => u.Email,
        ["fullName"] = u => u.FullName,
        ["createdAt"] = u => u.CreatedAt,
        ["isActive"] = u => u.IsActive
    };

    public async Task<PagedResult<UserResponse>> GetAllAsync(UserListQuery query, CancellationToken cancellationToken = default)
    {
        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await userRepository.SearchAsync(query.IsActive, query.Search, query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<UserResponse>
        {
            Items = paged.Items
                .Select(u => u.Entity.Adapt<UserResponse>() with
                {
                    AvatarUrl = u.Media is null ? null : storageProvider.GetUrl(u.Media.StorageKey, u.Media.MimeType)
                })
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (user, avatar) = await userRepository.GetByIdWithAvatarAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"User '{id}' not found");

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = avatar is null ? null : storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType)
        };
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, request.Phone, cancellationToken))
        {
            logger.LogWarning("Create user failed: email or phone already in use ({Email})", email);
            throw new AppConflictException("Email or phone already in use");
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
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Create user failed: email or phone already in use ({Email})", email);
            throw new AppConflictException("Email or phone already in use");
        }

        logger.LogInformation("User created ({UserId}, {Email})", user.Id, email);

        return user.Adapt<UserResponse>();
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var (user, avatar) = await userRepository.GetByIdWithAvatarAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"User '{id}' not found");

        if (request.Email.IsSet)
        {
            var email = request.Email.Value!.Trim().ToLowerInvariant();

            if (email != user.Email &&
                await userRepository.ExistsByEmailExcludingUserAsync(email, user.Id, cancellationToken))
            {
                logger.LogWarning("Update user failed: email already in use ({Email})", email);
                throw new AppConflictException("Email already in use");
            }

            user.Email = email;
        }

        if (request.Phone.IsSet)
        {
            if (request.Phone.Value != user.Phone &&
                request.Phone.Value != string.Empty &&
                request.Phone.Value is not null &&
                await userRepository.ExistsByPhoneExcludingUserAsync(request.Phone.Value, user.Id, cancellationToken))
            {
                logger.LogWarning("Update user failed: phone already in use ({UserId})", id);
                throw new AppConflictException("Phone already in use");
            }

            user.Phone = request.Phone.Value;
        }

        request.FullName.ApplyIfSet(v => user.FullName = v!);
        request.IsActive.ApplyIfSet(v => user.IsActive = v);

        try
        {
            userRepository.Update(user);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Update user failed: email or phone already in use ({UserId})", id);
            throw new AppConflictException("Email or phone already in use");
        }

        await userCache.InvalidateAsync(user.Id, cancellationToken);

        logger.LogInformation("User updated ({UserId})", user.Id);

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = avatar is null ? null : storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType)
        };
    }

    public async Task<UserResponse> UpdateAvatarAsync(
        Guid id, UpdateUserAvatarRequest request, CancellationToken cancellationToken = default)
    {
        var (user, _) = await userRepository.GetByIdWithRolesAndOrganizationsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"User '{id}' not found");

        var change = await mediaAttachmentService.StageReplaceAsync(
            MediaAssetEntityType.UserAvatar,
            user.Id,
            request.File!,
            $"users/{user.Id}",
            organizationId: null,
            cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(MediaAssetConfiguration.EntityTypeEntityIdUniqueIndex))
        {
            await mediaAttachmentService.DiscardAsync(change);
            logger.LogWarning("Update user avatar failed: avatar changed concurrently ({UserId})", user.Id);
            throw new AppConflictException("Avatar was changed by another request, please try again");
        }
        catch
        {
            await mediaAttachmentService.DiscardAsync(change);
            throw;
        }

        await mediaAttachmentService.CompleteAsync(change);

        logger.LogInformation("User avatar updated ({UserId})", user.Id);

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = storageProvider.GetUrl(change.Added!.StorageKey, change.Added.MimeType)
        };
    }

    public async Task DeleteAvatarAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await userRepository.ExistsAsync(id, cancellationToken))
        {
            throw new AppNotFoundException($"User '{id}' not found");
        }

        var change = await mediaAttachmentService.StageRemoveAsync(MediaAssetEntityType.UserAvatar, id, cancellationToken);
        if (change == MediaAttachmentChange.None)
        {
            return;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(MediaAssetConfiguration.EntityTypeEntityIdUniqueIndex))
        {
            await mediaAttachmentService.DiscardAsync(change);
            logger.LogWarning("Remove user avatar failed: avatar changed concurrently ({UserId})", id);
            throw new AppConflictException("Avatar was changed by another request, please try again");
        }
        catch
        {
            await mediaAttachmentService.DiscardAsync(change);
            throw;
        }

        await mediaAttachmentService.CompleteAsync(change);

        logger.LogInformation("User avatar removed ({UserId})", id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException($"User '{id}' not found");

        userRepository.SoftDelete(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await userCache.InvalidateAsync(user.Id, cancellationToken);

        logger.LogInformation("User soft-deleted ({UserId})", user.Id);
    }
}
