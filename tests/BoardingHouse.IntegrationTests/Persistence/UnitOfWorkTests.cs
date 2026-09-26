using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
