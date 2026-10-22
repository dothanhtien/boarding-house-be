using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Middleware;
using BoardingHouse.Application;
using BoardingHouse.Application.Common;
using BoardingHouse.Infrastructure;
using BoardingHouse.Infrastructure.BackgroundJobs;
using BoardingHouse.Infrastructure.Persistence;
using Hangfire;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting BoardingHouse.Api");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
    );

    builder.Services
        .AddApiControllers()
        .AddApiDocumentation()
        .AddRequestContext()
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddPermissionAuthorization()
        .AddJwtAuthentication(builder.Configuration)
        .AddFrontendCors(builder.Configuration);

    var app = builder.Build();

    if (await app.TryRunCliCommandAsync(args))
    {
        return;
    }

    await app.ApplyDevelopmentDatabaseSetupAsync();

    app.UseExceptionHandler();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<CurrentOrganizationMiddleware>();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]);
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.EnablePersistentAuthentication());
    }

    app.UseHttpsRedirection();

    app.UseCors(ApiServiceCollectionExtensions.FrontendCorsPolicy);

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    if (app.Services.GetRequiredService<IOptions<BackgroundJobsOptions>>().Value.DashboardEnabled)
    {
        app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            // Access is enforced by the permission policy below, not Hangfire's local-requests-only default.
            Authorization = []
        })
        .RequireAuthorization($"{PermissionResources.BackgroundJob}:{PermissionActions.Manage}");
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "BoardingHouse.Api terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
