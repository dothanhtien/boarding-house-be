using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardingHouse.UnitTests.Persistence;

public class AuditableEntitySaveChangesInterceptorTests
{
    private static AppDbContext CreateContext(User? currentUser = null, ILogger<AuditableEntitySaveChangesInterceptor>? logger = null)
    {
        var currentUserAccessor = new CurrentUserAccessor { User = currentUser };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                currentUserAccessor,
                logger ?? NullLogger<AuditableEntitySaveChangesInterceptor>.Instance))
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_NewEntity_SetsCreatedAt()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        Assert.True(user.CreatedAt > DateTimeOffset.MinValue);
        Assert.Null(user.UpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_SetsUpdatedAt_KeepsCreatedAt()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var createdAt = user.CreatedAt;

        user.FullName = "Updated Name";
        context.Users.Update(user);
        await context.SaveChangesAsync();

        Assert.Equal(createdAt, user.CreatedAt);
        Assert.NotNull(user.UpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_WithCurrentUser_SetsUpdatedBy()
    {
        var actor = new User { Email = "actor@test.com", PasswordHash = "hashed-password", FullName = "Actor", CreatedBy = SentinelActors.System };
        using var context = CreateContext(actor);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.FullName = "Updated Name";
        context.Users.Update(user);
        await context.SaveChangesAsync();

        Assert.Equal(actor.Id, user.UpdatedBy);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_WithoutCurrentUser_LeavesUpdatedByNull()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.FullName = "Updated Name";
        context.Users.Update(user);
        await context.SaveChangesAsync();

        Assert.Null(user.UpdatedBy);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_WithExplicitUpdatedBy_DoesNotOverwrite()
    {
        var actor = new User { Email = "actor@test.com", PasswordHash = "hashed-password", FullName = "Actor", CreatedBy = SentinelActors.System };
        using var context = CreateContext(actor);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var explicitActorId = Guid.NewGuid();
        user.FullName = "Updated Name";
        user.UpdatedBy = explicitActorId;
        context.Users.Update(user);
        await context.SaveChangesAsync();

        Assert.Equal(explicitActorId, user.UpdatedBy);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_WithoutCurrentUser_LogsWarning()
    {
        var logger = new Mock<ILogger<AuditableEntitySaveChangesInterceptor>>();
        using var context = CreateContext(logger: logger.Object);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.FullName = "Updated Name";
        context.Users.Update(user);
        await context.SaveChangesAsync();

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_WithCurrentUser_DoesNotLogWarning()
    {
        var actor = new User { Email = "actor@test.com", PasswordHash = "hashed-password", FullName = "Actor", CreatedBy = SentinelActors.System };
        var logger = new Mock<ILogger<AuditableEntitySaveChangesInterceptor>>();
        using var context = CreateContext(actor, logger.Object);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.FullName = "Updated Name";
        context.Users.Update(user);
        await context.SaveChangesAsync();

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveChangesAsync_DeletedEntity_WithCurrentUser_SetsDeletedAtAndDeletedBy()
    {
        var actor = new User { Email = "actor@test.com", PasswordHash = "hashed-password", FullName = "Actor", CreatedBy = SentinelActors.System };
        using var context = CreateContext(actor);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        Assert.NotNull(user.DeletedAt);
        Assert.Equal(actor.Id, user.DeletedBy);
    }

    [Fact]
    public async Task SaveChangesAsync_DeletedEntity_WithoutCurrentUser_LeavesDeletedByNull()
    {
        using var context = CreateContext();
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        Assert.NotNull(user.DeletedAt);
        Assert.Null(user.DeletedBy);
    }

    [Fact]
    public async Task SaveChangesAsync_DeletedEntity_WithExplicitDeletedBy_DoesNotOverwrite()
    {
        var actor = new User { Email = "actor@test.com", PasswordHash = "hashed-password", FullName = "Actor", CreatedBy = SentinelActors.System };
        using var context = CreateContext(actor);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var explicitActorId = Guid.NewGuid();
        user.DeletedBy = explicitActorId;
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        Assert.Equal(explicitActorId, user.DeletedBy);
    }

    [Fact]
    public async Task SaveChangesAsync_DeletedEntity_WithoutCurrentUser_LogsWarning()
    {
        var logger = new Mock<ILogger<AuditableEntitySaveChangesInterceptor>>();
        using var context = CreateContext(logger: logger.Object);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveChangesAsync_DeletedEntity_WithCurrentUser_DoesNotLogWarning()
    {
        var actor = new User { Email = "actor@test.com", PasswordHash = "hashed-password", FullName = "Actor", CreatedBy = SentinelActors.System };
        var logger = new Mock<ILogger<AuditableEntitySaveChangesInterceptor>>();
        using var context = CreateContext(actor, logger.Object);
        var user = new User { Email = "user@test.com", PasswordHash = "hashed-password", FullName = "Test User", CreatedBy = SentinelActors.System };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
