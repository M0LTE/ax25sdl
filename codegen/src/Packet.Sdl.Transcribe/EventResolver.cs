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

        // Internal events with specific spelling per events.yaml — only
        // I_frame_pops_off_queue preserves a capital because it's an
        // I-frame name; the others are fully lowercase.
        [("I Frame Pops Off Queue", "Signal reception from Lower Layer")] = "I_frame_pops_off_queue",
        [("I Frame Pops Off Queue", "Signal reception from upper layer")] = "I_frame_pops_off_queue",
        // figc4.5 TimerRecovery draws "I Frame Pops Off Queue" as an
        // Internal Signal Reception shape (which is semantically correct —
        // it's the local upper layer's I-frame queue producing a frame);
        // Connected (older graphml) drew it as "Signal reception from Lower
        // Layer" which is a real authoring inconsistency. Map both to the
        // same canonical event so the pages converge.
        [("I Frame Pops Off Queue", "Internal Signal Reception")] = "I_frame_pops_off_queue",

        // figc4.5 has an "I Frame" trigger (bare, no qualifier) meaning
        // "received an I-frame from the peer". Sibling of the other bare
        // frame receptions (RR_received, REJ_received, etc.) — canonical
        // event name follows that pattern. events.yaml needs an entry.
        [("I Frame", "Signal reception from upper layer")] = "I_received",
    };

    public static string ResolveTriggerEvent(string label, string shapeClass)
    {
        var trimmed = label.Trim();
        if (ExplicitMap.TryGetValue((trimmed, shapeClass), out var explicitName))
            return explicitName;

        // "All Other Commands"
        if (trimmed.Equals("All Other Commands", StringComparison.OrdinalIgnoreCase))
            return "all_other_commands";

        // "Timer T1 Expiry" / "Timer T2 Expiry" / "Timer T3 Expiry" → "T1_expiry" etc.
        var timerMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed, "^Timer (T[0-9])+ Expiry$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (timerMatch.Success)
            return timerMatch.Groups[1].Value + "_expiry";

        // "DL-X Y" → "DL_X_y" / "MDL-X Y" → "MDL_X_y" / "LM-X Y" → "LM_X_y"
        // (request/confirm/indicate lowercased)
        if (trimmed.StartsWith("DL-", StringComparison.OrdinalIgnoreCase)
         || trimmed.StartsWith("MDL-", StringComparison.OrdinalIgnoreCase)
         || trimmed.StartsWith("LM-", StringComparison.OrdinalIgnoreCase))
            return NormaliseDashPrefixedPrimitive(trimmed);

        // Bare frame name → "<NAME>_received"
        if (FrameNames.Contains(trimmed))
            return trimmed + "_received";

        // "Control Field Error" / "Info Not Permitted In Frame" / "U or S
        // Frame Length Error" — title-case → fully snake_case lowercase to
        // match events.yaml § internal entries. (I_frame_pops_off_queue is
        // the lone exception, handled via ExplicitMap above.)
        return string.Join('_', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.ToLowerInvariant()));
    }

    private static string NormaliseDashPrefixedPrimitive(string label)
    {
        // "DL-DISCONNECT Request" → "DL_DISCONNECT_request"
        // "DL-UNIT-DATA Request" → "DL_UNIT_DATA_request"
        // "DL-ERROR Indication (L)" → "DL_ERROR_indication"  (drop parenthetical)
        // "LM-SEIZE Confirm" → "LM_SEIZE_confirm"
        // "MDL-NEGOTIATE Request" → "MDL_NEGOTIATE_request"
        var stripped = StripTrailingParenthetical(label);
        var parts = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // First part keeps its case (DL/MDL/LM + verb, replace dashes with underscores).
        var head = parts[0].Replace('-', '_');
        // Trailing words lowercase (the action: Request/Confirm/Indication/Indicate).
        var tail = string.Join('_', parts.Skip(1).Select(p => p.ToLowerInvariant()));
        return string.IsNullOrEmpty(tail) ? head : $"{head}_{tail}";
    }

    private static string ToSnakeCasePreservingSpecVars(string label)
    {
        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('_', parts.Select(PreserveOrLower));
    }

    private static string PreserveOrLower(string token)
    {
        // Keep single-letter or all-caps tokens as-is (I, U, S, F, V — spec
        // variable names). Lowercase everything else.
        return System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Z]+[0-9]*$")
            ? token
            : token.ToLowerInvariant();
    }

    private static string StripTrailingParenthetical(string s)
    {
        var openParen = s.IndexOf('(');
        return openParen < 0 ? s.Trim() : s[..openParen].Trim();
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
