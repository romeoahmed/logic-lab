using LogicLab.Application.Workspaces;
using LogicLab.Web;
using LogicLab.Web.Components;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IEditorWorkspace>(services =>
    EditorWorkspaceFactory.Create(
        loggerFactory: services.GetRequiredService<ILoggerFactory>(),
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
