using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.Entities;

public class UserTests
{
    [Fact]
    public void Register_NormalizesEmailAndPhone()
    {
        var user = User.Register(" User@Test.COM ", "+84 912.345.678", "hash", "Nguyen Van A", SentinelActors.System);

        Assert.Equal(("user@test.com", "0912345678"), (user.Email, user.Phone));
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Register_WithoutEmailAndPhone_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => User.Register(" ", null, "hash", "A", SentinelActors.System));

        Assert.Equal("A user must have an email or a phone", ex.Message);
    }

    [Fact]
    public void Register_BlankFullName_Throws()
    {
        Assert.Throws<DomainException>(() => User.Register("a@test.com", null, "hash", " ", SentinelActors.System));
    }

    [Fact]
    public void ChangePhone_ClearingTheOnlyContact_Throws()
    {
        var user = User.Register(null, "0912345678", "hash", "Tenant", SentinelActors.System);

        Assert.Throws<DomainException>(() => user.ChangePhone(null));
        Assert.Equal("0912345678", user.Phone);
    }

    [Fact]
    public void ChangePhone_ClearingWhenEmailExists_Clears()
    {
        var user = User.Register("a@test.com", "0912345678", "hash", "A", SentinelActors.System);

        user.ChangePhone(" ");

        Assert.Null(user.Phone);
    }

    [Fact]
    public void RecordLogin_SetsLastLoginAt()
    {
        var user = User.Register("a@test.com", null, "hash", "A", SentinelActors.System);
        var now = DateTimeOffset.UtcNow;

        user.RecordLogin(now);

        Assert.Equal(now, user.LastLoginAt);
    }
}
