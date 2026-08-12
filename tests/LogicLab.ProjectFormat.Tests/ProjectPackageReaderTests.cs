using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LogicLab.Domain;
using LogicLab.Domain.Authoring;
using TUnit.Assertions.Enums;
using static LogicLab.ProjectFormat.Tests.ProjectPackageTestFixture;

namespace LogicLab.ProjectFormat.Tests;

internal sealed class ProjectPackageReaderTests
{
    [Test]
    public async Task ReadAsync_WriterOutput_RoundTripsProjectAndDigestsWithoutClosingSource()
    {
        var revision = BeginProject("Round trip", "Main");
        await using var carrier = await WriteAsync(revision);
        var written = (PackageWriteSucceeded)carrier.Outcome;

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(carrier.Stream, PackagePolicy.Development),
            CancellationToken.None);

        ThrowIfRejected(outcome);
        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageReadSucceeded>())!;
        var genesis = (ProjectGenesisCommitted)ProjectEditor.Begin(
            new ImportedProjectSeed(succeeded.ImportCandidate));
        using (Assert.Multiple())
        {
            await Assert.That(genesis.Revision.Document.DisplayName)
                .IsEqualTo("Round trip");
            await Assert.That(genesis.Revision.Document.ProjectId)
                .IsEqualTo(revision.Document.ProjectId);
            await Assert.That(succeeded.ProjectContentDigest)
                .IsEqualTo(written.ProjectContentDigest);
            await Assert.That(succeeded.PackageDigest)
                .IsEqualTo(written.PackageDigest);
            await Assert.That(carrier.Stream.CanRead).IsTrue();
        }
    }

    [Test]
    public async Task ReadAsync_NonSeekableCarrierBeyondPolicy_RejectsObservedActualBytes()
    {
        var revision = BeginProject("Bounded spool", "Main");
        await using var carrier = await WriteAsync(revision);
        var bytes = carrier.Stream.ToArray();
        await using var source = new NonSeekableReadStream(bytes);
        var policy = WithLimit(
            PackageDimension.CarrierBytes,
            checked((ulong)bytes.Length - 1));

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(source, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.CarrierBytes);
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Observed)
                .IsEqualTo(checked((ulong)bytes.Length));
            await Assert.That(source.CanRead).IsTrue();
        }
    }

    [Test]
    public async Task ReadAsync_CarrierExactlyAtPolicyLimit_Succeeds()
    {
        var revision = BeginProject("Exact carrier boundary", "Main");
        await using var carrier = await WriteAsync(revision);
        var policy = WithLimit(
            PackageDimension.CarrierBytes,
            checked((ulong)carrier.Stream.Length));

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(carrier.Stream, policy),
            CancellationToken.None);

        await Assert.That(outcome).IsTypeOf<PackageReadSucceeded>();
    }

    [Test]
    public async Task ReadAsync_Zip64DeclaredEntryCountBeyondPolicy_RejectsBeforeMaterialization()
    {
        var revision = BeginProject("ZIP64 entry count", "Main");
        await using var carrier = await WriteAsync(revision);
        var bytes = carrier.Stream.ToArray();
        var endRecordIndex = bytes.AsSpan().LastIndexOf("PK\x05\x06"u8);
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(endRecordIndex + 16));
        const ulong declaredEntryCount = 10_000;
        var zip64EndRecord = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(zip64EndRecord, 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64EndRecord.AsSpan(4), 44);
        BinaryPrimitives.WriteUInt64LittleEndian(
            zip64EndRecord.AsSpan(24),
            declaredEntryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(
            zip64EndRecord.AsSpan(32),
            declaredEntryCount);
        BinaryPrimitives.WriteUInt64LittleEndian(
            zip64EndRecord.AsSpan(40),
            checked((ulong)(endRecordIndex - centralDirectoryOffset)));
        BinaryPrimitives.WriteUInt64LittleEndian(
            zip64EndRecord.AsSpan(48),
            centralDirectoryOffset);
        var locator = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(
            locator.AsSpan(8),
            checked((ulong)endRecordIndex));
        BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(16), 1);
        var endRecord = bytes.AsSpan(endRecordIndex).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(endRecord.AsSpan(8), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(endRecord.AsSpan(10), ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(endRecord.AsSpan(16), uint.MaxValue);
        await using var zip64 = new MemoryStream([
            .. bytes.AsSpan(0, endRecordIndex),
            .. zip64EndRecord,
            .. locator,
            .. endRecord,
        ]);
        var policy = WithLimit(PackageDimension.EntryCount, 3);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(zip64, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.EntryCount);
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Observed)
                .IsEqualTo(declaredEntryCount);
        }
    }

    [Test]
    public async Task ReadAsync_ManifestMemoryCountBeyondPolicy_RejectsBeforePartIntegrity()
    {
        var revision = BeginProject("Memory count", "Main");
        revision = AddSingleCellMemory(revision, "First");
        revision = AddSingleCellMemory(revision, "Second");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var secondMemoryPath = entries.Keys
            .Where(path => path.StartsWith("memory/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Last();
        entries[secondMemoryPath][^1] = 1;
        await using var tampered = WriteEntries(entries);
        var policy = WithLimit(PackageDimension.MemoryPartCount, 1);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(tampered, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.MemoryPartCount);
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Observed)
                .IsEqualTo(2UL);
        }
    }

    [Test]
    public async Task ReadAsync_MemoryCellsBeyondPolicy_RejectsBeforePartIntegrity()
    {
        var revision = BeginProject("Memory cells", "Main");
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                "Program",
                2,
                1,
                [new MemoryImageWord([LogicValue.Zero, LogicValue.One])]))).Revision;
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var memoryPath = entries.Keys.Single(path => path.StartsWith(
            "memory/",
            StringComparison.Ordinal));
        entries[memoryPath][^1] ^= 1;
        await using var tampered = WriteEntries(entries);
        var policy = WithLimit(PackageDimension.MemoryCellCount, 1);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(tampered, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.MemoryCellCount);
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Observed)
                .IsEqualTo(2UL);
        }
    }

    [Test]
    [Arguments("wordWidth")]
    [Arguments("depth")]
    public async Task ReadAsync_ZeroMemoryDimension_RejectsShapeBeforeBodyIntegrity(
        string dimension)
    {
        var revision = AddSingleCellMemory(
            BeginProject("Memory shape", "Main"),
            "Program");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var project = JsonNode.Parse(entries["project.json"])!.AsObject();
        project["memoryImages"]!.AsArray()[0]![dimension] = dimension == "wordWidth"
            ? JsonValue.Create(0)
            : JsonValue.Create("0");
        entries["project.json"] = Encoding.UTF8.GetBytes(project.ToJsonString());
        var manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
        SetPartIntegrity(manifest["projectPart"]!.AsObject(), entries["project.json"]);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        var memoryPath = entries.Keys.Single(path => path.StartsWith(
            "memory/",
            StringComparison.Ordinal));
        entries[memoryPath][^1] ^= 1;
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("package_memory_invalid");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("rule", "shape"));
        }
    }

    [Test]
    public async Task ReadAsync_OversizedMemoryPart_PrecedesInvalidProjectJson()
    {
        var revision = AddSingleCellMemory(
            BeginProject("Actual byte precedence", "Main"),
            "Program");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var memoryPath = entries.Keys.Single(path => path.StartsWith(
            "memory/",
            StringComparison.Ordinal));
        entries["project.json"] = "{"u8.ToArray();
        entries[memoryPath] = new byte[4 * 1_024];
        RefreshIntegrity(entries);
        await using var tampered = WriteEntries(entries);
        var policy = WithLimit(PackageDimension.PartBytes, 1_024);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(tampered, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.PartBytes);
        }
    }

    [Test]
    public async Task ReadAsync_Deflate64Entry_RejectsZipProfileBeforeManifestAgreement()
    {
        var revision = BeginProject("Compression profile", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        await using var withExtraEntry = WriteEntries(
            entries,
            ("memory/extra.bin", Array.Empty<byte>()));
        await using var deflate64 = PatchCompressionMethods(
            withExtraEntry,
            "memory/extra.bin",
            centralMethod: 9,
            localMethod: 9);

        var outcome = await ReadAsync(deflate64);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code)
                .IsEqualTo("package_unsupported_feature");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("feature", "compression"));
        }
    }

    [Test]
    public async Task ReadAsync_LocalCompressionMethodOutsideProfile_RejectsCarrier()
    {
        var revision = BeginProject("Local compression profile", "Main");
        await using var carrier = await WriteAsync(revision);
        await using var unsupported = PatchCompressionMethods(
            carrier.Stream,
            "project.json",
            centralMethod: 8,
            localMethod: 9);

        var outcome = await ReadAsync(unsupported);

        await AssertDiagnostic(outcome, "package_unsupported_feature");
    }

    [Test]
    public async Task ReadAsync_LocalEncryptionFlag_RejectsEncryptedEntry()
    {
        var revision = BeginProject("Local encryption flag", "Main");
        await using var carrier = await WriteAsync(revision);
        await using var encrypted = PatchLocalGeneralPurposeFlag(
            carrier.Stream,
            "project.json",
            0x0001);

        var outcome = await ReadAsync(encrypted);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_invalid");
            await Assert.That(diagnostic.Code)
                .IsEqualTo("package_unsupported_feature");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("feature", "encryption"));
        }
    }

    [Test]
    public async Task ReadAsync_NonzeroDiskMetadata_RejectsSplitArchive()
    {
        var revision = BeginProject("Split archive metadata", "Main");
        await using var carrier = await WriteAsync(revision);
        await using var split = PatchDiskNumbers(carrier.Stream, 1);

        var outcome = await ReadAsync(split);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_invalid");
            await Assert.That(diagnostic.Code).IsEqualTo("package_illegal_entry");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("rule", "carrier"));
        }
    }

    [Test]
    public async Task ReadAsync_CorruptDeflateEntry_RejectsCarrierInsteadOfCompressionProfile()
    {
        var revision = BeginProject("Corrupt Deflate", "Main");
        await using var carrier = await WriteAsync(revision);
        await using var corrupt = CorruptCompressedEntry(carrier.Stream, "project.json");

        var outcome = await ReadAsync(corrupt);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("package_illegal_entry");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("rule", "corruptPart"));
        }
    }

    [Test]
    public async Task ReadAsync_DuplicateManifestMember_RejectsStrictJson()
    {
        var revision = BeginProject("Strict JSON", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var manifest = Encoding.UTF8.GetString(entries["manifest.json"])
            .Replace(
                "{\"format\":\"logiclab\"",
                "{\"format\":\"logiclab\",\"format\":\"logiclab\"",
                StringComparison.Ordinal);
        await using var tampered = WriteEntries(
            entries,
            ("manifest.json", Encoding.UTF8.GetBytes(manifest)));

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(tampered, PackagePolicy.Development),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_invalid");
            await Assert.That(rejected.Diagnostics.Select(item => item.Code))
                .Contains("package_json_invalid");
        }
    }

    [Test]
    [Arguments("manifest.json")]
    [Arguments("project.json")]
    public async Task ReadAsync_NullRecordArrayElement_RejectsSchema(string partPath)
    {
        var revision = BeginProject("Null array element", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var document = JsonNode.Parse(entries[partPath])!.AsObject();
        var arrayName = partPath == "manifest.json"
            ? "memoryParts"
            : "circuitDefinitions";
        document[arrayName]!.AsArray().Add(null);
        entries[partPath] = Encoding.UTF8.GetBytes(document.ToJsonString());
        if (partPath == "project.json")
        {
            RefreshIntegrity(entries);
        }

        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_invalid");
            await Assert.That(rejected.Diagnostics.Select(item => item.Code))
                .Contains("package_json_invalid");
        }
    }

    [Test]
    public async Task ReadAsync_StringLimitPrecedesFullEscapeDecoding()
    {
        var revision = BeginProject("Bounded string", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        entries["manifest.json"] = "{\"aaaaa\\uD800\":0}"u8.ToArray();
        await using var tampered = WriteEntries(entries);
        var policy = WithLimit(PackageDimension.StringUtf8Bytes, 4);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(tampered, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.StringUtf8Bytes);
        }
    }

    [Test]
    public async Task ReadAsync_ProjectPartHashMismatch_RejectsIntegrityBeforeDomainConstruction()
    {
        var revision = BeginProject("Integrity", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var project = Encoding.UTF8.GetString(entries["project.json"])
            .Replace("Integrity", "Xntegrity", StringComparison.Ordinal);
        await using var tampered = WriteEntries(
            entries,
            ("project.json", Encoding.UTF8.GetBytes(project)));

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(tampered, PackagePolicy.Development),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_invalid");
            await Assert.That(diagnostic.Code).IsEqualTo("package_integrity_mismatch");
            await Assert.That(diagnostic.Arguments)
                .IsEquivalentTo(
                    [
                        new PackageDiagnosticArgument("partKind", "project"),
                        new PackageDiagnosticArgument("check", "sha256"),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task ReadAsync_MemoryImage_RestoresCanonicalWords()
    {
        var revision = BeginProject("Memory", "Main");
        revision = ((EditCommitted)ProjectEditor.Apply(
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
                ]))).Revision;
        await using var carrier = await WriteAsync(revision);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(carrier.Stream, PackagePolicy.Development),
            CancellationToken.None);

        ThrowIfRejected(outcome);
        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageReadSucceeded>())!;
        var imported = (ProjectGenesisCommitted)ProjectEditor.Begin(
            new ImportedProjectSeed(succeeded.ImportCandidate));
        var image = imported.Revision.Document.MemoryImages.Single();
        using (Assert.Multiple())
        {
            await Assert.That(image.Id)
                .IsEqualTo(revision.Document.MemoryImages.Single().Id);
            await Assert.That(image.Width).IsEqualTo(3U);
            await Assert.That(image.Depth).IsEqualTo(2U);
            await Assert.That(image.Words[0].Values)
                .IsEquivalentTo(
                    [LogicValue.Zero, LogicValue.One, LogicValue.X],
                    CollectionOrdering.Matching);
            await Assert.That(image.Words[1].Values)
                .IsEquivalentTo(
                    [LogicValue.One, LogicValue.Zero, LogicValue.X],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task ReadAsync_UnknownProjectMember_RejectsClosedDtoSchema()
    {
        var revision = BeginProject("Unknown member", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var project = Encoding.UTF8.GetString(entries["project.json"]);
        entries["project.json"] = Encoding.UTF8.GetBytes(
            project.Insert(project.IndexOf('{') + 1, "\"unexpected\":0,"));
        RefreshIntegrity(entries);
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        await AssertDiagnostic(outcome, "package_unknown_member");
    }

    [Test]
    public async Task ReadAsync_UnknownTargetDiscriminator_RejectsClosedUnion()
    {
        var revision = BeginProject("Unknown discriminator", "Main");
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new LogicLab.Domain.Components.ComponentContractKey(
                    LogicLab.Domain.Components.CoreLibrarySchema.LibraryId,
                    "logic.not"),
                [new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0))))).Revision;
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        entries["project.json"] = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(entries["project.json"])
                .Replace(
                    "\"kind\":\"libraryContract\"",
                    "\"kind\":\"unsupported\"",
                    StringComparison.Ordinal));
        RefreshIntegrity(entries);
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        await AssertDiagnostic(outcome, "package_unknown_discriminator");
    }

    [Test]
    public async Task ReadAsync_TargetDiscriminatorAfterDataMember_AcceptsLegalMemberOrder()
    {
        var revision = BeginProject("Reordered discriminator", "Main");
        revision = ((EditCommitted)ProjectEditor.Apply(
            revision,
            new PlaceComponentInstanceIntent(
                revision.Document.EntryCircuitDefinitionId,
                new LogicLab.Domain.Components.ComponentContractKey(
                    LogicLab.Domain.Components.CoreLibrarySchema.LibraryId,
                    "logic.not"),
                [new ComponentParameterBinding(
                    "width",
                    new Unsigned32ParameterValue(1))],
                new ComponentPlacement(new GridPoint(0, 0))))).Revision;
        await using var carrier = await WriteAsync(revision);
        var written = (PackageWriteSucceeded)carrier.Outcome;
        var entries = ReadEntries(carrier.Stream);
        entries["project.json"] = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(entries["project.json"])
                .Replace(
                    "\"kind\":\"libraryContract\",\"libraryId\":\"logiclab.core\"",
                    "\"libraryId\":\"logiclab.core\",\"kind\":\"libraryContract\"",
                    StringComparison.Ordinal));
        RefreshIntegrity(entries);
        await using var reordered = WriteEntries(entries);

        var outcome = await ReadAsync(reordered);

        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageReadSucceeded>())!;
        await Assert.That(succeeded.ProjectContentDigest)
            .IsEqualTo(written.ProjectContentDigest);
    }

    [Test]
    public async Task ReadAsync_EntityLimitBelowMinimalProject_RejectsProjectRoot()
    {
        var revision = BeginProject("Entity limit", "Main");
        await using var carrier = await WriteAsync(revision);
        var policy = WithLimit(PackageDimension.EntityCount, 1);

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(carrier.Stream, policy),
            CancellationToken.None);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_limit_exceeded");
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Dimension)
                .IsEqualTo(PackageDimension.EntityCount);
            await Assert.That(rejected.Evidence.PolicyLimitBreach?.Observed)
                .IsEqualTo(2UL);
        }
    }

    [Test]
    public async Task ReadAsync_UnsupportedSchemaVersion_RejectsManifest()
    {
        var revision = BeginProject("Unsupported schema", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(entries["manifest.json"])
                .Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        await AssertDiagnostic(outcome, "package_schema_version_unsupported");
    }

    [Test]
    public async Task ReadAsync_TraversalEntry_RejectsBeforeManifestAgreement()
    {
        var revision = BeginProject("Traversal", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        await using var tampered = WriteEntries(
            entries,
            ("../outside", Array.Empty<byte>()));

        var outcome = await ReadAsync(tampered);

        await AssertDiagnostic(outcome, "package_illegal_entry");
    }

    [Test]
    public async Task ReadAsync_MemoryTailFieldsNonZero_RejectsCanonicalEncoding()
    {
        var revision = BeginProject("Tail fields", "Main");
        revision = ((EditCommitted)ProjectEditor.Apply(
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
                ]))).Revision;
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var memoryPath = entries.Keys.Single(path => path.StartsWith(
            "memory/",
            StringComparison.Ordinal));
        entries[memoryPath][^1] |= 0b1100_0000;
        RefreshIntegrity(entries);
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("package_memory_invalid");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("rule", "tailFields"));
        }
    }

    [Test]
    public async Task ReadAsync_ManifestMemoryWithoutProjectReference_RejectsAgreement()
    {
        var revision = BeginProject("Memory agreement", "Main");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        const string memoryPath = "memory/extra.bin";
        entries[memoryPath] = CreateSingleCellMemoryPart();
        var manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
        manifest["memoryParts"]!.AsArray().Add(new JsonObject
        {
            ["memoryImageId"] = "extra",
            ["path"] = memoryPath,
            ["length"] = 0,
            ["sha256"] = new string('0', 64),
        });
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        RefreshIntegrity(entries);
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("package_integrity_mismatch");
            await Assert.That(diagnostic.Arguments)
                .IsEquivalentTo(
                    [
                        new PackageDiagnosticArgument("partKind", "memory"),
                        new PackageDiagnosticArgument("check", "agreement"),
                    ],
                    CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task ReadAsync_UnsortedManifestMemoryParts_RejectsCanonicalOrder()
    {
        var revision = BeginProject("Memory order", "Main");
        revision = AddSingleCellMemory(revision, "First");
        revision = AddSingleCellMemory(revision, "Second");
        await using var carrier = await WriteAsync(revision);
        var entries = ReadEntries(carrier.Stream);
        var manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
        var memoryParts = manifest["memoryParts"]!.AsArray();
        var first = memoryParts[0]!.DeepClone();
        var second = memoryParts[1]!.DeepClone();
        memoryParts.Clear();
        memoryParts.Add(second);
        memoryParts.Add(first);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        await using var tampered = WriteEntries(entries);

        var outcome = await ReadAsync(tampered);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        var diagnostic = rejected.Diagnostics.Single();
        using (Assert.Multiple())
        {
            await Assert.That(diagnostic.Code).IsEqualTo("package_json_invalid");
            await Assert.That(diagnostic.Arguments)
                .Contains(new PackageDiagnosticArgument("rule", "memoryPartOrder"));
        }
    }

    [Test]
    public async Task ReadAsync_NonCanonicalWhitespace_NormalizesProjectContentDigest()
    {
        var revision = BeginProject("Whitespace", "Main");
        await using var carrier = await WriteAsync(revision);
        var written = (PackageWriteSucceeded)carrier.Outcome;
        var entries = ReadEntries(carrier.Stream);
        entries["project.json"] = [
            .. " \n\t"u8,
            .. entries["project.json"],
            .. " \r\n"u8,
        ];
        RefreshIntegrity(entries);
        await using var nonCanonical = WriteEntries(entries);

        var outcome = await ReadAsync(nonCanonical);

        var succeeded = (await Assert.That(outcome).IsTypeOf<PackageReadSucceeded>())!;
        using (Assert.Multiple())
        {
            await Assert.That(succeeded.ProjectContentDigest)
                .IsEqualTo(written.ProjectContentDigest);
            await Assert.That(succeeded.PackageDigest)
                .IsNotEqualTo(written.PackageDigest);
        }
    }

    [Test]
    public async Task ReadAsync_PreCancelled_ReturnsClosedOutcomeWithoutClosingSource()
    {
        await using var source = new MemoryStream([1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(source, PackagePolicy.Development),
            cancellation.Token);

        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_cancelled");
            await Assert.That(rejected.Diagnostics).IsEmpty();
            await Assert.That(source.CanRead).IsTrue();
        }
    }

    [Test]
    public async Task ReadAsync_ImportExportImport_PreservesCanonicalProjectMeaning()
    {
        var original = BeginProject("Semantic round trip", "Main");
        await using var firstCarrier = await WriteAsync(original);
        var firstRead = (PackageReadSucceeded)await ReadAsync(firstCarrier.Stream);
        var imported = (ProjectGenesisCommitted)ProjectEditor.Begin(
            new ImportedProjectSeed(firstRead.ImportCandidate));
        await using var secondCarrier = await WriteAsync(imported.Revision);

        var secondRead = (PackageReadSucceeded)await ReadAsync(secondCarrier.Stream);
        var firstEntries = ReadEntries(firstCarrier.Stream);
        var secondEntries = ReadEntries(secondCarrier.Stream);
        using (Assert.Multiple())
        {
            await Assert.That(secondRead.ProjectContentDigest)
                .IsEqualTo(firstRead.ProjectContentDigest);
            await Assert.That(secondEntries["project.json"])
                .IsEquivalentTo(
                    firstEntries["project.json"],
                    CollectionOrdering.Matching);
            await Assert.That(imported.Revision.RevisionId)
                .IsNotEqualTo(original.RevisionId);
        }
    }

    private static ProjectRevision AddSingleCellMemory(
        ProjectRevision revision,
        string displayName)
    {
        return ((EditCommitted)ProjectEditor.Apply(
            revision,
            new CreateMemoryImageIntent(
                displayName,
                1,
                1,
                [new MemoryImageWord([LogicValue.Zero])]))).Revision;
    }

    private static byte[] CreateSingleCellMemoryPart()
    {
        var bytes = new byte[21];
        "LLMI"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        bytes[6] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12, 8), 1);
        return bytes;
    }

    private static async Task<WrittenCarrier> WriteAsync(ProjectRevision revision)
    {
        var stream = new MemoryStream();
        var outcome = await ProjectPackage.WriteAsync(
            new ProjectPackageWriteRequest(
                revision,
                stream,
                PackagePolicy.Development),
            CancellationToken.None);
        stream.Position = 0;
        return new WrittenCarrier(stream, outcome);
    }

    private static Task<PackageReadOutcome> ReadAsync(Stream source)
    {
        source.Position = 0;
        return ProjectPackage.ReadAsync(
            new ProjectPackageReadRequest(source, PackagePolicy.Development),
            CancellationToken.None);
    }

    private static async Task AssertDiagnostic(
        PackageReadOutcome outcome,
        string expectedCode)
    {
        var rejected = (await Assert.That(outcome).IsTypeOf<PackageReadRejected>())!;
        using (Assert.Multiple())
        {
            await Assert.That(rejected.Reason).IsEqualTo("package_invalid");
            await Assert.That(rejected.Diagnostics.Select(item => item.Code))
                .Contains(expectedCode);
        }
    }

    private static PackagePolicy WithLimit(
        PackageDimension dimension,
        ulong maximum)
    {
        var limits = PackagePolicy.Development.Limits.ToArray();
        limits[(int)dimension] = new PackageLimit(dimension, maximum);
        return new PackagePolicy("test-package", "1", limits);
    }

    private static void ThrowIfRejected(PackageReadOutcome outcome)
    {
        if (outcome is PackageReadRejected rejected)
        {
            throw new InvalidOperationException(
                $"{rejected.Reason}: {string.Join(',', rejected.Diagnostics.Select(item => item.Code + '[' + string.Join(',', item.Arguments.Select(argument => argument.Name + '=' + argument.Value)) + ']'))}");
        }
    }

    private static MemoryStream WriteEntries(
        IReadOnlyDictionary<string, byte[]> entries,
        params (string Path, byte[] Bytes)[] replacements)
    {
        var replacementMap = replacements.ToDictionary(
            item => item.Path,
            item => item.Bytes,
            StringComparer.Ordinal);
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in entries)
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.SmallestSize);
                using var destination = entry.Open();
                destination.Write(replacementMap.Remove(pair.Key, out var replacement)
                    ? replacement
                    : pair.Value);
            }

            foreach (var replacement in replacementMap)
            {
                var entry = archive.CreateEntry(
                    replacement.Key,
                    CompressionLevel.SmallestSize);
                using var destination = entry.Open();
                destination.Write(replacement.Value);
            }
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream PatchCompressionMethods(
        MemoryStream package,
        string path,
        ushort centralMethod,
        ushort localMethod)
    {
        var bytes = package.ToArray();
        var centralOffset = FindCentralDirectoryEntry(bytes, path);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(centralOffset + 10),
            centralMethod);
        var localOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(centralOffset + 42)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(localOffset + 8),
            localMethod);
        return new MemoryStream(bytes);
    }

    private static MemoryStream PatchLocalGeneralPurposeFlag(
        MemoryStream package,
        string path,
        ushort flag)
    {
        var bytes = package.ToArray();
        var centralOffset = FindCentralDirectoryEntry(bytes, path);
        var localOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(centralOffset + 42)));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(localOffset + 6));
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(localOffset + 6),
            checked((ushort)(flags | flag)));
        return new MemoryStream(bytes);
    }

    private static MemoryStream PatchDiskNumbers(
        MemoryStream package,
        ushort diskNumber)
    {
        var bytes = package.ToArray();
        foreach (var path in new[] { "manifest.json", "project.json" })
        {
            var centralOffset = FindCentralDirectoryEntry(bytes, path);
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(centralOffset + 34),
                diskNumber);
        }

        var endRecordOffset = bytes.AsSpan().LastIndexOf("PK\x05\x06"u8);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(endRecordOffset + 4),
            diskNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(endRecordOffset + 6),
            diskNumber);
        return new MemoryStream(bytes);
    }

    private static MemoryStream CorruptCompressedEntry(
        MemoryStream package,
        string path)
    {
        var bytes = package.ToArray();
        var centralOffset = FindCentralDirectoryEntry(bytes, path);
        var localOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(centralOffset + 42)));
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(localOffset + 26));
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(localOffset + 28));
        var dataOffset = checked(localOffset + 30 + nameLength + extraLength);
        bytes[dataOffset] = 0xff;
        return new MemoryStream(bytes);
    }

    private static int FindCentralDirectoryEntry(byte[] bytes, string path)
    {
        var pathBytes = Encoding.UTF8.GetBytes(path);
        for (var offset = 0; offset <= bytes.Length - 46; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) !=
                0x02014b50)
            {
                continue;
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(offset + 28));
            if (nameLength == pathBytes.Length
                && bytes.AsSpan(offset + 46, nameLength).SequenceEqual(pathBytes))
            {
                return offset;
            }
        }

        throw new InvalidOperationException($"ZIP entry '{path}' was not found.");
    }

    private static void RefreshIntegrity(Dictionary<string, byte[]> entries)
    {
        var manifest = JsonNode.Parse(entries["manifest.json"])!.AsObject();
        var projectPart = manifest["projectPart"]!.AsObject();
        SetPartIntegrity(projectPart, entries["project.json"]);
        foreach (var memoryPartNode in manifest["memoryParts"]!.AsArray())
        {
            var memoryPart = memoryPartNode!.AsObject();
            var path = memoryPart["path"]!.GetValue<string>();
            SetPartIntegrity(memoryPart, entries[path]);
        }

        manifest["packageDigest"] = ComputePackageDigest(entries);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.ToJsonString());
    }

    private static void SetPartIntegrity(JsonObject part, byte[] bytes)
    {
        part["length"] = bytes.Length;
        part["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string ComputePackageDigest(
        IReadOnlyDictionary<string, byte[]> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("logiclab-package-v1\0"u8);
        foreach (var part in entries
                     .Where(pair => pair.Key != "manifest.json")
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var path = Encoding.UTF8.GetBytes(part.Key);
            var pathLength = new byte[4];
            var contentLength = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(
                pathLength,
                checked((uint)path.Length));
            BinaryPrimitives.WriteUInt64LittleEndian(
                contentLength,
                checked((ulong)part.Value.Length));
            hash.AppendData(pathLength);
            hash.AppendData(path);
            hash.AppendData(contentLength);
            hash.AppendData(SHA256.HashData(part.Value));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed record WrittenCarrier(
        MemoryStream Stream,
        PackageWriteOutcome Outcome) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Stream.DisposeAsync();
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
