using System.Globalization;
using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Components.Pages;
using LogicLab.Web.Scene;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal static class WebTestContext
{
    public static BunitContext CreateBunitContext(
        bool configureAttachmentNavigation = false)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(LogicLabCultures.English);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(LogicLabCultures.English);
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton(WorkspacePolicy.Default);
        context.JSInterop.SetupVoid(
            "Microsoft.FluentUI.Blazor.Utilities.Attributes.copyToShadow",
            static _ => true)
            .SetVoidResult();
        context.JSInterop.SetupVoid(
            "Microsoft.FluentUI.Blazor.Utilities.Attributes.observeAttributeChange",
            static _ => true)
            .SetVoidResult();
        context.JSInterop.SetupVoid(
            "Microsoft.FluentUI.Blazor.Components.TextInput.attachImmediateEvent",
            static _ => true)
            .SetVoidResult();
        context.JSInterop.SetupModule(BrowserSceneAdapter.ModulePath).Mode =
            JSRuntimeMode.Loose;
        if (configureAttachmentNavigation)
        {
            context.JSInterop.SetupModule(WorkspaceAttachmentNavigation.ModulePath).Mode =
                JSRuntimeMode.Loose;
        }

        return context;
    }

    public static BunitContext CreateBunitContext(
        out BunitJSModuleInterop attachmentNavigation)
    {
        var context = CreateBunitContext();
        attachmentNavigation = context.JSInterop.SetupModule(
            WorkspaceAttachmentNavigation.ModulePath);
        attachmentNavigation.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
