using Bunit;
using LogicLab.Application.Workspaces;
using LogicLab.Domain;
using LogicLab.Web.Components.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

internal sealed partial class LogicAnalyzerTests
{
    [Test]
    [Arguments("1111", "hex", "F")]
    [Arguments("10000000", "hex", "80")]
    [Arguments("111111111", "hex", "1FF")]
    [Arguments("000000001", "hex", "001")]
    [Arguments("1111111111111111111111111111111111111111111111111111111111111111", "hex", "FFFFFFFFFFFFFFFF")]
    [Arguments("11111111111111111111111111111111111111111111111111111111111111111", "hex", "1FFFFFFFFFFFFFFFF")]
    [Arguments("11111111", "unsigned", "255")]
    [Arguments("10XZ", "hex", "10XZ")]
    public async Task RadixChange_LogicVector_UsesUnsignedSignalWidth(
        string bits,
        string radix,
        string expected)
    {
        await using var context = WebTestContext.CreateBunitContext();
        context.Renderer.SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var fixture = Fixture.Create();
        var probe = fixture.Projection.Simulation!.Probes[0];
        var values = bits.Reverse().Select(symbol => symbol switch
        {
            '0' => LogicValue.Zero,
            '1' => LogicValue.One,
            'X' => LogicValue.X,
            'Z' => LogicValue.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(bits)),
        }).ToArray();
        fixture = fixture.WithProbes([new ProbeProjection(probe.ProbeId, probe.Source, values)]);
        var rendered = context.Render<LogicAnalyzer>(parameters => parameters
            .Add(component => component.Projection, fixture.Projection)
            .Add(component => component.TraceReader, (_, _) =>
                Task.FromResult<TraceWindowOutcome?>(new TraceWindowUnavailable(
                    TraceWindowUnavailableReason.Evicted, 0, 0))));

        var select = rendered.FindComponent<FluentSelect<string, string>>();
        await rendered.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(radix));

        await Assert.That(rendered.Find(".probe-identity strong").TextContent)
            .IsEqualTo(expected);
    }
}
