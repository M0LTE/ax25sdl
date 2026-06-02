using System.Globalization;
using Packet.Sdl.IR;

namespace Packet.Sdl.CodeGen.Csharp;

/// <summary>
/// C# emitter for the resolved SDL IR. Renders state-machine pages to
/// <c>.g.cs</c> / <c>.g.Tests.cs</c> / <c>.g.mmd</c> via Scriban
/// templates, and subroutine pages to <c>.g.cs</c>. Every emission is
/// validated with <see cref="CsharpValidator"/> before being returned.
/// </summary>
public static class CsharpEmitter
{
    private static readonly Scriban.Template CodeTemplate        = TemplateLoader.Load("code.scriban-cs");
    private static readonly Scriban.Template TestsTemplate       = TemplateLoader.Load("tests.scriban-cs");
    private static readonly Scriban.Template MermaidTemplate     = TemplateLoader.Load("mermaid.scriban-mmd");
    private static readonly Scriban.Template SubroutinesTemplate = TemplateLoader.Load("subroutines.scriban-cs");

    public sealed record StatePageEmission(string ClassName, string Code, string Tests, string Mermaid);
    public sealed record SubroutinePageEmission(string ClassName, string Code);

    /// <summary>
    /// Emit C# for one state-machine page. <paramref name="codePath"/> and
    /// <paramref name="testsPath"/> are passed through to the Roslyn
    /// parse-back diagnostics so error messages point at the eventual
    /// output paths rather than virtual names.
    /// </summary>
    public static StatePageEmission EmitStatePage(ResolvedPage page, string codePath, string testsPath)
    {
        var model = CsharpStateModel.From(page);
        var code    = TemplateLoader.Render(CodeTemplate, model);
        var tests   = TemplateLoader.Render(TestsTemplate, model);
        var mermaid = TemplateLoader.Render(MermaidTemplate, model);
        CsharpValidator.Validate(codePath,  code);
        CsharpValidator.Validate(testsPath, tests);
        return new StatePageEmission(model.ClassName, code, tests, mermaid);
    }

    /// <summary>Emit C# for one subroutine page.</summary>
    public static SubroutinePageEmission EmitSubroutinePage(ResolvedSubroutinesPage page, string codePath)
    {
        var model = CsharpSubroutinesModel.From(page);
        var code = TemplateLoader.Render(SubroutinesTemplate, model);
        CsharpValidator.Validate(codePath, code);
        return new SubroutinePageEmission(model.ClassName, code);
    }

    // ─── Literal helpers ──────────────────────────────────────────────

