using Bunit;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.Presentation.Scene;
using LogicLab.Web.Components.Editor;
using LogicLab.Web.Scene;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using TUnit.Assertions.Enums;

namespace LogicLab.Web.Tests;

internal sealed class AccessibleCircuitSceneTests
{
    [Test]
    public async Task AccessibleCircuitScene_CompleteCircuit_RendersReachableTopology()
    {
        await using var context = CreateContext();
        var scene = Project(WebTestCircuit.CreateCompleteCircuit());

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));
        var componentIds = rendered.FindAll("[data-component]")
            .Select(element => element.GetAttribute("data-component")!)
            .ToArray();
        var connectionIds = rendered.FindAll("[data-connection]")
            .Select(element => element.GetAttribute("data-connection")!)
            .ToArray();
        var section = rendered.Find("section[aria-labelledby]");
        var heading = rendered.Find("section h2[id]");

        using (Assert.Multiple())
        {
            await Assert.That(section.GetAttribute("aria-labelledby")).IsEqualTo(heading.Id);
            await Assert.That(heading.TextContent).IsEqualTo(scene.DisplayName);
            await Assert.That(componentIds).IsEquivalentTo(
                scene.Components.Select(component => component.Source.ComponentInstanceId.Value),
                CollectionOrdering.Matching);
            await Assert.That(connectionIds).IsEquivalentTo(
                scene.Connections.Select(connection => connection.Source.NetId.Value),
                CollectionOrdering.Matching);
        }

        foreach (var component in scene.Components)
        {
            await Assert.That(rendered.Find(
                    $"[data-component='{component.Source.ComponentInstanceId.Value}'] h3")
                    .TextContent)
                .IsEqualTo(component.Label);
        }

        foreach (var connection in scene.Connections)
        {
            var summary = rendered.Find(
                $"[data-connection='{connection.Source.NetId.Value}'] .connection-summary span");
            foreach (var terminalLabel in TerminalLabels(scene, connection))
            {
                await Assert.That(summary.TextContent).Contains(terminalLabel);
            }
        }

        var navigationComponent = scene.Components.First(candidate => candidate.Ports.Count > 1);
        var componentSource = new SceneSourceRefV1(
            navigationComponent.Source.CircuitDefinitionId.Value,
            "componentInstance",
            navigationComponent.Source.ComponentInstanceId.Value);
        var firstPort = navigationComponent.Ports[0];
        var secondPort = navigationComponent.Ports[1];
        var firstPortSource = new SceneSourceRefV1(
            firstPort.Source.CircuitDefinitionId.Value,
            "instancePort",
            firstPort.Source.ComponentInstanceId.Value,
            firstPort.Source.PortId);
        var secondPortSource = new SceneSourceRefV1(
            secondPort.Source.CircuitDefinitionId.Value,
            "instancePort",
            secondPort.Source.ComponentInstanceId.Value,
            secondPort.Source.PortId);
        var connectedPort = navigationComponent.Ports.First(port => scene.Connections.Any(connection =>
            connection.Terminals.OfType<InstanceTerminalReference>().Any(terminal =>
                terminal.ComponentInstanceId == navigationComponent.Source.ComponentInstanceId
                && string.Equals(terminal.PortId, port.Source.PortId, StringComparison.Ordinal))));
        var connectedPortSource = new SceneSourceRefV1(
            connectedPort.Source.CircuitDefinitionId.Value,
            "instancePort",
            connectedPort.Source.ComponentInstanceId.Value,
            connectedPort.Source.PortId);
        var connectedNet = scene.Connections.Single(connection =>
            connection.Terminals.OfType<InstanceTerminalReference>().Any(terminal =>
                terminal.ComponentInstanceId == navigationComponent.Source.ComponentInstanceId
                && string.Equals(
                    terminal.PortId,
                    connectedPort.Source.PortId,
                    StringComparison.Ordinal)));
        var connectedNetSource = new SceneSourceRefV1(
            connectedNet.Source.CircuitDefinitionId.Value,
            "net",
            connectedNet.Source.NetId.Value);
        var topologyAttribute = connectedPort.Direction == PortDirection.Input
            ? "data-scene-navigation-left"
            : "data-scene-navigation-right";

        using (Assert.Multiple())
        {
            await Assert.That(rendered.Find($"[data-scene-source='{componentSource.Key}']")
                    .GetAttribute("data-scene-navigation-right"))
                .IsEqualTo(firstPortSource.Key);
            await Assert.That(rendered.Find($"[data-scene-source='{firstPortSource.Key}']")
                    .GetAttribute("data-scene-navigation-down"))
                .IsEqualTo(secondPortSource.Key);
            await Assert.That(rendered.Find($"[data-scene-source='{connectedPortSource.Key}']")
                    .GetAttribute(topologyAttribute))
                .IsEqualTo(connectedNetSource.Key);
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
        var junctionIds = rendered.FindAll("[data-junction]")
            .Select(element => element.GetAttribute("data-junction")!)
            .ToArray();
        var wireGeometryIds = rendered.FindAll("[data-wire-geometry]")
            .Select(element => element.GetAttribute("data-wire-geometry")!)
            .ToArray();
        var projectedConnection = scene.Connections.Single(connection =>
            connection.Source.NetId == net.Id);

        using (Assert.Multiple())
        {
            await Assert.That(junctionIds).IsEquivalentTo(projectedConnection.Junctions
                    .Select(junction => junction.Source.JunctionId.Value),
                CollectionOrdering.Matching);
            await Assert.That(wireGeometryIds).IsEquivalentTo(projectedConnection.WireGeometries
                    .Select(geometry => geometry.Source.WireGeometryId.Value),
                CollectionOrdering.Matching);
            await Assert.That(rendered.FindAll("[data-junction]")
                    .All(element => !string.IsNullOrWhiteSpace(element.TextContent)))
                .IsTrue();
            await Assert.That(rendered.FindAll("[data-wire-geometry]")
                    .All(element => !string.IsNullOrWhiteSpace(element.TextContent)))
                .IsTrue();
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_GeneratedPorts_RenderEveryProjectedPort()
    {
        await using var context = CreateContext();
        var revision = CreateWidthConversionComponents();
        var scene = Project(revision);

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene));

        foreach (var component in scene.Components)
        {
            await AssertRenderedPorts(rendered, component);
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_BoundedPage_ExposesEverySourceThroughNavigation()
    {
        await using var context = CreateContext();
        var scene = Project(WebTestCircuit.CreateCompleteCircuit());
        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene)
            .Add(component => component.PageSize, 1));
        var firstSource = rendered.Find("[data-scene-source]")
            .GetAttribute("data-scene-source");

        await rendered.Find(".semantic-pager button:last-of-type").ClickAsync();
        var secondSource = rendered.Find("[data-scene-source]")
            .GetAttribute("data-scene-source");

        using (Assert.Multiple())
        {
            await Assert.That(rendered.FindAll("[data-scene-source]")).Count().IsEqualTo(1);
            await Assert.That(firstSource).IsNotEqualTo(secondSource);
            await Assert.That(rendered.FindAll(".semantic-pager")).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task AccessibleCircuitScene_Annotation_ExposesMoveAndRemoveActions()
    {
        await using var context = CreateContext();
        var revision = WebTestCircuit.CreateCompleteCircuit();
        revision = WebTestCircuit.Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                revision.Document.EntryCircuitDefinitionId,
                new AnnotationValue(
                    "Carry output",
                    new GridPoint(3, 2),
                    AnnotationAlignment.Start))));
        var scene = Project(revision);
        var actions = new List<SceneSemanticActionV1>();

        var rendered = context.Render<AccessibleCircuitScene>(parameters => parameters
            .Add(component => component.Scene, scene)
            .Add(component => component.OnAction,
                EventCallback.Factory.Create<SceneSemanticActionV1>(
                    this,
                    actions.Add)));
        var annotation = rendered.Find("[data-annotation]");
        await annotation.QuerySelector("[data-scene-action='nudge']")!.ClickAsync();
        await annotation.QuerySelector("[data-scene-action='remove']")!.ClickAsync();

        using (Assert.Multiple())
        {
            await Assert.That(annotation.TextContent).Contains("Carry output");
            await Assert.That(annotation.QuerySelectorAll("[data-scene-action='nudge']"))
                .Count().IsEqualTo(4);
            await Assert.That(annotation.QuerySelectorAll("[data-scene-action='remove']"))
                .HasSingleItem();
            await Assert.That(actions[0]).IsTypeOf<NudgeSceneSemanticActionV1>();
            await Assert.That(actions[1]).IsTypeOf<RemoveSceneSemanticActionV1>();
        }
    }

    private static async Task AssertRenderedPorts(
        IRenderedComponent<AccessibleCircuitScene> rendered,
        AccessibleComponentProjection component)
    {
        var renderedPorts = rendered.FindAll(
            $"[data-component='{component.Source.ComponentInstanceId.Value}'] article > ul > li");
        await Assert.That(renderedPorts).Count().IsEqualTo(component.Ports.Count);

        foreach (var (port, element) in component.Ports.Zip(renderedPorts))
        {
            await Assert.That(element.QuerySelector("strong")?.TextContent)
                .IsEqualTo(port.Label);
        }
    }

    private static IEnumerable<string> TerminalLabels(
        AccessibleSceneProjection scene,
        AccessibleConnectionProjection connection)
    {
        return connection.Terminals.Select(terminal => terminal switch
        {
            DefinitionTerminalReference definition => scene.DefinitionPorts.Single(port =>
                port.Source.DefinitionPortId == definition.DefinitionPortId).Label,
            InstanceTerminalReference instance => scene.Components.Single(component =>
                    component.Source.ComponentInstanceId == instance.ComponentInstanceId)
                .Ports.Single(port => string.Equals(
                    port.Source.PortId,
                    instance.PortId,
                    StringComparison.Ordinal)).Label,
            _ => throw new InvalidOperationException(
                "The Terminal Reference variant is undefined."),
        });
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
        var context = WebTestContext.CreateBunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }
}
