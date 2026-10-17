using System.Reflection;
using BoardingHouse.Api.Entities;

namespace BoardingHouse.UnitTests.Entities;

public class TenantProfileTests
{
    private static TenantProfile NewProfile() => new() { UserId = Guid.NewGuid(), CreatedBy = Guid.NewGuid() };

    [Fact]
    public void IsEmpty_NoFieldSet_ReturnsTrue()
    {
        Assert.True(NewProfile().IsEmpty());
    }

    [Fact]
    public void IsEmpty_ChecksEveryProfileField()
    {
        var fields = typeof(TenantProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.Name is not (nameof(TenantProfile.UserId) or nameof(TenantProfile.User)))
            .ToList();

        Assert.NotEmpty(fields);
        foreach (var field in fields)
        {
            var profile = NewProfile();
            field.SetValue(profile, SampleValue(field.PropertyType));

            Assert.False(profile.IsEmpty(), $"TenantProfile.IsEmpty does not check {field.Name}");
        }
    }

    private static object SampleValue(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return "x";
        if (t == typeof(DateOnly)) return new DateOnly(2000, 1, 1);
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0)!;
        throw new NotSupportedException($"Add a sample value for {t.Name}");
    }
}
