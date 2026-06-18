using Fw3.QbAgent.Core.Abstractions;
using Fw3.QbAgent.Core.Configuration;
using Fw3.QbAgent.Core.Idempotency;
using Fw3.QbAgent.Core.Queue;
using Fw3.QbAgent.Host;
using Fw3.QbAgent.Host.Api;
using Fw3.QbAgent.Host.Auth;
using Fw3.QbAgent.Host.Errors;
using Fw3.QbAgent.Host.Logging;
using Fw3.QbAgent.QuickBooks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Durable data root — the service runs unattended under a machine account, so logs and idempotency
// records live somewhere persistent and writable rather than next to the binaries.
var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Fw3QbAgent");
var isTesting = builder.Environment.IsEnvironment("Testing");

// ---- Logging: Serilog (console always; rolling app-log file outside tests) ----
builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    if (!isTesting)
    {
        loggerConfig.WriteTo.File(
            Path.Combine(dataRoot, "logs", "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30);
    }
});

// ---- Run as a Windows Service when started by the SCM (no-op when run as a console app) ----
builder.Host.UseWindowsService(o => o.ServiceName = "Fw3QbAgent");

// ---- Options ----
builder.Services
    .AddOptions<QbAgentOptions>()
    .Bind(builder.Configuration.GetSection(QbAgentOptions.SectionName))
    .PostConfigure(o =>
    {
        o.IdempotencyPath = ResolveUnderDataRoot(o.IdempotencyPath);
        o.QbXmlAuditPath = ResolveUnderDataRoot(o.QbXmlAuditPath);
    });

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

// Gateway choice depends only on Mode, so it is safe to read configuration directly here.
var qbOptions = builder.Configuration.GetSection(QbAgentOptions.SectionName).Get<QbAgentOptions>() ?? new QbAgentOptions();
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

// ---- The single QuickBooks choke point: queue + STA worker ----
builder.Services.AddSingleton<QbRequestQueue>();
builder.Services.AddSingleton<IQbRequestQueue>(sp => sp.GetRequiredService<QbRequestQueue>());
builder.Services.AddHostedService<QbWorkerService>();

// ---- Durable concerns ----
builder.Services.AddSingleton<IQbXmlAuditLog>(sp =>
    new SerilogQbXmlAuditLog(sp.GetRequiredService<IOptions<QbAgentOptions>>().Value.QbXmlAuditPath));

builder.Services.AddSingleton<IIdempotencyStore>(sp =>
    new FileIdempotencyStore(
        sp.GetRequiredService<IOptions<QbAgentOptions>>().Value.IdempotencyPath,
        sp.GetRequiredService<ILogger<FileIdempotencyStore>>()));

builder.Services.AddQuickBooksGateway(qbOptions);

// ---- Structured errors as ProblemDetails ----
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<QbAgentExceptionHandler>();

// ---- OpenAPI: first-party document at /openapi.json, Swagger UI for browsing ----
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "FormulaWeb QuickBooks Agent",
            Version = "v1",
            Description = "Thin translator between the FormulaWeb ERP and QuickBooks Desktop. JSON in, JSON out.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = authOptions.HeaderName,
            Description = "Shared secret for the internal ERP->agent network.",
        };

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

// Auth first: everything except /health and the OpenAPI doc/UI requires the API key.
app.UseMiddleware<ApiKeyMiddleware>();

app.MapOpenApi("/openapi.json");
app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi.json", "FormulaWeb QuickBooks Agent v1"));

app.MapHealthEndpoints();
app.MapCustomerEndpoints();

app.Run();

string ResolveUnderDataRoot(string path) =>
    Path.IsPathRooted(path) ? path : Path.Combine(dataRoot, path);

// Exposed so the integration tests can spin up the real pipeline via WebApplicationFactory.
public partial class Program;
