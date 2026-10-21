using System.Reflection;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Domain.Common;
using BoardingHouse.Infrastructure.Persistence;

namespace BoardingHouse.UnitTests;

/// <summary>
/// Guards the Clean Architecture layering: dependencies point inward (Api → Application, Infrastructure;
/// Infrastructure → Application; Application → Domain; Domain → nothing). Checks the compiled assembly references,
/// so a stray <c>using</c> of a forbidden namespace fails here even when the package arrives transitively.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Entity).Assembly;
    private static readonly Assembly Application = typeof(UniqueIndexNames).Assembly;
    private static readonly Assembly Infrastructure = typeof(UnitOfWork).Assembly;

    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    private static bool IsFrameworkAssembly(string name) =>
        name is "netstandard" or "mscorlib" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal);

    [Fact]
    public void Domain_ReferencesOnlyTheBaseClassLibrary()
    {
        var offending = ReferencedAssemblyNames(Domain).Where(name => !IsFrameworkAssembly(name));

        Assert.Empty(offending);
    }

    [Theory]
    [InlineData("BoardingHouse.Api")]
    [InlineData("BoardingHouse.Infrastructure")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    [InlineData("StackExchange.Redis")]
    [InlineData("CloudinaryDotNet")]
    public void Application_DoesNotReferenceOuterLayersOrInfrastructurePackages(string forbiddenPrefix)
    {
        var offending = ReferencedAssemblyNames(Application)
            .Where(name => name == forbiddenPrefix || name.StartsWith(forbiddenPrefix + ".", StringComparison.Ordinal));

        Assert.Empty(offending);
    }

    [Fact]
    public void Application_ReferencesDomain()
    {
        Assert.Contains(Domain.GetName().Name, ReferencedAssemblyNames(Application));
    }

    [Theory]
    [InlineData("BoardingHouse.Api")]
    [InlineData("Microsoft.AspNetCore")]
    public void Infrastructure_DoesNotReferenceTheApi(string forbiddenPrefix)
    {
        var offending = ReferencedAssemblyNames(Infrastructure)
            .Where(name => name == forbiddenPrefix || name.StartsWith(forbiddenPrefix + ".", StringComparison.Ordinal));

        Assert.Empty(offending);
    }
}
