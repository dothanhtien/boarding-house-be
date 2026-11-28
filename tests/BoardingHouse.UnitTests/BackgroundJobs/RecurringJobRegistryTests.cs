using Hangfire;
using Hangfire.Common;
using Moq;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Infrastructure.BackgroundJobs;

namespace BoardingHouse.UnitTests.BackgroundJobs;

public class RecurringJobRegistryTests
{
    private readonly Mock<IRecurringJobManager> _recurringJobManager = new();
    private readonly List<(string Id, Job Job, string Cron, RecurringJobOptions Options)> _registered = [];

    public RecurringJobRegistryTests()
    {
        _recurringJobManager
            .Setup(m => m.AddOrUpdate(It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
            .Callback((string id, Job job, string cron, RecurringJobOptions options) => _registered.Add((id, job, cron, options)));
    }

    [Theory]
    [InlineData(RecurringJobRegistry.ExpireLeases, typeof(ExpireLeasesJob), "5 0 * * *")]
    [InlineData(RecurringJobRegistry.MarkOverdueInvoices, typeof(MarkOverdueInvoicesJob), "15 0 * * *")]
    [InlineData(RecurringJobRegistry.GenerateDueInvoices, typeof(GenerateDueInvoicesJob), "30 0 * * *")]
    [InlineData(RecurringJobRegistry.VoidExpiredOnlinePayments, typeof(VoidExpiredOnlinePaymentsJob), "*/10 * * * *")]
    public async Task StartAsync_RegistersTheJobInUtc(string id, Type jobType, string cron)
    {
        await new RecurringJobRegistry(_recurringJobManager.Object).StartAsync(CancellationToken.None);

        var registration = Assert.Single(_registered, r => r.Id == id);
        Assert.Equal(jobType, registration.Job.Type);
        Assert.Equal("RunAsync", registration.Job.Method.Name);
        Assert.Equal(cron, registration.Cron);
        Assert.Equal(TimeZoneInfo.Utc, registration.Options.TimeZone);
    }

    [Fact]
    public async Task StartAsync_RegistersTheJobsInTheOrderTheyRun()
    {
        await new RecurringJobRegistry(_recurringJobManager.Object).StartAsync(CancellationToken.None);

        Assert.Equal(
            [
                RecurringJobRegistry.ExpireLeases,
                RecurringJobRegistry.MarkOverdueInvoices,
                RecurringJobRegistry.GenerateDueInvoices,
                RecurringJobRegistry.VoidExpiredOnlinePayments
            ],
            _registered.Select(r => r.Id));
    }

    [Fact]
    public async Task StartAsync_GenerateDueInvoicesCallsTheOverloadWithoutDate()
    {
        await new RecurringJobRegistry(_recurringJobManager.Object).StartAsync(CancellationToken.None);

        var registration = Assert.Single(_registered, r => r.Id == RecurringJobRegistry.GenerateDueInvoices);
        Assert.Equal([typeof(CancellationToken)], registration.Job.Method.GetParameters().Select(p => p.ParameterType));
    }
}
