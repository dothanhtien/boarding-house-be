using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.UnitTests.Common;

public class InvoiceIssueKeysTests
{
    [Theory]
    [InlineData(InvoiceIssueCode.MissingReading, "missingReading")]
    [InlineData(InvoiceIssueCode.AlreadyInvoiced, "alreadyInvoiced")]
    [InlineData(InvoiceIssueCode.LeaseNotBillable, "leaseNotBillable")]
    public void Of_ReturnsTheCodeInCamelCase(InvoiceIssueCode code, string expected)
    {
        Assert.Equal(expected, InvoiceIssueKeys.Of(code));
    }

    [Fact]
    public void Failed_IsNotTheKeyOfAnyIssueCode()
    {
        Assert.DoesNotContain(InvoiceIssueKeys.Failed, Enum.GetValues<InvoiceIssueCode>().Select(InvoiceIssueKeys.Of));
    }
}
