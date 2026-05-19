using AwesomeAssertions;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Packet.Sdl.CodeGen.Tests;

/// <summary>
/// Validates each committed `*.citations.yaml` sidecar against
/// `spec-sdl/schema/sdl-citations.schema.json`. Pairs with the
/// schema landed in m0lte/ax25sdl#10; the schema is only useful if
/// the committed sidecars actually conform to it, so the tests are
/// the structural contract.
/// </summary>
public class CitationsSchemaTests
{
    [Fact]
    public void Schema_parses_as_a_well_formed_2020_12_document()
    {
        var schemaText = File.ReadAllText(LocateSchema());
        Action parse = () => JsonSchema.FromText(schemaText);
        parse.Should().NotThrow();

        var schema = JsonSchema.FromText(schemaText);
        schema.Should().NotBeNull();
    }

    public static IEnumerable<object[]> CitationsFiles()
    {
        var dir = LocateCitationsDir();
        foreach (var file in Directory.EnumerateFiles(dir, "*.citations.yaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return [Path.GetFileName(file)];
        }
    }

    [Theory]
    [MemberData(nameof(CitationsFiles))]
    public void Each_committed_citations_file_validates_against_the_schema(string filename)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(LocateSchema()))!;
        var path = Path.Combine(LocateCitationsDir(), filename);
        var doc = YamlFileToJsonNode(path);

        var eval = schema.Evaluate(doc, new EvaluationOptions { OutputFormat = OutputFormat.List });
        eval.IsValid.Should().BeTrue($"{filename} failed schema validation: {DescribeFailure(eval)}");
    }

    [Fact]
    public void Schema_rejects_a_pinned_refs_entry_missing_required_commit()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(LocateSchema()))!;
        var doc = JsonNode.Parse("""
            {
              "pinned_refs": {
                "linbpq": { "repo": "https://example.invalid/x" }
              }
            }
            """)!;
        var eval = schema.Evaluate(doc, new EvaluationOptions { OutputFormat = OutputFormat.List });
        eval.IsValid.Should().BeFalse("schema should require pinned_refs.<source>.commit");
    }

    [Fact]
    public void Schema_rejects_a_references_entry_missing_required_source()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(LocateSchema()))!;
        var doc = JsonNode.Parse("""
            {
              "transitions": {
                "t01_example": {
                  "references": [
                    { "path": "file.c", "function": "foo" }
                  ]
                }
              }
            }
            """)!;
        var eval = schema.Evaluate(doc, new EvaluationOptions { OutputFormat = OutputFormat.List });
        eval.IsValid.Should().BeFalse("schema should require references[].source");
    }

    [Fact]
    public void Schema_rejects_transition_id_not_matching_canonical_pattern()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(LocateSchema()))!;
        var doc = JsonNode.Parse("""
            {
              "transitions": {
                "NotATransitionId": {
                  "notes": "x"
                }
              }
            }
            """)!;
        var eval = schema.Evaluate(doc, new EvaluationOptions { OutputFormat = OutputFormat.List });
        eval.IsValid.Should().BeFalse("transition keys should match the schema's pattern");
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static JsonNode YamlFileToJsonNode(string path)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        using var reader = new StreamReader(path);
        var obj = deserializer.Deserialize<object?>(reader);
        // YAML's `Deserialize<object>` returns every scalar as `string`
        // (no type inference when target is dynamic). Walk the tree and
        // promote strings that parse as integers / booleans / null to
        // their proper JSON-Schema types so `line: 4125` validates
        // against `{"type": "integer"}`.
        var json = JsonSerializer.Serialize(NormaliseTree(obj));
        return JsonNode.Parse(json)!;
    }

    private static object? NormaliseTree(object? value) => value switch
    {
        IDictionary<object, object?> dict => dict.ToDictionary(
            kv => kv.Key?.ToString() ?? "", kv => NormaliseTree(kv.Value)),
        IList<object?> list => list.Select(NormaliseTree).ToList(),
        string s => InferScalar(s),
        _ => value,
    };

    private static object? InferScalar(string s)
    {
        // YAML 1.2 core schema: bool / null / int / float, otherwise string.
        if (s is "null" or "~" or "") return null;
        if (s is "true" or "True" or "TRUE") return true;
        if (s is "false" or "False" or "FALSE") return false;
        if (long.TryParse(s, System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var f)
            && !double.IsInfinity(f) && !double.IsNaN(f)
            && (s.Contains('.', StringComparison.Ordinal) || s.Contains('e', StringComparison.OrdinalIgnoreCase)))
            return f;
        return s;
    }

    private static string DescribeFailure(EvaluationResults eval)
    {
        var details = string.Join("; ", (eval.Details ?? [])
            .Where(d => d.HasErrors)
            .Select(d => $"{d.InstanceLocation}: {string.Join(", ", d.Errors?.Values ?? [])}"));
        return string.IsNullOrEmpty(details) ? "(no detail)" : details;
    }

    private static string LocateSchema() =>
        Path.Combine(FindRepoRoot(), "spec-sdl", "schema", "sdl-citations.schema.json");

    private static string LocateCitationsDir() =>
        Path.Combine(FindRepoRoot(), "spec-sdl", "v2.2-errata", "data-link", "yaml");

    private static string FindRepoRoot()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(CitationsSchemaTests).Assembly.Location)!;
        var d = new DirectoryInfo(assemblyDir);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "spec-sdl")))
            d = d.Parent;
        if (d is null)
            throw new InvalidOperationException(
                $"can't locate repo root walking up from {assemblyDir} — no spec-sdl/ directory in any ancestor.");
        return d.FullName;
    }
}
