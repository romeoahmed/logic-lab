using Microsoft.Playwright;

namespace LogicLab.Web.BrowserTests;

/// <summary>Serves adapter fixtures with the same module paths used by the Web host.</summary>
internal static class BrowserAdapterPage
{
    public static async Task OpenAsync(IPage page, string origin, string document)
    {
        await page.RouteAsync($"{origin}/**", route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/")
            {
                return route.FulfillAsync(new() { ContentType = "text/html", Body = document });
            }

            var contentType = Path.GetExtension(path) switch
            {
                ".js" => "text/javascript",
                ".css" => "text/css",
                ".woff2" => "font/woff2",
                _ => null,
            };
            var assetRoot = Path.Combine(AppContext.BaseDirectory, "BrowserAssets") + Path.DirectorySeparatorChar;
            var asset = Path.GetFullPath(Path.Combine(assetRoot, path.TrimStart('/')));
            return contentType is not null
                && asset.StartsWith(assetRoot, StringComparison.Ordinal)
                && File.Exists(asset)
                ? route.FulfillAsync(new() { ContentType = contentType, Path = asset })
                : route.FulfillAsync(new() { Status = 404 });
        });
        await page.GotoAsync($"{origin}/");
    }
}
