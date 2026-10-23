using BoardingHouse.Application.Jobs;
using Hangfire;
using Microsoft.Extensions.Hosting;

namespace BoardingHouse.Infrastructure.BackgroundJobs;

public class RecurringJobRegistry(IRecurringJobManager recurringJobManager) : IHostedService
{
    public const string ExpireLeases = "expire-leases";

    private static readonly RecurringJobOptions UtcOptions = new() { TimeZone = TimeZoneInfo.Utc };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobManager.AddOrUpdate<ExpireLeasesJob>(
            ExpireLeases,
            job => job.RunAsync(CancellationToken.None),
            "5 0 * * *",
            UtcOptions);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
