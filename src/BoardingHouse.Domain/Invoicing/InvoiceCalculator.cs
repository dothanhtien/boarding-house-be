using System.Globalization;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Invoicing;

public static class InvoiceCalculator
{
    public const string SupportedCurrency = "VND";

    public static InvoiceCalculation Calculate(InvoiceCalculationInput input)
    {
        var lease = input.Lease;
        var month = input.BillingMonth;

        var errors = new List<InvoiceIssue>();
        var warnings = new List<InvoiceIssue>();
        var lines = new List<InvoiceLine>();

        var currency = input.Settings?.Currency ?? SupportedCurrency;
        if (!string.Equals(currency, SupportedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(InvoiceIssueCode.UnsupportedCurrency,
                $"Only {SupportedCurrency} invoices are supported (organization currency: {currency})"));
        }

        if (lease.Status == LeaseStatus.Draft)
        {
            errors.Add(new(InvoiceIssueCode.LeaseNotBillable, "A draft lease cannot be invoiced"));
        }

        if (input.HasInvoiceForMonth)
        {
            errors.Add(new(InvoiceIssueCode.AlreadyInvoiced, $"The lease already has an invoice for {month}"));
        }

        var lastDayOfStay = lease.LastDayOfStay;
        var startMonth = BillingMonth.Of(lease.StartDate);
        BillingMonth? endMonth = lastDayOfStay is { } lastDay ? BillingMonth.Of(lastDay) : null;

        if (month < startMonth)
        {
            errors.Add(new(InvoiceIssueCode.MonthBeforeStart,
                $"The lease starts on {lease.StartDate:yyyy-MM-dd}, after {month}"));
        }
        else if (endMonth is { } lastMonth && month > lastMonth.AddMonths(1))
        {
            errors.Add(new(InvoiceIssueCode.MonthAfterEnd,
                $"The lease ended on {lastDayOfStay:yyyy-MM-dd}; it can be invoiced up to {lastMonth.AddMonths(1)}"));
        }

        // Null when the lease does not occupy the room in this month (before it starts, or the month after it ends)
        var stay = month < startMonth || month > endMonth ? null : StayPeriod.Of(month, lease.StartDate, lastDayOfStay);

        var billableServices = input.LeaseServices.Where(s => s.IsBillable).ToList();

        if (stay is not null)
        {
            lines.Add(new(
                $"Rent {Month(month)}{stay.Suffix}",
                1,
                lease.MonthlyRent,
                stay.Prorate(lease.MonthlyRent),
                InvoiceItemSourceType.Rent,
                lease.Id));

            if (lease.DepositAmount > 0 && lease.PreviousLeaseId is null && !input.HasDepositItem)
            {
                lines.Add(new(
                    "Deposit", 1, lease.DepositAmount, lease.DepositAmount, InvoiceItemSourceType.Deposit, lease.Id));
            }
        }

        AddMeteredLines(input, lines, warnings);

        if (stay is not null && lease.Status == LeaseStatus.Active && month > startMonth)
        {
            CheckMonthReadings(input, billableServices, errors, warnings);
        }

        if (stay is not null)
        {
            AddRecurringLines(input, billableServices, stay, lines);
        }

        lines.AddRange(input.CustomLines);

        if (lines.Count == 0)
        {
            errors.Add(new(InvoiceIssueCode.NoItems, $"There is nothing to invoice for {month}"));
        }

        var vatRate = input.Settings?.VatRate ?? 0;
        var totals = InvoiceTotals.Compute(
            lines.Select(l => (l.SourceType, l.Amount)), vatRate, lateFeeAmount: 0, input.DiscountAmount);

        if (totals.TotalAmount < 0)
        {
            errors.Add(new(InvoiceIssueCode.NegativeTotal, "The invoice total must not be negative"));
        }

        return new InvoiceCalculation
        {
            BillingMonth = month,
            DueDate = ResolveDueDate(lease, input.Property, input.Settings, month, input.Today),
            VatRate = vatRate,
            DiscountAmount = input.DiscountAmount,
            Lines = lines,
            Totals = totals,
            Errors = errors,
            Warnings = warnings
        };
    }

    public static int ResolveBillingDay(Lease lease, Property property, OrganizationSettings? settings) =>
        lease.BillingDay ?? property.DefaultBillingDay ?? settings?.DefaultBillingDay ?? lease.StartDate.Day;

