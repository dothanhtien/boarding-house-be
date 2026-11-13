using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Invoicing;

public sealed record InvoiceTotals(decimal Subtotal, decimal VatAmount, decimal TotalAmount)
{
    public static InvoiceTotals Compute(
        IEnumerable<(InvoiceItemSourceType SourceType, decimal Amount)> lines,
        decimal vatRate,
        decimal lateFeeAmount,
        decimal discountAmount)
    {
        var amounts = lines.ToList();
        var subtotal = amounts.Sum(l => l.Amount);
        var deposit = amounts.Where(l => l.SourceType == InvoiceItemSourceType.Deposit).Sum(l => l.Amount);
        var vatAmount = Money.Round(Math.Max(0, subtotal - deposit) * vatRate / 100);

        return new InvoiceTotals(subtotal, vatAmount, subtotal + vatAmount + lateFeeAmount - discountAmount);
    }
}
