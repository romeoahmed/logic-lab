using System.Text.RegularExpressions;
using Bunit;
using LogicLab.Web.Components.Pages;

namespace LogicLab.Web.Tests;

internal sealed class HomePageTests
{
    [Test]
    public async Task Home_StaticRender_UsesCapabilityCopyWithoutDeliverySliceLabels()
    {
        await using var context = new BunitContext();

        var rendered = context.Render<Home>();
        var lede = rendered.Find(".lede").TextContent;

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("h1").TextContent)
                .IsEqualTo("Build it. Compile it. Watch the signal move.");
            await Assert.That(lede).Contains("authoring");
            await Assert.That(lede).Contains("simulation");
            await Assert.That(Regex.IsMatch(
                    lede,
                    @"\bSlice \d+\b",
                    RegexOptions.CultureInvariant))
                .IsFalse();
        }
    }
}
