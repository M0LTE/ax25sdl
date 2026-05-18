namespace Packet.Sdl.Transcribe;

/// <summary>
/// Resolves a figure-verbatim label (with its shape class) to the canonical
/// event name defined in spec-sdl/events.yaml.
/// </summary>
/// <remarks>
/// d5 shape-class is informational, NOT semantic — CLAUDE.md is explicit that
/// the figures don't always use "lower layer" / "upper layer" consistently
/// with what they really mean. The resolver therefore looks at the label
/// shape primarily and consults d5 only to disambiguate the two "All Other
/// Primitives" catch-alls.
/// </remarks>
public static class EventResolver
{
    /// <summary>
    /// Set of frame names that, when used as a Signal reception trigger,
    /// always resolve to "&lt;NAME&gt;_received" — see events.yaml § frames_received.
    /// </summary>
    private static readonly HashSet<string> FrameNames = new(StringComparer.Ordinal)
    {
        "I", "RR", "RNR", "REJ", "SREJ", "UI",
        "SABM", "SABME", "DISC", "UA", "DM",
        "FRMR", "XID", "TEST",
    };

    /// <summary>
    /// Explicit mappings for labels that aren't a clean transformation
    /// of their figure spelling. Keyed by (Label, ShapeClass).
    /// </summary>
    private static readonly Dictionary<(string Label, string ShapeClass), string> ExplicitMap = new()
    {
        // The "All Other Primitives" catch-all has two flavours, distinguished
        // by which shape class drew them. Encoded verbatim per events.yaml.
        [("All Other Primitives", "Signal reception from Lower Layer")] = "all_other_primitives__from_lower_layer",
        [("All Other Primitives", "Signal reception from upper layer")] = "all_other_primitives__from_upper_layer",

        // Composite "I, RR, RNR, REJ or SREJ Commands" — figc4.3.
        [("I, RR, RNR, REJ or SREJ Commands", "Signal reception from Lower Layer")] = "i_or_s_command_received",
        [("I, RR, RNR, REJ or SREJ Commands", "Signal reception from upper layer")] = "i_or_s_command_received",
    };

    public static string ResolveTriggerEvent(string label, string shapeClass)
    {
        if (ExplicitMap.TryGetValue((label, shapeClass), out var explicitName))
            return explicitName;

        // "All Other Commands"
        if (label == "All Other Commands") return "all_other_commands";

        // "DL-X Y" → "DL_X_y" (request/confirm/indication lowercased)
        if (label.StartsWith("DL-", StringComparison.Ordinal))
            return NormaliseDlPrimitive(label);

        // Bare frame name → "<NAME>_received"
        if (FrameNames.Contains(label.Trim()))
            return label.Trim() + "_received";

        // "Control Field Error" / "Info Not Permitted In Frame" / "U or S
        // Frame Length Error" — multi-word title-case → snake_case lowercase.
        // These all live under events.yaml § internal.
        return ToSnakeCase(label);
    }

    private static string NormaliseDlPrimitive(string label)
    {
        // "DL-DISCONNECT Request" → "DL_DISCONNECT_request"
        // "DL-UNIT-DATA Request" → "DL_UNIT_DATA_request"
        // "DL-ERROR Indication (L)" → "DL_ERROR_indication"  (drop parenthetical)
        var stripped = StripTrailingParenthetical(label);
        var parts = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0].Replace('-', '_');
        var tail = string.Join('_', parts.Skip(1).Select(p => p.ToLowerInvariant()));
        return string.IsNullOrEmpty(tail) ? head : $"{head}_{tail}";
    }

    private static string StripTrailingParenthetical(string s)
    {
        var openParen = s.IndexOf('(');
        return openParen < 0 ? s.Trim() : s[..openParen].Trim();
    }

    private static string ToSnakeCase(string label)
    {
        // "Control Field Error" → "control_field_error"
        // "U or S Frame Length Error" → "u_or_s_frame_length_error"
        return string.Join('_', label.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.ToLowerInvariant()));
    }

    /// <summary>
    /// Maps a d5 shape class to the canonical "kind" enum value in the
    /// yaml schema, for use in path steps.
    /// </summary>
    public static string? ShapeClassToActionKind(string shapeClass) => shapeClass switch
    {
        "Signal generation to upper layer" => "signal_upper",
        "Signal generation to lower layer" => "signal_lower",
        "Processing description"           => "processing",
        "Subroutine call"                  => "subroutine",
        "Internal Signal Generation"       => "internal_out",
        _ => null,
    };

    /// <summary>
    /// Cleans up a node label for use as an action verb in a path step.
    /// yEd's label contains line-breaks for multi-statement blocks (e.g.
    /// "V(s) ← 0\nV(a) ← 0\nV(r) ← 0") and uses HTML entity escaping for
    /// left-arrow assignments. Verbatim per CLAUDE.md "Trust the figure"
    /// — we don't normalise verb spelling here; actions.yaml does that
    /// downstream at codegen time.
    /// </summary>
    public static string NormaliseActionLabel(string raw)
    {
        // yEd writes "←" as the literal arrow character or as the HTML
        // entity &lt;- (which appears in d6 as "&lt;-" pre-unescaping or
        // "<-" post-unescape). The transcription convention used to date
        // is ":=" — see existing yamls.
        return raw.Replace("\n", "; ")
                  .Replace("<-", ":=")
                  .Replace("←", ":=")
                  .Trim();
    }
}
