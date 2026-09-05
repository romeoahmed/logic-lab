using System.Globalization;
using System.Threading.RateLimiting;
using Azure.Core;
using Azure.Extensions.AspNetCore.DataProtection.Blobs;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Identity;
using LogicLab.Infrastructure.Persistence;
using LogicLab.Infrastructure.Transfers;
using LogicLab.ProjectFormat;
using LogicLab.Web;
using LogicLab.Web.Components;
using LogicLab.Web.Health;
using LogicLab.Web.Hosting;
using LogicLab.Web.Identity;
using LogicLab.Web.Projects;
using LogicLab.Web.Scene;
using LogicLab.Web.Transfers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.FluentUI.AspNetCore.Components;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("LogicLab")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:LogicLab must be configured.");
var production = builder.Environment.IsProduction()
    ? AzureProductionConfiguration.Load(builder.Configuration, connectionString)
    : null;
TokenCredential? azureCredential = production is null
    ? null
    : new ManagedIdentityCredential(
        ManagedIdentityId.FromUserAssignedClientId(
            production.ManagedIdentityClientId.ToString()));
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
if (azureCredential is not null)
{
    ConfigureAzurePostgreSqlAuthentication(dataSourceBuilder, azureCredential);
}

var dataSource = dataSourceBuilder.Build();
var workspacePolicy = WorkspacePolicy.Default;
var packagePolicy = PackagePolicy.Default;
var accountIngressPolicy = AccountIngressPolicy.Default;
var durableProjectIngressPolicy = DurableProjectIngressPolicy.Default;
var anonymousWorkspaceIngressPolicy = AnonymousWorkspaceIngressPolicy.Default;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(dataSource);
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(45));
if (production is not null && azureCredential is not null)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedHost
            | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.AllowedHosts.Add(production.PublicOrigin.Host);
    });
    builder.Services.Configure<HostFilteringOptions>(options =>
        options.AllowedHosts = [production.PublicOrigin.Host]);
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options => options.Credential = azureCredential);
}
builder.Services.AddSingleton(workspacePolicy);
builder.Services.AddSingleton(BrowserPolicy.Default);
builder.Services.AddSingleton(anonymousWorkspaceIngressPolicy);
builder.Services.AddSingleton<RateLimiter>(services => services
    .GetRequiredService<AnonymousWorkspaceIngressPolicy>()
    .CreateLimiter());
builder.Services.AddProjectExportPolicies();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider,
    IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/login";
    options.SlidingExpiration = false;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Events.OnSigningIn = AuthenticationTicketExpiry.StampAsync;
    var validatePrincipal = options.Events.OnValidatePrincipal;
    options.Events.OnValidatePrincipal = context =>
        AuthenticationTicketExpiry.ValidateAndPreserveAsync(
            context,
            validatePrincipal);
    var redirectToLogin = options.Events.OnRedirectToLogin;
    options.Events.OnRedirectToLogin = context =>
        context.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<IDisableCookieRedirectMetadata>() is not null
            ? LogicLabProblemDetails.Create(
                context.HttpContext,
                LogicLabProblemDetails.AuthenticationRequiredCode).ExecuteAsync(
                    context.HttpContext)
            : redirectToLogin(context);
    var redirectToAccessDenied = options.Events.OnRedirectToAccessDenied;
    options.Events.OnRedirectToAccessDenied = context =>
        context.HttpContext.GetEndpoint()?.Metadata
            .GetMetadata<IDisableCookieRedirectMetadata>() is not null
            ? LogicLabProblemDetails.Create(
                context.HttpContext,
                LogicLabProblemDetails.ForbiddenCode).ExecuteAsync(
                    context.HttpContext)
            : redirectToAccessDenied(context);
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(4);
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = LogicLabCultures.Supported.ToArray();
    options.DefaultRequestCulture = new RequestCulture(LogicLabCultures.English);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});
builder.Services.AddSingleton<LogicLabReadinessHealthCheck>(services => new(
    services.GetRequiredService<IEditorWorkspaceReadiness>(),
    services.GetRequiredService<LogicLabPersistenceReadiness>(),
    services.GetRequiredService<DataProtectionReadiness>(),
    services.GetRequiredService<IServiceScopeFactory>(),
    services.GetRequiredService<IHostApplicationLifetime>()));
builder.Services.AddHealthChecks()
    .AddCheck<LogicLabReadinessHealthCheck>(
        "required_dependencies",
        tags: ["ready"]);
