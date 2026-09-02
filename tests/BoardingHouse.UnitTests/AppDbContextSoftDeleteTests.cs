using BoardingHouse.Api.Common;
using BoardingHouse.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.UnitTests;

public class SampleEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}

public class AnotherSampleEntity : BaseEntity
{
    public int Quantity { get; set; }
}

public class AppDbContextSoftDeleteTests
{
    private sealed class TestDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SampleEntity>();
            modelBuilder.Entity<AnotherSampleEntity>();
            base.OnModelCreating(modelBuilder);
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public void Query_Excludes_SoftDeleted_Entity()
    {
        using var context = CreateContext();

        var active = new SampleEntity { Name = "Active" };
        var deleted = new SampleEntity { Name = "Deleted", DeletedAt = DateTimeOffset.UtcNow };

        context.Set<SampleEntity>().AddRange(active, deleted);
        context.SaveChanges();

        var result = context.Set<SampleEntity>().ToList();

        Assert.Single(result);
        Assert.Equal(active.Id, result[0].Id);
    }

    [Fact]
    public void IgnoreQueryFilters_Includes_SoftDeleted_Entity()
    {
        using var context = CreateContext();

        var active = new SampleEntity { Name = "Active" };
        var deleted = new SampleEntity { Name = "Deleted", DeletedAt = DateTimeOffset.UtcNow };

        context.Set<SampleEntity>().AddRange(active, deleted);
        context.SaveChanges();

        var result = context.Set<SampleEntity>().IgnoreQueryFilters().ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Id == active.Id);
        Assert.Contains(result, e => e.Id == deleted.Id);
    }

    [Fact]
    public void Filter_Applies_Independently_To_Every_BaseEntity_Subclass()
    {
        using var context = CreateContext();

        var activeSample = new SampleEntity { Name = "Active" };
        var deletedSample = new SampleEntity { Name = "Deleted", DeletedAt = DateTimeOffset.UtcNow };
        var activeAnother = new AnotherSampleEntity { Quantity = 1 };
        var deletedAnother = new AnotherSampleEntity { Quantity = 2, DeletedAt = DateTimeOffset.UtcNow };

        context.Set<SampleEntity>().AddRange(activeSample, deletedSample);
        context.Set<AnotherSampleEntity>().AddRange(activeAnother, deletedAnother);
        context.SaveChanges();

        var sampleResult = context.Set<SampleEntity>().ToList();
        var anotherResult = context.Set<AnotherSampleEntity>().ToList();

        Assert.Single(sampleResult);
        Assert.Equal(activeSample.Id, sampleResult[0].Id);

        Assert.Single(anotherResult);
        Assert.Equal(activeAnother.Id, anotherResult[0].Id);
    }
}
