using BoardingHouse.Application.Jobs;
using Hangfire;
using Microsoft.Extensions.Hosting;

namespace BoardingHouse.Infrastructure.BackgroundJobs;

public class RecurringJobRegistry(IRecurringJobManager recurringJobManager) : IHostedService
{
    public const string ExpireLeases = "expire-leases";
    public const string MarkOverdueInvoices = "mark-overdue-invoices";
    public const string GenerateDueInvoices = "generate-due-invoices";
    public const string VoidExpiredOnlinePayments = "void-expired-online-payments";

    private static readonly RecurringJobOptions UtcOptions = new() { TimeZone = TimeZoneInfo.Utc };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobManager.AddOrUpdate<ExpireLeasesJob>(
            ExpireLeases,
            job => job.RunAsync(CancellationToken.None),
            "5 0 * * *",
            UtcOptions);

        recurringJobManager.AddOrUpdate<MarkOverdueInvoicesJob>(
            MarkOverdueInvoices,
            job => job.RunAsync(CancellationToken.None),
            "15 0 * * *",
            UtcOptions);

        recurringJobManager.AddOrUpdate<GenerateDueInvoicesJob>(
            GenerateDueInvoices,
            job => job.RunAsync(CancellationToken.None),
            "30 0 * * *",
            UtcOptions);

        recurringJobManager.AddOrUpdate<VoidExpiredOnlinePaymentsJob>(
            VoidExpiredOnlinePayments,
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *",
            UtcOptions);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
