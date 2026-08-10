using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Persistence;
using LogicLab.Web;
using LogicLab.Web.Components;
using LogicLab.Web.Data;
using LogicLab.Web.Identity;
using LogicLab.Web.Projects;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("LogicLab")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:LogicLab must be configured.");
var workspacePolicy = WorkspacePolicy.Default;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(workspacePolicy);
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
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
builder.Services.AddDbContext<ApplicationIdentityDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationIdentityDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddLogicLabSqlitePersistence(
    connectionString,
    workspacePolicy.IdempotencyRecordCount);
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IProjectCatalogCursorProtector,
    DataProtectionProjectCatalogCursorProtector>();
builder.Services.AddSingleton<IDurableProjectCatalogAuthorization,
    AuthenticatedDurableProjectCatalogAuthorization>();
builder.Services.AddSingleton<IDurableProjectCatalog>(services =>
    DurableProjectCatalogFactory.Create(
        workspacePolicy,
        services.GetRequiredService<IDurableProjectCatalogRepository>(),
        services.GetRequiredService<IProjectCatalogCursorProtector>()));
builder.Services.AddSingleton<IEditorWorkspace>(services =>
    EditorWorkspaceFactory.Create(
        workspacePolicy: workspacePolicy,
        loggerFactory: services.GetRequiredService<ILoggerFactory>(),
        timeProvider: services.GetRequiredService<TimeProvider>(),
        durableProjectRepository:
            services.GetRequiredService<IDurableProjectRepository>(),
        durableProjectLoader:
            services.GetRequiredService<IDurableProjectLoader>(),
        buildFingerprint: LogicLabWebBuild.Fingerprint));
builder.Services.AddFluentUIComponents();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapLogicLabAccountEndpoints();
app.MapDurableProjectEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options =>
        options.DisableWebSocketCompression = true);

app.Run();
