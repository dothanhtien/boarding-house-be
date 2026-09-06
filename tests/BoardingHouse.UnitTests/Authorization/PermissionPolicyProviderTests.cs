using BoardingHouse.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BoardingHouse.UnitTests.Authorization;

public class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateSut() =>
        new(Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task GetPolicyAsync_ResourceActionPolicyName_ReturnsPolicyWithPermissionRequirement()
    {
        var policy = await CreateSut().GetPolicyAsync("user:read");

        Assert.NotNull(policy);
        var requirement = Assert.IsType<PermissionRequirement>(Assert.Single(policy!.Requirements));
        Assert.Equal("user", requirement.Resource);
        Assert.Equal("read", requirement.Action);
    }

    [Theory]
    [InlineData("no-colon")]
    [InlineData(":read")]
    [InlineData("user:")]
    [InlineData("")]
    public async Task GetPolicyAsync_NotAResourceActionPolicyName_FallsBackToDefaultProvider(string policyName)
    {
        var policy = await CreateSut().GetPolicyAsync(policyName);

        Assert.Null(policy);
    }

    [Fact]
    public async Task GetPolicyAsync_ValueWithExtraColon_SplitsOnlyOnFirstColon()
    {
        var policy = await CreateSut().GetPolicyAsync("user:read:extra");

        var requirement = Assert.IsType<PermissionRequirement>(Assert.Single(policy!.Requirements));
        Assert.Equal("user", requirement.Resource);
        Assert.Equal("read:extra", requirement.Action);
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_ReturnsAuthenticatedUserPolicy()
    {
        var policy = await CreateSut().GetDefaultPolicyAsync();

        Assert.NotNull(policy);
    }

    [Fact]
    public async Task GetFallbackPolicyAsync_NoFallbackConfigured_ReturnsNull()
    {
        var policy = await CreateSut().GetFallbackPolicyAsync();

        Assert.Null(policy);
    }
}
