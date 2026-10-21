using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;

namespace BoardingHouse.IntegrationTests.Persistence;

public class UnitOfWorkTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Organization NewOrganization(string name) => new()
    {
        Name = name,
        CreatedBy = SentinelActors.System
    };

    // Reads through a fresh scope so the assertion hits the database, not the change tracker of the context under test
    private async Task<int> CountOrganizationsAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Organizations.IgnoreQueryFilters().CountAsync(o => o.Name == name);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_OperationSucceeds_CommitsChanges()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var organization = NewOrganization("uow-commit");
            await context.Organizations.AddAsync(organization, ct);
            return organization;
        });

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(1, await CountOrganizationsAsync("uow-commit"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_OperationThrows_RollsBackAndRethrowsOriginalException()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var exception = await Assert.ThrowsAsync<AppConflictException>(() =>
            unitOfWork.ExecuteInTransactionAsync<Organization>(async ct =>
            {
                await context.Organizations.AddAsync(NewOrganization("uow-rollback"), ct);
                // Flush so the INSERT has actually reached the database before the failure
                await context.SaveChangesAsync(ct);
                throw new AppConflictException("boom");
            }));

        Assert.Equal("boom", exception.Message);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, await CountOrganizationsAsync("uow-rollback"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Nested_JoinsOuterTransactionAndDoesNotCommitOnItsOwn()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await Assert.ThrowsAsync<AppConflictException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var outerTransaction = context.Database.CurrentTransaction;
                Assert.NotNull(outerTransaction);

                await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
                {
                    Assert.Same(outerTransaction, context.Database.CurrentTransaction);
                    await context.Organizations.AddAsync(NewOrganization("uow-nested"), innerCt);
                }, ct);

                // The inner call returned successfully; failing the outer one must still discard its write
                throw new AppConflictException("outer failed");
            }));

        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, await CountOrganizationsAsync("uow-nested"));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NonGenericOverload_CommitsChanges()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Block body with no return value, so overload resolution picks the non-generic overload
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await context.Organizations.AddAsync(NewOrganization("uow-non-generic"), ct);
        });

        Assert.Equal(1, await CountOrganizationsAsync("uow-non-generic"));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsTrackedChanges()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await context.Organizations.AddAsync(NewOrganization("uow-save"));
        var written = await unitOfWork.SaveChangesAsync();

        Assert.Equal(1, written);
        Assert.Equal(1, await CountOrganizationsAsync("uow-save"));
    }

    private static User NewUser(string email) => new()
    {
        Email = email,
        PasswordHash = "hash",
        FullName = "UoW User",
        CreatedBy = SentinelActors.System
    };

    // Every service catch matches on ConstraintName, so the real Postgres index name must come through untouched
    [Fact]
    public async Task SaveChangesAsync_UniqueIndexViolated_ThrowsUniqueConstraintViolationWithIndexName()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await context.Users.AddAsync(NewUser("uow-dup@test.com"));
        await unitOfWork.SaveChangesAsync();

        await context.Users.AddAsync(NewUser("uow-dup@test.com"));
        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => unitOfWork.SaveChangesAsync());

        Assert.Equal(UniqueIndexNames.UserEmail, exception.ConstraintName);
        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_UniqueIndexViolatedOnCommit_ThrowsUniqueConstraintViolationAndRollsBack()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await context.Organizations.AddAsync(NewOrganization("uow-unique-rollback"), ct);
                await context.Users.AddAsync(NewUser("uow-tx-dup@test.com"), ct);
                await context.Users.AddAsync(NewUser("uow-tx-dup@test.com"), ct);
            }));

        Assert.Equal(UniqueIndexNames.UserEmail, exception.ConstraintName);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, await CountOrganizationsAsync("uow-unique-rollback"));
    }

    [Fact]
    public async Task SaveChangesAsync_NonUniqueFailure_ThrowsPlainPersistenceException()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Foreign key violation: the organization does not exist
        await context.Properties.AddAsync(new Property
        {
            OrganizationId = Guid.NewGuid(),
            Name = "uow-orphan",
            CreatedBy = SentinelActors.System
        });

        var exception = await Assert.ThrowsAsync<PersistenceException>(() => unitOfWork.SaveChangesAsync());

        Assert.IsNotType<UniqueConstraintViolationException>(exception);
        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
    }
}