    internal static string CSharpStringLiteral(string s)
    {
        var escaped = s
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r",  StringComparison.Ordinal)
            .Replace("\n", "\\n",  StringComparison.Ordinal)
            .Replace("\t", "\\t",  StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    internal static string NullOrLiteral(string? s) => s is null ? "null" : CSharpStringLiteral(s);

    internal static string KindEnumLiteral(ResolvedActionKind kind) => kind switch
    {
        ResolvedActionKind.SignalUpper => "ActionKind.SignalUpper",
        ResolvedActionKind.SignalLower => "ActionKind.SignalLower",
        ResolvedActionKind.Processing  => "ActionKind.Processing",
        ResolvedActionKind.Subroutine  => "ActionKind.Subroutine",
        ResolvedActionKind.InternalOut => "ActionKind.InternalOut",
        _ => throw new InvalidOperationException($"unknown action kind '{kind}'"),
    };

    internal static string KindIndicator(ResolvedActionKind kind) => kind switch
    {
        ResolvedActionKind.SignalUpper => "↑",
        ResolvedActionKind.SignalLower => "↓",
        ResolvedActionKind.Processing  => "·",
        ResolvedActionKind.Subroutine  => "→()",
        ResolvedActionKind.InternalOut => "⇢",
        _ => "?",
    };

    internal static string Pascal(string snake)
    {
        var parts = snake.Split('_');
        return string.Concat(parts.Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    // ─── Typed action verbs (SP-010 / packet.net#260) ─────────────────────

    /// <summary>
    /// Map a canonical verb string to a stable, collision-free C# enum-member
    /// identifier for <c>Ax25ActionVerb</c>. Operators that recur across verbs
    /// become words (<c>:=</c>→Assign, <c>*</c>→Times, <c>+</c>→Plus,
    /// <c>=</c>→Eq); every other run of non-alphanumerics is a PascalCase token
    /// boundary. Verified collision-free across the canonical catalog; the enum
    /// emission asserts distinctness so any future collision fails codegen.
    /// </summary>
    internal static string VerbEnumMember(string verb)
    {
        var s = verb
            .Replace(":=", " Assign ", StringComparison.Ordinal)
            .Replace("*",  " Times ",  StringComparison.Ordinal)
            .Replace("+",  " Plus ",   StringComparison.Ordinal)
            .Replace("=",  " Eq ",     StringComparison.Ordinal);

        var sb = new System.Text.StringBuilder(s.Length);
        var atBoundary = true;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(atBoundary ? char.ToUpperInvariant(ch) : ch);
                atBoundary = false;
            }
            else
            {
                atBoundary = true;
            }
        }

        var id = sb.ToString();
        if (id.Length == 0)
            throw new InvalidOperationException($"verb '{verb}' sanitised to an empty enum identifier");
        if (char.IsDigit(id[0]))
            id = "_" + id;
        return id;
    }

    /// <summary>Render a verb as its <c>Ax25ActionVerb.&lt;Member&gt;</c> literal.</summary>
    internal static string VerbEnumLiteral(string verb) => "Ax25ActionVerb." + VerbEnumMember(verb);

    /// <summary>
    /// Render the generated <c>Ax25ActionVerb.g.cs</c> — the closed set of every
    /// canonical action verb in the spec, so the runtime dispatcher can switch
    /// exhaustively (a new/renamed verb is then a compile error, not a runtime
    /// throw). Members are ordered by canonical string for a stable diff; each
    /// carries the canonical text as its doc comment.
    /// </summary>
    public static string EmitActionVerbEnum(IEnumerable<string> canonicalVerbs)
    {
        var distinct = canonicalVerbs.Distinct(StringComparer.Ordinal)
                                     .OrderBy(v => v, StringComparer.Ordinal)
                                     .ToList();

        var byMember = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in distinct)
        {
            var member = VerbEnumMember(v);
            if (byMember.TryGetValue(member, out var clash))
                throw new InvalidOperationException(
                    $"Ax25ActionVerb identifier collision: '{v}' and '{clash}' both sanitise to '{member}'. " +
                    "Disambiguate one of the canonical verb spellings in spec-sdl/actions.yaml.");
            byMember[member] = v;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by Packet.Sdl.CodeGen — do not edit. See spec-sdl/actions.yaml.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Packet.Ax25.Sdl;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// The closed set of canonical AX.25 SDL action verbs (figc4.x), one member per");
        sb.AppendLine("/// distinct semantic action across every page + subroutine. Carried by");
        sb.AppendLine("/// <see cref=\"ActionStep.Verb\"/>; lets a dispatcher switch exhaustively.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public enum Ax25ActionVerb");
        sb.AppendLine("{");
        foreach (var v in distinct)
        {
            sb.AppendLine("    /// <summary><c>" + System.Security.SecurityElement.Escape(v) + "</c></summary>");
            sb.AppendLine("    " + VerbEnumMember(v) + ",");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    internal static string FormatReferenceCsv(IEnumerable<ResolvedReference> refs)
        => string.Join(", ", refs.Select(r =>
            "new ImplementationReference(" +
            $"Source: {CSharpStringLiteral(r.Source)}, " +
            $"Cite: {NullOrLiteral(r.Cite)}, " +
            $"Quote: {NullOrLiteral(r.Quote)}, " +
            $"Path: {NullOrLiteral(r.Path)}, " +
            $"Function: {NullOrLiteral(r.Function)}, " +
            $"Line: {(r.Line is null ? "null" : r.Line.Value.ToString(CultureInfo.InvariantCulture))}, " +
            $"Note: {NullOrLiteral(r.Note)})"));

    internal static string FormatActionsCsv(IEnumerable<ResolvedAction> actions)
        => string.Join(", ", actions.Select(a =>
            $"new ActionStep({VerbEnumLiteral(a.Verb)}, {KindEnumLiteral(a.Kind)})"));

    internal static string FormatLoopsCsv(IEnumerable<ResolvedLoop> loops)
        => string.Join(", ", loops.Select(l =>
            $"new LoopRange({l.Start.ToString(CultureInfo.InvariantCulture)}, " +
            $"{l.Length.ToString(CultureInfo.InvariantCulture)}, " +
            $"{CSharpStringLiteral(l.Predicate)}, " +
            $"{(l.TestAtEnd ? "true" : "false")})"));

    internal static string EscapeMermaid(string s) => s
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("\n", " ",      StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal);

    internal static string BuildMermaidEdgeLabel(string id, string on, string? guard, IReadOnlyList<ResolvedAction> actions)
    {
        var parts = new List<string> { id, "on: " + EscapeMermaid(on) };
        if (!string.IsNullOrEmpty(guard))
        {
            parts.Add("[" + EscapeMermaid(guard) + "]");
        }
        foreach (var a in actions)
        {
            parts.Add("/ " + KindIndicator(a.Kind) + " " + EscapeMermaid(a.Verb));
        }
        return string.Join("<br/>", parts);
    }
}
