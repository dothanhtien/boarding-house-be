using System.Text.Json;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Common;

public static class InvoiceIssueKeys
{
    public const string Failed = "failed";

    public static string Of(InvoiceIssueCode code) => JsonNamingPolicy.CamelCase.ConvertName(code.ToString());
}
