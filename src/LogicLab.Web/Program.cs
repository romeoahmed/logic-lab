using LogicLab.Application.Workspaces;
using LogicLab.Infrastructure.Persistence;
using LogicLab.Web;
using LogicLab.Web.Components;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("LogicLab")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:LogicLab must be configured.");
var workspacePolicy = WorkspacePolicy.Default;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddLogicLabSqlitePersistence(
    connectionString,
    workspacePolicy.IdempotencyRecordCount);
builder.Services.AddSingleton<IEditorWorkspace>(services =>
    EditorWorkspaceFactory.Create(
        workspacePolicy: workspacePolicy,
        loggerFactory: services.GetRequiredService<ILoggerFactory>(),
        timeProvider: services.GetRequiredService<TimeProvider>(),
        durableProjectRepository:
            services.GetRequiredService<IDurableProjectRepository>(),
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
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options =>
        options.DisableWebSocketCompression = true);

app.Run();