    public static DateOnly ResolveDueDate(
        Lease lease, Property property, OrganizationSettings? settings, BillingMonth month, DateOnly today)
    {
        var billingDay = Math.Min(ResolveBillingDay(lease, property, settings), month.LastDay.Day);
        var billingDate = month.FirstDay.AddDays(billingDay - 1);
        var graceDays = property.LateFee.GraceDays ?? settings?.LateFee.GraceDays ?? 0;

        return (billingDate > today ? billingDate : today).AddDays(graceDays);
    }

    private static void AddMeteredLines(
        InvoiceCalculationInput input, List<InvoiceLine> lines, List<InvoiceIssue> warnings)
    {
        var activeLeaseServices = input.LeaseServices.Where(s => s.IsActive).ToDictionary(s => s.ServiceId);

        foreach (var reading in input.UninvoicedReadings
            .OrderBy(r => r.Service!.Name)
            .ThenBy(r => r.ReadingMonth.FirstDay))
        {
            var service = reading.Service!;

            if (!activeLeaseServices.TryGetValue(reading.ServiceId, out var leaseService))
            {
                warnings.Add(new(InvoiceIssueCode.ServiceNotOnLease,
                    $"The {service.Name} reading of {reading.ReadingMonth} is not billed: the lease does not pay for {service.Name}",
                    service.Id));
                continue;
            }

            if (reading.PreviousValue is not { } previous)
            {
                warnings.Add(new(InvoiceIssueCode.ReadingWithoutConsumption,
                    $"The first {service.Name} reading ({reading.ReadingMonth}) has no previous value and is not billed",
                    service.Id));
                continue;
            }

            var consumption = reading.CurrentValue - previous;
            var unitPrice = leaseService.EffectiveUnitPrice;
            var period = reading.ReadingMonth == input.BillingMonth ? "" : $" (period {Month(reading.ReadingMonth)})";

            lines.Add(new(
                $"{service.Name}: {Number(previous)} → {Number(reading.CurrentValue)} ({Number(consumption)} {service.Unit}){period}",
                consumption,
                unitPrice,
                Money.Round(consumption * unitPrice),
                InvoiceItemSourceType.Utility,
                reading.Id));
        }
    }

    private static void CheckMonthReadings(
        InvoiceCalculationInput input,
        IEnumerable<LeaseUtilityService> billableServices,
        List<InvoiceIssue> errors,
        List<InvoiceIssue> warnings)
    {
        var month = input.BillingMonth;

        foreach (var leaseService in billableServices
                     .Where(s => s.Service!.BillingMethod == UtilityBillingMethod.Metered)
                     .OrderBy(s => s.Service!.Name))
        {
            var service = leaseService.Service!;
            var reading = input.MonthReadings.FirstOrDefault(r => r.ServiceId == service.Id);

            if (reading is null)
            {
                errors.Add(new(InvoiceIssueCode.MissingReading,
                    $"No {service.Name} reading has been recorded for {month}", service.Id));
            }
            else if (reading.BilledLeaseId != input.Lease.Id)
            {
                warnings.Add(new(InvoiceIssueCode.ReadingBilledElsewhere,
                    $"The {service.Name} reading of {month} is billed to another lease or to nobody", service.Id));
            }
        }
    }

    private static void AddRecurringLines(
        InvoiceCalculationInput input,
        IEnumerable<LeaseUtilityService> billableServices,
        StayPeriod stay,
        List<InvoiceLine> lines)
    {
        var month = Month(input.BillingMonth);
        var tenantCount = input.Lease.CountTenantsIn(input.BillingMonth);

        foreach (var leaseService in billableServices
                     .Where(s => s.Service!.BillingMethod != UtilityBillingMethod.Metered)
                     .OrderBy(s => s.Service!.Name))
        {
            var service = leaseService.Service!;
            var quantity = leaseService.BillableQuantity(tenantCount);

            if (quantity == 0)
            {
                continue;
            }

            var detail = service.BillingMethod == UtilityBillingMethod.PerTenant
                ? $" ({quantity} tenant{(quantity == 1 ? "" : "s")})"
                : quantity > 1 ? $" (x{quantity})" : "";

            lines.Add(new(
                $"{service.Name} {month}{detail}{stay.Suffix}",
                quantity,
                leaseService.EffectiveUnitPrice,
                stay.Prorate(leaseService.ChargeFor(tenantCount)),
                InvoiceItemSourceType.Utility,
                service.Id));
        }
    }

    private static string Month(BillingMonth month) => month.FirstDay.ToString("MM/yyyy", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);
}
