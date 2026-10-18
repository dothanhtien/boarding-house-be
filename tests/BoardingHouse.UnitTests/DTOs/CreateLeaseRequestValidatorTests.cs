using FluentValidation.TestHelper;
using BoardingHouse.Api.DTOs.Leases;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateLeaseRequestValidatorTests
{
    private readonly CreateLeaseRequestValidator _validator = new();

    private static readonly DateOnly StartDate = new(2026, 11, 1);

    private static CreateLeaseRequest ValidRequest() => new()
    {
        RoomId = Guid.NewGuid(),
        StartDate = StartDate
    };

    private static CreateLeaseTenantRequest Tenant(bool isRepresentative = false) => new()
    {
        UserId = Guid.NewGuid(),
        IsRepresentative = isRepresentative
    };

    [Fact]
    public void Validate_MinimalRequest_IsValid()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyRoomId_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { RoomId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.RoomId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_EndDateNotAfterStartDate_HasError(int offsetDays)
    {
        var result = _validator.TestValidate(ValidRequest() with { EndDate = StartDate.AddDays(offsetDays) });

        result.ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void Validate_EndDateAfterStartDate_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { EndDate = StartDate.AddMonths(6) });

        result.ShouldNotHaveValidationErrorFor(x => x.EndDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Validate_BillingDayOutOfRange_HasError(int billingDay)
    {
        var result = _validator.TestValidate(ValidRequest() with { BillingDay = billingDay });

        result.ShouldHaveValidationErrorFor(x => x.BillingDay);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    public void Validate_BillingDayInRange_HasNoError(int billingDay)
    {
        var result = _validator.TestValidate(ValidRequest() with { BillingDay = billingDay });

        result.ShouldNotHaveValidationErrorFor(x => x.BillingDay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MonthlyRentNotPositive_HasError(int monthlyRent)
    {
        var result = _validator.TestValidate(ValidRequest() with { MonthlyRent = monthlyRent });

        result.ShouldHaveValidationErrorFor(x => x.MonthlyRent);
    }

    [Fact]
    public void Validate_DepositAmountZero_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DepositAmount = 0 });

        result.ShouldNotHaveValidationErrorFor(x => x.DepositAmount);
    }

    [Fact]
    public void Validate_DepositAmountNegative_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DepositAmount = -1 });

        result.ShouldHaveValidationErrorFor(x => x.DepositAmount);
    }

    [Fact]
    public void Validate_DepositAmountThreeDecimals_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DepositAmount = 1.005m });

        result.ShouldHaveValidationErrorFor(x => x.DepositAmount);
    }

    [Fact]
    public void Validate_LeaseCodeEmpty_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LeaseCode = "" });

        result.ShouldHaveValidationErrorFor(x => x.LeaseCode);
    }

    [Fact]
    public void Validate_LeaseCodeTooLong_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { LeaseCode = new string('A', 51) });

        result.ShouldHaveValidationErrorFor(x => x.LeaseCode);
    }

    [Fact]
    public void Validate_DuplicateTenantUser_HasError()
    {
        var tenant = Tenant();

        var result = _validator.TestValidate(ValidRequest() with { Tenants = [tenant, tenant with { IsRepresentative = true }] });

        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void Validate_TwoRepresentatives_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Tenants = [Tenant(true), Tenant(true)] });

        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void Validate_TooManyTenants_HasError()
    {
        var tenants = Enumerable.Range(0, Lease.MaxTenants + 1).Select(_ => Tenant()).ToList();

        var result = _validator.TestValidate(ValidRequest() with { Tenants = tenants });

        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void Validate_TenantWithEmptyUserId_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Tenants = [Tenant() with { UserId = Guid.Empty }] });

        result.ShouldHaveValidationErrorFor("Tenants[0].UserId");
    }

    [Fact]
    public void Validate_TenantsWithOneRepresentative_IsValid()
    {
        var result = _validator.TestValidate(ValidRequest() with { Tenants = [Tenant(true), Tenant()] });

        result.ShouldNotHaveAnyValidationErrors();
    }
}
