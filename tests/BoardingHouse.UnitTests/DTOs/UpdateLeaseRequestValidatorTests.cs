using BoardingHouse.Api.DTOs.Leases;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateLeaseRequestValidatorTests
{
    private readonly UpdateLeaseRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyRequest_IsValid()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_LeaseCodeSetEmpty_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest { LeaseCode = "" });

        result.ShouldHaveValidationErrorFor(x => x.LeaseCode);
    }

    [Fact]
    public void Validate_BillingDaySetNull_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest { BillingDay = null });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_BillingDayOutOfRange_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest { BillingDay = 29 });

        result.ShouldHaveValidationErrorFor(x => x.BillingDay);
    }

    [Fact]
    public void Validate_MonthlyRentZero_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest { MonthlyRent = 0m });

        result.ShouldHaveValidationErrorFor(x => x.MonthlyRent);
    }

    [Fact]
    public void Validate_DepositAmountZero_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest { DepositAmount = 0m });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_TenantsSetNull_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest { Tenants = null });

        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void Validate_DuplicateTenantId_HasError()
    {
        var id = Guid.NewGuid();

        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { Id = id }, new() { Id = id, IsDeleted = true } }
        });

        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void Validate_SameUserAddedTwice_HasError()
    {
        var userId = Guid.NewGuid();

        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { UserId = userId }, new() { UserId = userId } }
        });

        result.ShouldHaveValidationErrorFor(x => x.Tenants);
    }

    [Fact]
    public void Validate_NewTenantWithoutUserId_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { IsRepresentative = true } }
        });

        result.ShouldHaveValidationErrorFor("Tenants[0].UserId");
    }

    [Fact]
    public void Validate_ExistingTenantWithUserId_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid() } }
        });

        result.ShouldHaveValidationErrorFor("Tenants[0].UserId");
    }

    [Fact]
    public void Validate_IsDeletedWithoutId_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { UserId = Guid.NewGuid(), IsDeleted = true } }
        });

        result.ShouldHaveValidationErrorFor("Tenants[0].IsDeleted");
    }

    [Fact]
    public void Validate_ValidTenantChanges_IsValid()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest>
            {
                new() { UserId = Guid.NewGuid(), IsRepresentative = true },
                new() { Id = Guid.NewGuid(), JoinedDate = new DateOnly(2026, 11, 5) },
                new() { Id = Guid.NewGuid(), IsDeleted = true }
            }
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NewTenantWithLeftDate_HasError()
    {
        var result = _validator.TestValidate(new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest>
            {
                new() { UserId = Guid.NewGuid(), LeftDate = new DateOnly(2026, 11, 5) }
            }
        });

        result.ShouldHaveValidationErrorFor("Tenants[0].LeftDate");
    }
}
