using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
using LogicLab.Conformance;
using LogicLab.Domain.Authoring;

if (args is ["schema"])
{
    var schemas = LibrarySnapshot.Core.Contracts.Select(contract => new ContractSchemaEvidence(contract.Key.ContractId, contract.SchemaDigest)).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(schemas, EvidenceJsonContext.Default.ContractSchemaEvidenceArray));
    return 0;
}

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: LogicLab.Conformance <manifest.json> <evidence.json> <TRX-directory> <verified-manifest.json>");
    Console.Error.WriteLine("   or: LogicLab.Conformance corpus <corpus.json> <TRX-directory> <verified-corpus.json>");
    return 2;
}
try
{
    var results = ReadReports(args[2]);
    if (args[0] == "corpus")
    {
        var corpus = ReadJson(args[1], EvidenceJsonContext.Default.QualificationCorpus);
        if (ReportErrors(QualificationCorpusVerifier.Verify(corpus, results)))
        {
            return 1;
        }
        await WriteVerified(corpus, EvidenceJsonContext.Default.QualificationCorpus, args[3]);
        Console.WriteLine($"Verified {corpus.Cases.Length} qualification corpus cases against {results.Count} passed test cases.");
        return 0;
    }
    var manifest = ReadJson(args[0], EvidenceJsonContext.Default.ContractEvidenceArray);
    var registry = ReadJson(args[1], EvidenceJsonContext.Default.EvidenceDefinitionArray);
    if (ReportErrors(ComponentEvidenceVerifier.Verify(manifest, registry, results)))
    {
        return 1;
    }
    await WriteVerified(manifest, EvidenceJsonContext.Default.ContractEvidenceArray, args[3]);
    Console.WriteLine($"Verified {manifest.Length} component contracts against {results.Count} passed test cases.");
    return 0;
}
catch (Exception exception) when (exception is JsonException or XmlException or IOException or ArgumentException)
{
    Console.Error.WriteLine($"Evidence input rejected: {exception.Message}");
    return 1;
}

static IReadOnlyList<TestEvidence> ReadReports(string directory)
{
    var results = new List<TestEvidence>();
    var assemblies = new HashSet<string>(StringComparer.Ordinal);
    foreach (var path in Directory.EnumerateFiles(directory, "*.trx").Order(StringComparer.Ordinal))
    {
        using var stream = File.OpenRead(path);
        var report = TrxEvidenceReader.Read(stream);
        var reportAssemblies = report.Select(result => result.Assembly).Distinct(StringComparer.Ordinal).ToArray();
        if (reportAssemblies.Length != 1 || !assemblies.Add(reportAssemblies[0]))
        {
            throw new InvalidDataException("Each test assembly must have exactly one report.");
        }
        results.AddRange(report);
    }
    return results.AsReadOnly();
}

static bool ReportErrors(IReadOnlyList<EvidenceError> errors)
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine($"{error.Code}: {error.Item}");
    }
    return errors.Count != 0;
}

static async Task WriteVerified<T>(T value, JsonTypeInfo<T> typeInfo, string path)
{
    var output = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
        File.Move(temporary, output, overwrite: true);
    }
    finally
    {
        File.Delete(temporary);
    }
}

static T ReadJson<T>(string path, JsonTypeInfo<T> typeInfo)
{
    using var stream = File.OpenRead(path);
    return JsonSerializer.Deserialize(stream, typeInfo)
        ?? throw new JsonException("The evidence document cannot be null.");
}