builder.Services.AddRateLimiter();
builder.Services.AddOptions<RateLimiterOptions>()
    .Configure<ProjectExportTransferPolicy>((options, projectExportTransferPolicy) =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            projectExportTransferPolicy.ConcurrentTransferPartition);
        options.AddPolicy<string>(
            AccountIngressPolicy.LoginRateLimitPolicyName,
            accountIngressPolicy.LoginPartition);
        options.AddPolicy<string>(
            AccountIngressPolicy.RegistrationRateLimitPolicyName,
            accountIngressPolicy.RegistrationPartition);
        options.AddPolicy<string>(
            AccountIngressPolicy.LogoutRateLimitPolicyName,
            accountIngressPolicy.LogoutPartition);
        options.AddPolicy<string>(
            DurableProjectIngressPolicy.OpenRateLimitPolicyName,
            durableProjectIngressPolicy.OpenPartition);
        options.AddPolicy<string>(
            ProjectExportTransferPolicy.DownloadRateLimitPolicyName,
            projectExportTransferPolicy.DownloadPartition);
        options.OnRejected = async (context, _) =>
        {
            if (context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfter))
            {
                context.HttpContext.Response.Headers[
                    Microsoft.Net.Http.Headers.HeaderNames.RetryAfter] = Math
                        .Ceiling(retryAfter.TotalSeconds)
                        .ToString(CultureInfo.InvariantCulture);
            }

            var code = context.HttpContext.GetEndpoint()?.Metadata
                .GetMetadata<RateLimitProblemDetailsMetadata>()?.Code
                ?? LogicLabProblemDetails.AuthenticationRateLimitExceededCode;
            if (context.HttpContext.GetEndpoint()?.Metadata
                    .GetMetadata<ProjectExportTransferMetadata>() is not null)
            {
                ProjectExportEndpointRouteBuilderExtensions.DisableCaching(
                    context.HttpContext);
            }

            await LogicLabProblemDetails.Create(context.HttpContext, code)
                .ExecuteAsync(context.HttpContext);
        };
    });
builder.Services.AddLogicLabIdentity(dataSource)
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddLogicLabPersistence(
    dataSource,
    workspacePolicy.IdempotencyRecordCount);
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(AzureProductionConfiguration.DataProtectionApplicationName);
BlobClient? keyBlob = null;
if (production is not null && azureCredential is not null)
{
    keyBlob = new BlobClient(
        production.DataProtectionBlobUri,
        azureCredential);
    builder.Services.AddSingleton(keyBlob);
    dataProtection.PersistKeysToAzureBlobStorage(keyBlob);
}

builder.Services.AddSingleton<DataProtectionReadiness>(services => new(
    services.GetRequiredService<IDataProtectionProvider>(),
    keyBlob));
builder.Services.AddSingleton<IProjectCatalogCursorProtector,
    DataProtectionProjectCatalogCursorProtector>();
builder.Services.AddSingleton<TemporaryProjectExportStore>();
builder.Services.AddSingleton<IProjectExportStore>(services =>
    services.GetRequiredService<TemporaryProjectExportStore>());
builder.Services.AddSingleton<IProjectExportDownloads>(services =>
    services.GetRequiredService<TemporaryProjectExportStore>());
builder.Services.AddSingleton<IDurableProjectCatalog>(services =>
    DurableProjectCatalogFactory.Create(
        workspacePolicy,
        services.GetRequiredService<IDurableProjectCatalogRepository>(),
        services.GetRequiredService<IProjectCatalogCursorProtector>(),
        services.GetRequiredService<ILoggerFactory>()));
builder.Services.AddScoped(static _ => new DurableProjectCatalogPageState());
builder.Services.AddSingleton(packagePolicy);
builder.Services.AddSingleton<ProjectImportWorkflow>();
builder.Services.AddSingleton<IEditorWorkspace>(services =>
    EditorWorkspaceFactory.Create(
        workspacePolicy: workspacePolicy,
        packagePolicy: packagePolicy,
        loggerFactory: services.GetRequiredService<ILoggerFactory>(),
        timeProvider: services.GetRequiredService<TimeProvider>(),
        durableProjectRepository:
            services.GetRequiredService<IDurableProjectRepository>(),
        durableProjectLoader:
            services.GetRequiredService<IDurableProjectLoader>(),
        projectExportStore:
            services.GetRequiredService<IProjectExportStore>(),
        projectExportPreparationPolicy:
            services.GetRequiredService<ProjectExportPreparationPolicy>(),
        buildFingerprint: LogicLabWebBuild.Fingerprint));
builder.Services.AddSingleton<IEditorWorkspaceReadiness>(services =>
    (IEditorWorkspaceReadiness)services.GetRequiredService<IEditorWorkspace>());
builder.Services.AddFluentUIComponents();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (production is not null)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRouting();
app.UseRequestLocalization();
app.UseMiddleware<RequestBodyLimitProblemDetailsMiddleware>();
app.UseAuthentication();
app.UseMiddleware<AnonymousWorkspaceCallerMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<RequestBodyBufferingMiddleware>();
app.UseAntiforgery();
app.UseMiddleware<AntiforgeryProblemDetailsMiddleware>();
app.MapStaticAssets();
app.MapLogicLabAccountEndpoints();
app.MapDurableProjectEndpoints();
app.MapProjectExportEndpoints();
app.MapLogicLabCultureEndpoint();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
    ResponseWriter = WriteHealthStatus,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthStatus,
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options =>
        options.DisableWebSocketCompression = true)
    .AddDurableProjectCatalogPageAdapter();

app.Run();

static void ConfigureAzurePostgreSqlAuthentication(
    NpgsqlDataSourceBuilder dataSourceBuilder,
    TokenCredential credential)
{
    var tokenRequest = new TokenRequestContext(
        ["https://ossrdbms-aad.database.windows.net/.default"]);
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (_, cancellationToken) =>
            (await credential.GetTokenAsync(tokenRequest, cancellationToken)).Token,
        TimeSpan.FromMinutes(55),
        TimeSpan.FromSeconds(5));
}

static Task WriteHealthStatus(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "text/plain; charset=utf-8";
    return context.Response.WriteAsync(
        report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy");
}
