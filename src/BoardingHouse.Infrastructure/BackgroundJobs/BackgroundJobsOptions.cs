using System.ComponentModel.DataAnnotations;

namespace BoardingHouse.Infrastructure.BackgroundJobs;

public class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";
    public const string SchemaName = "hangfire";

    public bool Enabled { get; set; } = true;

    public bool DashboardEnabled { get; set; }

    [Range(1, 50)]
    public int WorkerCount { get; set; } = 2;
}
