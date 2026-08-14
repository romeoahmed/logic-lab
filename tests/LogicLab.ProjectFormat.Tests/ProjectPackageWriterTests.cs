using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using LogicLab.Domain.Components;
using LogicLab.ProjectFormat;
using TUnit.Assertions.Enums;
using static LogicLab.ProjectFormat.Tests.ProjectPackageTestFixture;

namespace LogicLab.ProjectFormat.Tests;

internal sealed class ProjectPackageWriterTests
{
    [Test]
    public async Task PackagePolicy_InvalidStableTokens_ThrowArgumentException()
    {
        string[] invalidTokens =
        [
            "_leading",
            ".leading",
            "-leading",
            "contains/slash",
            "contains space",
            "nonascii-\u00e9",
            "control\u0001",
            new string('a', 97),
        ];
        var limits = PackagePolicy.Development.Limits;

        using (Assert.Multiple())
        {
            foreach (var invalidToken in invalidTokens)
            {
                await Assert.That(() => new PackagePolicy(
                        invalidToken,
                        "1",
                        limits))
                    .ThrowsExactly<ArgumentException>();
                await Assert.That(() => new PackagePolicy(
                        "valid",
                        invalidToken,
                        limits))
                    .ThrowsExactly<ArgumentException>();
            }
        }
    }

    [Test]
    public async Task PackagePolicy_StableTokenBoundaryValues_AreAccepted()
    {
        var oneCharacter = "A";
        var maximumLength = $"A._-{new string('z', 92)}";
        var limits = PackagePolicy.Development.Limits;

        var first = new PackagePolicy(oneCharacter, maximumLength, limits);
        var second = new PackagePolicy(maximumLength, oneCharacter, limits);

        using (Assert.Multiple())
        {
            await Assert.That(first.PolicyId).IsEqualTo(oneCharacter);
            await Assert.That(first.PolicyRevision).IsEqualTo(maximumLength);
            await Assert.That(second.PolicyId).IsEqualTo(maximumLength);
            await Assert.That(second.PolicyRevision).IsEqualTo(oneCharacter);
        }
    }

