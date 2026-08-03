using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace LogicLab.Web.Tests;

public sealed class AccessibleCircuitSceneTests
{
    [Test]
    public async Task AccessibleCircuitScene_CompleteCircuit_RendersReachableTopology()
    {
        await using var context = CreateContext();
        var scene = Project(WebTestCircuit.CreateCompleteCircuit());

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));
        var componentLabels = rendered.FindAll("[data-component] h3")
            .Select(element => element.TextContent)
            .ToArray();
        var terminalPaths = rendered.FindAll("[data-connection] .connection-summary span")
            .Select(element => element.TextContent)
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find("section").GetAttribute("aria-labelledby"))
                .IsEqualTo("circuit-scene-heading");
            await Assert.That(rendered.FindAll("[data-component]")).Count().IsEqualTo(3);
            await Assert.That(rendered.FindAll("[data-connection]")).Count().IsEqualTo(2);
            await Assert.That(componentLabels).IsEquivalentTo(["Input", "NOT", "Output"]);
            await Assert.That(terminalPaths).IsEquivalentTo(["Q → A", "Q → D"]);
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_ExplicitTopology_RendersJunctionsAndRoutes()
    {
        await using var context = CreateContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        var definition = revision.Document.EntryCircuitDefinition;
        var net = definition.Nets[0];
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                definition.Id,
                net.Id,
                new GridPoint(2, 1),
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(4, 1)]),
                    new UnroutedWireRoute(),
                ],
                [],
                [])));
        var scene = Project(revision);

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));
        var routeLabels = rendered.FindAll("[data-wire-geometry]")
            .Select(element => element.TextContent.Trim())
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-junction]")).Count().IsEqualTo(1);
            await Assert.That(rendered.FindAll("[data-wire-geometry]")).Count().IsEqualTo(2);
            await Assert.That(rendered.Find("[data-junction]").TextContent.Trim())
                .IsEqualTo("Junction at grid 2, 1");
            await Assert.That(routeLabels)
                .IsEquivalentTo(["Orthogonal · 0,0 → 0,1 → 4,1", "Unrouted"]);
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_GeneratedPorts_RenderDirectionAndWidth()
    {
        await using var context = CreateContext();
        var revision = CreateWidthConversionComponents();
        var scene = Project(revision);

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));

        await AssertRenderedPorts(
            rendered,
            revision,
            "topology.split",
            ["D · Input · 4 bit", "Q0 · Output · 1 bit", "Q1 · Output · 3 bit"]);
        await AssertRenderedPorts(
            rendered,
            revision,
            "topology.concat",
            ["D0 · Input · 1 bit", "D1 · Input · 3 bit", "Q · Output · 4 bit"]);
        await AssertRenderedPorts(
            rendered,
            revision,
            "topology.zero_extend",
            ["D · Input · 4 bit", "Q · Output · 6 bit"]);
        await AssertRenderedPorts(
            rendered,
            revision,
            "topology.sign_extend",
            ["D · Input · 4 bit", "Q · Output · 6 bit"]);
    }

    [Test]
    public async Task AccessibleCircuitScene_SteeringComponents_RenderLabelsAndGeneratedPorts()
    {
        await using var context = CreateContext();
        var revision = CreateSteeringComponents();
        var scene = Project(revision);

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));
        var labels = rendered.FindAll("[data-component] h3")
            .Select(element => element.TextContent)
            .ToArray();

        await Assert.That(labels).IsEquivalentTo(
            ["AND", "Buffer", "Decoder", "DEMUX", "MUX", "NAND", "NOR", "OR",
                "Priority Encoder", "Tri-State", "XNOR", "XOR"],
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await AssertRenderedPorts(
            rendered,
            revision,
            "logic.mux",
            ["D0 · Input · 2 bit", "D1 · Input · 2 bit", "S · Input · 1 bit",
                "Q · Output · 2 bit"]);
        await AssertRenderedPorts(
            rendered,
            revision,
            "logic.priority_encoder",
            ["A0 · Input · 1 bit", "A1 · Input · 1 bit", "A2 · Input · 1 bit",
                "Q · Output · 2 bit", "VALID · Output · 1 bit"]);
    }

    private static async Task AssertRenderedPorts(
        IRenderedComponent<AccessibleCircuitScene> rendered,
        ProjectRevision revision,
        string contractId,
        string[] expected)
    {
        var instance = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            item => item.Target is LibraryComponentTarget library
                && library.ContractKey.ContractId == contractId);
        var actual = rendered.FindAll(
                $"[data-component='{instance.Id.Value}'] article > ul > li")
            .Select(element => element.TextContent.Trim())
            .ToArray();

        await Assert.That(actual)
            .IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static AccessibleSceneProjection Project(ProjectRevision revision)
    {
        return AccessibleSceneProjector.TryProject(revision, 10_000, out var scene)
            ? scene
            : throw new InvalidOperationException(
                "The bounded test Scene could not be projected.");
    }

    private static ProjectRevision CreateWidthConversionComponents()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Accessible topology markup",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        revision = Place(revision, "topology.split",
        [
            new ComponentParameterBinding("width", new Unsigned32ParameterValue(4)),
            new ComponentParameterBinding(
                "slices",
                new SlicesParameterValue(
                    [new BitSlice(0, 1), new BitSlice(1, 3)])),
        ]);
        revision = Place(revision, "topology.concat",
        [
            new ComponentParameterBinding(
                "inputWidths",
                new WidthsParameterValue([1, 3])),
        ]);
        revision = Place(revision, "topology.zero_extend", ExtensionParameters(4, 6));
        return Place(revision, "topology.sign_extend", ExtensionParameters(4, 6));
    }

    private static ProjectRevision CreateSteeringComponents()
    {
        var revision = ((ProjectGenesisCommitted)ProjectEditor.Begin(new NewProjectSeed(
            "Accessible steering markup",
            LibrarySnapshot.Core,
            new SymbolProfileReference(
                "TeachingMixed",
                "1.0.0",
                IndicationConvention.Negation),
            "Main"))).Revision;
        foreach (var contractId in new[]
                 {
                     "logic.and", "logic.buffer", "logic.decoder", "logic.demux",
                     "logic.mux", "logic.nand", "logic.nor", "logic.or",
                     "logic.priority_encoder", "logic.tristate", "logic.xnor", "logic.xor",
                 })
        {
            ComponentParameterBinding[] parameters = contractId switch
            {
                "logic.buffer" =>
                    [new ComponentParameterBinding("width", new Unsigned32ParameterValue(2))],
                "logic.decoder" =>
                    [
                        new ComponentParameterBinding(
                            "selectorWidth",
                            new Unsigned32ParameterValue(1)),
                        new ComponentParameterBinding(
                            "enablePolarity",
                            new ChoiceParameterValue("activeHigh")),
                    ],
                "logic.demux" or "logic.mux" =>
                    [
                        new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                        new ComponentParameterBinding(
                            "selectorWidth",
                            new Unsigned32ParameterValue(1)),
                    ],
                "logic.priority_encoder" =>
                    [
                        new ComponentParameterBinding(
                            "inputCount",
                            new Unsigned32ParameterValue(3)),
                        new ComponentParameterBinding(
                            "priority",
                            new ChoiceParameterValue("highestIndex")),
                    ],
                "logic.tristate" =>
                    [
                        new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                        new ComponentParameterBinding(
                            "enablePolarity",
                            new ChoiceParameterValue("activeHigh")),
                    ],
                _ =>
                    [
                        new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                        new ComponentParameterBinding("fanIn", new Unsigned32ParameterValue(2)),
                    ],
            };
            revision = Place(revision, contractId, parameters);
        }

        return revision;
    }

    private static ProjectRevision Place(
        ProjectRevision revision,
        string contractId,
        ComponentParameterBinding[] parameters)
    {
        var count = revision.Document.EntryCircuitDefinition.ComponentInstances.Count;
        return WebTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(new GridPoint(count * 4, 0)))));
    }

    private static ComponentParameterBinding[] ExtensionParameters(
        uint inputWidth,
        uint outputWidth)
    {
        return
        [
            new ComponentParameterBinding(
                "inputWidth",
                new Unsigned32ParameterValue(inputWidth)),
            new ComponentParameterBinding(
                "outputWidth",
                new Unsigned32ParameterValue(outputWidth)),
        ];
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }
}
