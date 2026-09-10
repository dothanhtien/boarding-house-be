using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Authorization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Middleware;
using BoardingHouse.Api.OpenApi;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Interceptors;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services;
using BoardingHouse.Api.Services.Caching;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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

    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = ValidationProblemDetailsFactory.Create;
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // Stop at the first failed rule within a property so each field surfaces a single error.
    ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

    builder.Services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Boarding House API";
            document.Info.Version = "v1";
            return Task.CompletedTask;
        });
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    });

    builder.Services.AddDbContext<AppDbContext>((sp, options) =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(
                sp.GetRequiredService<ICurrentUserAccessor>(),
                sp.GetRequiredService<ILogger<AuditableEntitySaveChangesInterceptor>>()))
            .UseSnakeCaseNamingConvention()
    );

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
        options.InstanceName = "BoardingHouse";
    });

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddProblemDetails();

    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    builder.Services.AddScoped<IRoleRepository, RoleRepository>();
    builder.Services.AddScoped<IUserRoleRepository, UserRoleRepository>();
    builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
    builder.Services.AddScoped<IOrganizationSettingsRepository, OrganizationSettingsRepository>();

    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddSingleton<ITokenService, TokenService>();
    builder.Services.AddScoped<IOrganizationService, OrganizationService>();
    builder.Services.AddScoped<IOrganizationSettingsService, OrganizationSettingsService>();

    builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
    builder.Services.AddScoped<IUserCache, UserCache>();
    builder.Services.AddScoped<IRolePermissionCache, RolePermissionCache>();
    builder.Services.AddScoped<IPermissionService, PermissionService>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

    builder.Services.AddJwtAuthentication(builder.Configuration);

    builder.Services.AddAuthorization();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
        {
            policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    // `dotnet BoardingHouse.Api.dll --seed-rbac`: runs the default role/permission seed then exits,
    // without starting the web host. Used to seed manually in Production (after migrating) — seeding does
    // NOT run automatically in Production on app startup (risk of a race condition when multiple instances start and insert
    // duplicate rows at the same time).
    if (args.Contains("--seed-rbac"))
    {
        using var seedScope = app.Services.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RbacSeeder.SeedAsync(seedDb);
        Log.Information("RBAC seed completed");
        return;
    }

    if (args.Contains("--seed-admin"))
    {
        var adminEmail = builder.Configuration["ADMIN_EMAIL"];
        var adminPassword = builder.Configuration["ADMIN_PASSWORD"];
        var adminFullName = builder.Configuration["ADMIN_FULLNAME"] ?? "Platform Admin";

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            Log.Fatal("--seed-admin requires ADMIN_EMAIL and ADMIN_PASSWORD environment variables to be set");
            return;
        }

        using var adminSeedScope = app.Services.CreateScope();
        var adminSeedDb = adminSeedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AdminSeeder.SeedAsync(adminSeedDb, adminEmail, adminPassword, adminFullName);
        Log.Information("Platform admin seed completed");
        return;
    }

    if (app.Environment.IsDevelopment())
    {
        using var devSeedScope = app.Services.CreateScope();
        var devSeedDb = devSeedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RbacSeeder.SeedAsync(devSeedDb);
    }

    app.UseExceptionHandler();

    app.UseMiddleware<CorrelationIdMiddleware>();

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

    app.UseCors("Frontend");

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

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