    [Test]
    public async Task WriteAsync_FullyPopulatedRevision_EmitsEveryV1DiscriminatorAndDigest()
    {
        var revision = CreateFullyPopulatedRevision();
        await using var destination = new MemoryStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                destination,
                PackagePolicy.Development),
            CancellationToken.None);

        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageWriteSucceeded>())!;
        var entries = ReadEntries(destination);
        var projectBytes = entries["project.json"];
        using var project = JsonDocument.Parse(projectBytes);
        var definitions = project.RootElement.GetProperty("circuitDefinitions")
            .EnumerateArray()
            .ToArray();
        var components = definitions
            .SelectMany(definition => definition.GetProperty("componentInstances")
                .EnumerateArray())
            .ToArray();
        var valueKinds = components
            .SelectMany(component => component.GetProperty("parameters")
                .EnumerateArray())
            .Select(parameter => parameter.GetProperty("value")
                .GetProperty("kind").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var targetKinds = components
            .Select(component => component.GetProperty("target")
                .GetProperty("kind").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var routeKinds = definitions
            .SelectMany(definition => definition.GetProperty("wireGeometry")
                .EnumerateArray())
            .Select(geometry => geometry.GetProperty("route")
                .GetProperty("kind").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var terminalKinds = definitions
            .SelectMany(definition => definition.GetProperty("nets")
                .EnumerateArray())
            .SelectMany(net => net.GetProperty("terminals").EnumerateArray())
            .Select(terminal => terminal.GetProperty("kind").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var programImage = project.RootElement.GetProperty("memoryImages")
            .EnumerateArray()
            .Single(image => image.GetProperty("displayName").GetString() == "Program");
        var digestParts = entries
            .Where(entry => entry.Key != "manifest.json")
            .Select(entry => (
                Path: entry.Key,
                Bytes: entry.Value,
                Hash: SHA256.HashData(entry.Value)))
            .ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(projectBytes[^1]).IsEqualTo((byte)'\n');
            await Assert.That(projectBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
                .IsFalse();
            await Assert.That(definitions.Length).IsEqualTo(2);
            await Assert.That(targetKinds).IsEquivalentTo(
                ["circuitDefinition", "libraryContract"],
                CollectionOrdering.Matching);
            await Assert.That(valueKinds).IsEquivalentTo(
                [
                    "enum",
                    "logicVector",
                    "memoryImage",
                    "sliceList",
                    "unsigned32",
                    "unsigned32List",
                    "unsigned64",
                ],
                CollectionOrdering.Matching);
            await Assert.That(routeKinds).IsEquivalentTo(
                ["orthogonal", "unrouted"],
                CollectionOrdering.Matching);
            await Assert.That(terminalKinds).IsEquivalentTo(
                ["definitionPort", "instancePort"],
                CollectionOrdering.Matching);
            await Assert.That(programImage.GetProperty("depth").GetString())
                .IsEqualTo("2");
            await Assert.That(definitions
                    .SelectMany(definition => definition.GetProperty("presentation")
                        .GetProperty("annotations").EnumerateArray())
                    .Single().GetProperty("alignment").GetString())
                .IsEqualTo("center");
            await Assert.That(succeeded.PackageDigest)
                .IsEqualTo(Digest("logiclab-package-v1\0", digestParts));
            await Assert.That(succeeded.ProjectContentDigest)
                .IsEqualTo(Digest("logiclab-project-content-v1\0", digestParts));
            await Assert.That(succeeded.Evidence.ObservedDimensions
                    .Select(observation => observation.Dimension))
                .IsEquivalentTo(
                    Enum.GetValues<PackageDimension>(),
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task WriteAsync_MinimalProject_ProducesCanonicalPackageAndDigests()
    {
        const string displayName = "Canonical\u3000\U0001F600\uE000项目";
        var revision = BeginProject(displayName, "Main");
        await using var destination = new MemoryStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                destination,
                PackagePolicy.Development),
            CancellationToken.None);

        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageWriteSucceeded>())!;
        var entries = ReadEntries(destination);
        var expectedProjectJson = $$$"""
            {"projectId":"{{{revision.Document.ProjectId.Value}}}","displayName":"{{{displayName}}}","symbolProfile":{"id":"TeachingMixed","version":"1.0.0","indicationConvention":"negation"},"libraryReferences":[{"id":"{{{revision.Document.LibrarySnapshot.LibraryId}}}","version":"{{{revision.Document.LibrarySnapshot.Version}}}","digest":"{{{revision.Document.LibrarySnapshot.ContentDigest}}}"}],"entryCircuitDefinitionId":"{{{revision.Document.EntryCircuitDefinitionId.Value}}}","circuitDefinitions":[{"id":"{{{revision.Document.EntryCircuitDefinitionId.Value}}}","displayName":"Main","ports":[],"componentInstances":[],"nets":[],"junctions":[],"wireGeometry":[],"presentation":{"componentPlacements":[],"definitionPortPlacements":[],"annotations":[]}}],"memoryImages":[]}
            """;
        var projectBytes = Encoding.UTF8.GetBytes(expectedProjectJson + "\n");
        var projectHash = SHA256.HashData(projectBytes);
        var expectedPackageDigest = Digest(
            "logiclab-package-v1\0",
            [("project.json", projectBytes, projectHash)]);
        var expectedContentDigest = Digest(
            "logiclab-project-content-v1\0",
            [("project.json", projectBytes, projectHash)]);

        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        var root = manifest.RootElement;
        using (Assert.Multiple())
        {
            await Assert.That(entries.Keys)
                .IsEquivalentTo(
                    ["manifest.json", "project.json"],
                    CollectionOrdering.Matching);
            await Assert.That(entries["project.json"]).IsEquivalentTo(
                projectBytes,
                CollectionOrdering.Matching);
            await Assert.That(root.GetProperty("format").GetString())
                .IsEqualTo("logiclab");
            await Assert.That(root.GetProperty("schemaVersion").GetInt32())
                .IsEqualTo(1);
            await Assert.That(root.GetProperty("projectPart").GetProperty("path").GetString())
                .IsEqualTo("project.json");
            await Assert.That(root.GetProperty("projectPart").GetProperty("length").GetUInt64())
                .IsEqualTo(checked((ulong)projectBytes.Length));
            await Assert.That(root.GetProperty("projectPart").GetProperty("sha256").GetString())
                .IsEqualTo(Convert.ToHexStringLower(projectHash));
            await Assert.That(root.GetProperty("memoryParts").GetArrayLength())
                .IsEqualTo(0);
            await Assert.That(root.GetProperty("packageDigest").GetString())
                .IsEqualTo(expectedPackageDigest);
            await Assert.That(succeeded.SourceProjectRevisionId)
                .IsEqualTo(revision.RevisionId);
            await Assert.That(succeeded.ProjectContentDigest)
                .IsEqualTo(expectedContentDigest);
            await Assert.That(succeeded.PackageDigest)
                .IsEqualTo(expectedPackageDigest);
            await Assert.That(succeeded.CarrierByteCount)
                .IsEqualTo(checked((ulong)destination.Length));
            await Assert.That(destination.CanWrite).IsTrue();
        }
    }

    [Test]
    public async Task WriteAsync_MemoryImage_EncodesCanonicalHeaderPayloadAndManifestPart()
    {
        var revision = BeginProject("Memory project", "Main");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Program",
                3,
                2,
                [
                    new MemoryImageWord(
                        [LogicValue.Zero, LogicValue.One, LogicValue.X]),
                    new MemoryImageWord(
                        [LogicValue.One, LogicValue.Zero, LogicValue.X]),
                ])));
        var image = revision.Document.MemoryImages.Single();
        await using var destination = new MemoryStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                destination,
                PackagePolicy.Development),
            CancellationToken.None);

        _ = (await Assert.That(outcome).IsTypeOf<PackageWriteSucceeded>())!;
        var entries = ReadEntries(destination);
        var memoryPath = $"memory/{image.Id.Value}.bin";
        var expectedMemory = new byte[]
        {
            (byte)'L', (byte)'L', (byte)'M', (byte)'I',
            1, 0,
            1,
            0,
            3, 0, 0, 0,
            2, 0, 0, 0, 0, 0, 0, 0,
            0x64, 0x08,
        };
        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        var memoryPart = manifest.RootElement.GetProperty("memoryParts")[0];

        using (Assert.Multiple())
        {
            await Assert.That(entries.Keys)
                .IsEquivalentTo(
                    ["manifest.json", "project.json", memoryPath],
                    CollectionOrdering.Matching);
            await Assert.That(entries[memoryPath]).IsEquivalentTo(
                expectedMemory,
                CollectionOrdering.Matching);
            await Assert.That(memoryPart.GetProperty("memoryImageId").GetString())
                .IsEqualTo(image.Id.Value);
            await Assert.That(memoryPart.GetProperty("path").GetString())
                .IsEqualTo(memoryPath);
            await Assert.That(memoryPart.GetProperty("length").GetUInt64())
                .IsEqualTo(checked((ulong)expectedMemory.Length));
            await Assert.That(memoryPart.GetProperty("sha256").GetString())
                .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(expectedMemory)));
        }
    }

    [Test]
    public async Task WriteAsync_PackageLimitExceeded_RejectsBeforeWritingCarrier()
    {
        var revision = BeginProject("Limit project", "Main");
        var limits = PackagePolicy.Development.Limits.ToArray();
        limits[(int)PackageDimension.EntityCount] = new PackageLimit(
            PackageDimension.EntityCount,
            1);
        var policy = new PackagePolicy("test-package", "1", limits);
        await using var destination = new MemoryStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(revision, destination, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageWriteRejected>())!;
        var breach = (await Assert.That(rejected.Evidence.PolicyLimitBreach).IsNotNull())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Diagnostics.Select(item => item.Code))
                .IsEquivalentTo(
                    ["package_limit_exceeded"],
                    CollectionOrdering.Matching);
            await Assert.That(breach.Dimension)
                .IsEqualTo(PackageDimension.EntityCount);
            await Assert.That(breach.Observed)
                .IsGreaterThan(1UL);
            await Assert.That(destination.Length).IsEqualTo(0L);
            await Assert.That(destination.CanWrite).IsTrue();
        }
    }

    [Test]
    public async Task WriteAsync_LongStringBeyondPolicy_StopsAtFirstDisallowedScalar()
    {
        var revision = BeginProject(new string('x', 128 * 1_024), "Main");
        var maximum = checked((ulong)(
            "projectId".Length
            + revision.Document.ProjectId.Value.Length
            + "displayName".Length
            + 1));
        var limits = PackagePolicy.Development.Limits.ToArray();
        limits[(int)PackageDimension.StringScalarCount] = new PackageLimit(
            PackageDimension.StringScalarCount,
            maximum);
        var policy = new PackagePolicy("test-package", "1", limits);
        await using var destination = new MemoryStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(revision, destination, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageWriteRejected>())!;
        var breach = (await Assert.That(rejected.Evidence.PolicyLimitBreach).IsNotNull())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(breach.Dimension)
                .IsEqualTo(PackageDimension.StringScalarCount);
            await Assert.That(breach.Observed).IsEqualTo(maximum + 1);
            await Assert.That(destination.Length).IsEqualTo(0L);
        }
    }

    [Test]
    public async Task WriteAsync_PackageLimitExactlyMet_AllowsCarrierPublication()
    {
        var revision = BeginProject("Exact limit project", "Main");
        var limits = PackagePolicy.Development.Limits.ToArray();
        limits[(int)PackageDimension.EntityCount] = new PackageLimit(
            PackageDimension.EntityCount,
            2);
        var policy = new PackagePolicy("test-package", "1", limits);
        await using var destination = new MemoryStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(revision, destination, policy),
            CancellationToken.None);

        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageWriteSucceeded>())!;
        var entityObservation = succeeded.Evidence.ObservedDimensions.Single(
            item => item.Dimension == PackageDimension.EntityCount);
        using (Assert.Multiple())
        {
            await Assert.That(entityObservation.Observed).IsEqualTo(2UL);
            await Assert.That(succeeded.Evidence.PolicyLimitBreach).IsNull();
            await Assert.That(destination.Length).IsGreaterThan(0L);
        }
    }

    [Test]
    public async Task WriteAsync_AlreadyCancelled_RejectsAndLeavesDestinationUntouched()
    {
        var revision = BeginProject("Cancelled project", "Main");
        await using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                destination,
                PackagePolicy.Development),
            cancellation.Token);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageWriteRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_cancelled");
            await Assert.That(rejected.Diagnostics).IsEmpty();
            await Assert.That(destination.Length).IsEqualTo(0L);
            await Assert.That(destination.CanWrite).IsTrue();
        }
    }

    [Test]
    public async Task WriteAsync_AsyncOnlyDestination_NeverUsesSynchronousIo()
    {
        var revision = BeginProject("Async project", "Main");
        await using var destination = new AsyncOnlyWriteStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                destination,
                PackagePolicy.Development),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<PackageWriteSucceeded>();
        using (Assert.Multiple())
        {
            await Assert.That(destination.Length).IsGreaterThan(0L);
            await Assert.That(destination.SynchronousFlushCount).IsEqualTo(0);
            await Assert.That(destination.SynchronousArrayWriteCount).IsEqualTo(0);
            await Assert.That(destination.SynchronousSpanWriteCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task WriteAsync_DestinationFlushFails_PreservesBytesAndClassifiesInfrastructure()
    {
        var revision = BeginProject("Failing destination", "Main");
        await using var destination = new FlushFailingWriteStream();

        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                destination,
                PackagePolicy.Development),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageWriteRejected>())!;
        var carrierBytes = rejected.Evidence.ObservedDimensions.Single(
            item => item.Dimension == PackageDimension.CarrierBytes);
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason)
                .IsEqualTo("package_infrastructure_failure");
            await Assert.That(destination.Length).IsGreaterThan(0L);
            await Assert.That(carrierBytes.Observed)
                .IsEqualTo(checked((ulong)destination.Length));
        }
    }

    private static ProjectRevision Commit(EditOutcome outcome) =>
        ((EditCommitted)outcome).Revision;

    internal static ProjectRevision CreateFullyPopulatedRevision()
    {
        var revision = BeginProject("Complete project", "Main");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Program",
                2,
                2,
                [
                    new MemoryImageWord([LogicValue.Zero, LogicValue.One]),
                    new MemoryImageWord([LogicValue.X, LogicValue.Zero]),
                ])));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Scratch",
                1,
                1,
                [new MemoryImageWord([LogicValue.X])])));
        var imageId = revision.Document.MemoryImages.Single(image =>
            image.DisplayName == "Program").Id;

        revision = Commit(ProjectEditor.Apply(
            revision,
            new CreateCircuitDefinitionIntent(
                "Child",
                [
                    new DefinitionPortDeclaration(
                        "A",
                        PortDirection.Input,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(0, 2),
                            CardinalDirection.West)),
                    new DefinitionPortDeclaration(
                        "Q",
                        PortDirection.Output,
                        1,
                        new DefinitionPortPlacement(
                            new GridPoint(8, 2),
                            CardinalDirection.East)),
                ])));
        var child = revision.Document.CircuitDefinitions.Single(definition =>
            definition.DisplayName == "Child");
        revision = PlaceLibrary(
            revision,
            child.Id,
            "logic.not",
            [new ComponentParameterBinding("width", new Unsigned32ParameterValue(1))],
            "Child NOT");
        child = revision.Document.FindCircuitDefinition(child.Id)!;
        var childNot = child.ComponentInstances.Single();
        var inputPort = child.Ports.Single(port => port.Direction == PortDirection.Input);
        var outputPort = child.Ports.Single(port => port.Direction == PortDirection.Output);
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new DefinitionTerminalReference(child.Id, inputPort.Id),
                    new InstanceTerminalReference(child.Id, childNot.Id, "A"),
                ],
                destinationNetId: null,
                newJunctionPositions: [],
                routeAdditions: [new UnroutedWireRoute()],
                routeReplacements: [])));
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(child.Id, childNot.Id, "Q"),
                    new DefinitionTerminalReference(child.Id, outputPort.Id),
                ])));

        var mainId = revision.Document.EntryCircuitDefinitionId;
        revision = PlaceLibrary(
            revision,
            mainId,
            "source.input",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero, LogicValue.One])),
            ],
            "Source");
        var source = revision.Document.EntryCircuitDefinition.ComponentInstances.Single();
        revision = Commit(ProjectEditor.Apply(
            revision,
            new SetSymbolVariantIntent(
                mainId,
                source.Id,
                SymbolVariantCatalog.RectangularId)));
        revision = PlaceLibrary(
            revision,
            mainId,
            "sink.output",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding("radix", new ChoiceParameterValue("binary")),
            ],
            "Sink");
        var sink = revision.Document.EntryCircuitDefinition.ComponentInstances.Single(
            instance => instance.DisplayName == "Sink");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new ConnectTerminalsIntent(
                [
                    new InstanceTerminalReference(mainId, source.Id, "Q"),
                    new InstanceTerminalReference(mainId, sink.Id, "D"),
                ],
                destinationNetId: null,
                newJunctionPositions: [new GridPoint(4, 1)],
                routeAdditions:
                [
                    new OrthogonalWireRoute(
                        [new GridPoint(0, 0), new GridPoint(4, 0), new GridPoint(4, 2)]),
                ],
                routeReplacements: [])));
        var mainNetId = revision.Document.EntryCircuitDefinition.Nets.Single().Id;
        revision = Commit(ProjectEditor.Apply(
            revision,
            new AddJunctionIntent(
                mainId,
                mainNetId,
                new GridPoint(6, 1),
                [new UnroutedWireRoute()],
                routeReplacements: [],
                routeRemovals: [])));

        revision = PlaceLibrary(
            revision,
            mainId,
            "source.clock",
            [
                new ComponentParameterBinding(
                    "initialValue",
                    new LogicVectorParameterValue([LogicValue.Zero])),
                new ComponentParameterBinding(
                    "firstTransition",
                    new Unsigned64ParameterValue(1)),
                new ComponentParameterBinding(
                    "highDuration",
                    new Unsigned64ParameterValue(2)),
                new ComponentParameterBinding(
                    "lowDuration",
                    new Unsigned64ParameterValue(3)),
            ],
            "Clock");
        revision = PlaceLibrary(
            revision,
            mainId,
            "topology.split",
            [
                new ComponentParameterBinding("width", new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "slices",
                    new SlicesParameterValue([new BitSlice(0, 1), new BitSlice(1, 1)])),
            ],
            "Split");
        revision = PlaceLibrary(
            revision,
            mainId,
            "topology.concat",
            [
                new ComponentParameterBinding(
                    "inputWidths",
                    new WidthsParameterValue([1, 1])),
            ],
            "Concat");
        revision = PlaceLibrary(
            revision,
            mainId,
            "memory.rom",
            [
                new ComponentParameterBinding(
                    "addressWidth",
                    new Unsigned32ParameterValue(1)),
                new ComponentParameterBinding(
                    "wordWidth",
                    new Unsigned32ParameterValue(2)),
                new ComponentParameterBinding(
                    "initialImage",
                    new MemoryImageParameterValue(imageId)),
            ],
            "Memory");
        revision = Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                mainId,
                new CircuitDefinitionComponentTarget(child.Id),
                [],
                new ComponentPlacement(
                    new GridPoint(12, 4),
                    QuarterTurn.Two,
                    Reflected: true),
                "Child call")));
        return Commit(ProjectEditor.Apply(
            revision,
            new CreateAnnotationIntent(
                mainId,
                new AnnotationValue(
                    "Stored annotation",
                    new GridPoint(3, 5),
                    AnnotationAlignment.Center))));
    }

    private static ProjectRevision PlaceLibrary(
        ProjectRevision revision,
        CircuitDefinitionId definitionId,
        string contractId,
        ComponentParameterBinding[] parameters,
        string displayName)
    {
        return Commit(ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                definitionId,
                new ComponentContractKey(CoreLibrarySchema.LibraryId, contractId),
                parameters,
                new ComponentPlacement(
                    new GridPoint(displayName.Length, displayName.Length + 1)),
                displayName)));
    }

    private static string Digest(
        string prefix,
        IReadOnlyList<(string Path, byte[] Bytes, byte[] Hash)> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(prefix));
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        foreach (var part in parts.OrderBy(part => part.Path, StringComparer.Ordinal))
        {
            var pathBytes = Encoding.UTF8.GetBytes(part.Path);
            BinaryPrimitives.WriteUInt32LittleEndian(
                length[..sizeof(uint)],
                checked((uint)pathBytes.Length));
            hash.AppendData(length[..sizeof(uint)]);
            hash.AppendData(pathBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(
                length,
                checked((ulong)part.Bytes.Length));
            hash.AppendData(length);
            hash.AppendData(part.Hash);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed class AsyncOnlyWriteStream : Stream
    {
        private readonly MemoryStream inner = new();

        public int SynchronousFlushCount { get; private set; }

        public int SynchronousArrayWriteCount { get; private set; }

        public int SynchronousSpanWriteCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            SynchronousFlushCount++;
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            SynchronousArrayWriteCount++;
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            SynchronousSpanWriteCount++;
            inner.Write(buffer);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class FlushFailingWriteStream : MemoryStream
    {
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Destination flush failed."));
    }
}
