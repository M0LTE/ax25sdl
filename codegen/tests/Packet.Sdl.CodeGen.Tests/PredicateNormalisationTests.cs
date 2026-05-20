using AwesomeAssertions;
using Packet.Sdl.Transcribe;

namespace Packet.Sdl.CodeGen.Tests;

/// <summary>
/// Direct coverage of the walker's question-text → identifier normaliser
/// (m0lte/ax25sdl#22). The graphml decision diamonds use mathematical
/// notation verbatim — `V(s) == V(a)?`, `N(s) &gt; V(r)+1?`, `P/F == 1?`,
/// `F == 1 &amp; (frame=RR || frame=RNR || frame=I)?`, `Version 2.2?`. All of
/// these need to produce valid downstream-identifier strings so the
/// runtime <c>GuardEvaluator</c> can tokenise predicate names and bind
/// them to closures. Before #22 the walker left <c>&amp;</c>, <c>||</c>,
/// <c>+</c>, <c>.</c>, <c>/</c> in literally; downstream consumers couldn't
/// resolve them.
/// </summary>
public class PredicateNormalisationTests
{
    [Theory]
    // Simple cases — historic baseline that must continue to work.
    [InlineData("P == 1?", "P_eq_1")]
    [InlineData("F == 1?", "F_eq_1")]
    [InlineData("Able to Establish?", "able_to_establish")]
    // Boolean connectives.
    [InlineData("Command & P == 1?", "command_and_P_eq_1")]
    [InlineData("Response & F == 1?", "response_and_F_eq_1")]
    [InlineData("F == 1 & (frame=RR || frame=RNR || frame=I)?", "F_eq_1_and_frame_eq_RR_or_frame_eq_RNR_or_frame_eq_I")]
    [InlineData("info field length <= N1 & content is octet aligned?", "info_field_length_le_N1_and_content_is_octet_aligned")]
    // P/F slash-or shorthand.
    [InlineData("P/F == 1?", "P_or_F_eq_1")]
    // Arithmetic plus.
    [InlineData("N(s) > V(r)+1?", "ns_gt_vr_plus_1")]
    [InlineData("V(s) == V(a)+k?", "vs_eq_va_plus_k")]
    // Dotted-version spec phrasing.
    [InlineData("Version 2.2?", "version_2_2")]
    // Comparison operators in various combinations.
    [InlineData("V(s) == V(a)?", "vs_eq_va")]
    [InlineData("V(a) <= N(r) <= V(s)?", "va_le_nr_le_vs")]
    [InlineData("N(s) != V(r)?", "ns_ne_vr")]
    // Parens stripped without leaving stray underscores.
    [InlineData("(V(a) <= N(r))?", "va_le_nr")]
    // Commas-as-separators; all-caps tokens preserved (DL / ERROR are spec
    // identifiers per CLAUDE.md and stay verbatim in the predicate form).
    [InlineData("DL-ERROR Indication (C,D)?", "DL_ERROR_indication_C_D")]
    public void Predicate_normalisation_produces_valid_identifiers(string questionLabel, string expectedPredicate)
    {
        Walker.DecisionPredicateForTests(questionLabel).Should().Be(expectedPredicate);
    }

    [Theory]
    [InlineData("P == 1?", "p_eq_1")]
    [InlineData("V(s) == V(a)?", "vs_eq_va")]
    [InlineData("V(s) == V(a)+k?", "vs_eq_va_plus_k")]
    [InlineData("Version 2.2?", "version_2_2")]
    [InlineData("F == 1 & (frame=RR || frame=RNR || frame=I)?", "f_eq_1_and_frame_eq_rr_or_frame_eq_rnr_or_frame_eq_i")]
    [InlineData("info field length <= N1 & content is octet aligned?", "info_field_length_le_n1_and_content_is_octet_aligned")]
    [InlineData("P/F == 1?", "p_or_f_eq_1")]
    public void Decision_id_normalisation_lower_cases_everything(string questionLabel, string expectedId)
    {
        Walker.NormaliseQuestionToIdForTests(questionLabel).Should().Be(expectedId);
    }

    [Theory]
    // Every output must match a valid C identifier (also valid in C#, Go, TS,
    // Rust, Python — the strictest is C, which is what we test against).
    [InlineData("P == 1?")]
    [InlineData("V(s) == V(a)+k?")]
    [InlineData("Version 2.2?")]
    [InlineData("F == 1 & (frame=RR || frame=RNR || frame=I)?")]
    [InlineData("info field length <= N1 & content is octet aligned?")]
    [InlineData("P/F == 1?")]
    [InlineData("N(s) > V(r)+1?")]
    [InlineData("V(a) <= N(r) <= V(s)?")]
    public void Predicate_output_is_a_valid_c_identifier(string questionLabel)
    {
        var predicate = Walker.DecisionPredicateForTests(questionLabel);
        predicate.Should().MatchRegex(@"^[A-Za-z_][A-Za-z0-9_]*$",
            $"predicate '{predicate}' (from '{questionLabel}') must be a valid identifier");
        var id = Walker.NormaliseQuestionToIdForTests(questionLabel);
        id.Should().MatchRegex(@"^[a-z_][a-z0-9_]*$",
            $"decision id '{id}' (from '{questionLabel}') must be a valid lower-case identifier");
    }

    [Fact]
    public void Every_committed_yaml_predicate_is_a_valid_identifier()
    {
        // Backstop: walk every predicate currently committed across all
        // *.sdl.yaml pages and assert each is a valid identifier. Guards
        // against future graphml authoring introducing new operator
        // characters that the normaliser doesn't yet cover.
        var dir = Path.Combine(FindRepoRoot(), "spec-sdl", "v2.2-errata", "data-link", "yaml");
        var files = Directory.EnumerateFiles(dir, "*.sdl.yaml").OrderBy(f => f, StringComparer.Ordinal).ToList();
        files.Should().NotBeEmpty();

        var pattern = new System.Text.RegularExpressions.Regex(@"^\s*predicate:\s*(\S+)\s*$");
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;
                var predicate = m.Groups[1].Value;
                predicate.Should().MatchRegex(@"^[A-Za-z_][A-Za-z0-9_]*$",
                    $"predicate '{predicate}' in {Path.GetFileName(file)} must be a valid identifier (m0lte/ax25sdl#22)");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(PredicateNormalisationTests).Assembly.Location)!;
        var d = new DirectoryInfo(assemblyDir);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "spec-sdl")))
            d = d.Parent;
        if (d is null)
            throw new InvalidOperationException(
                $"can't locate repo root walking up from {assemblyDir} — no spec-sdl/ directory in any ancestor.");
        return d.FullName;
    }
}
