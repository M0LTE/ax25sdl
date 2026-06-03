namespace Packet.Sdl.IR;

/// <summary>
/// One atom of a guard expression: a predicate atom plus whether it is
/// negated. The language-neutral counterpart of the per-backend
/// <c>GuardTerm</c> runtime type. <see cref="Atom"/> is a canonical
/// guard-atom string (a member of the <c>Ax25Guard</c> closed set);
/// <see cref="Negate"/> records a leading <c>not</c>.
/// </summary>
public readonly record struct GuardTermIR(string Atom, bool Negate);

/// <summary>
/// Parses the canonical guard / loop-predicate strings the
/// <see cref="Resolver"/> composes (e.g. <c>"not a and b and not c"</c>)
/// into a flat list of <see cref="GuardTermIR"/> conjuncts. Used by the
/// C# / TS emitters to render the typed (atom, negate) form; the
/// string-typed backends ignore it and keep emitting the raw string.
/// </summary>
/// <remarks>
/// The grammar the Resolver produces is a strict conjunction of optionally-
/// negated atoms: <c>factor ("and" factor)*</c> where <c>factor := "not"?
/// atom</c>. There is no <c>or</c> at the composition level — the only
/// <c>or</c> that occurs is inside an atom's own name (e.g.
/// <c>F_eq_1_and_frame_eq_RR_or_frame_eq_RNR_or_frame_eq_I</c>), which is a
/// single opaque atom token, never split. <see cref="Parse"/> therefore
/// rejects a standalone <c>or</c> token as an unsupported shape rather than
/// silently mis-parsing it — matching the codegen's "stop, don't force it"
/// discipline for guard shapes the typed model can't represent faithfully.
/// </remarks>
public static class GuardExpression
{
    private static readonly char[] Separators = { ' ', '\t' };

    /// <summary>
    /// Parse a composed guard string into its conjuncts. Null / empty /
    /// whitespace yields an empty list (no guard).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The expression contains a top-level <c>or</c>, a dangling <c>not</c>,
    /// or is otherwise not a plain conjunction of optionally-negated atoms —
    /// shapes the typed (atom, negate) model cannot represent.
    /// </exception>
    public static IReadOnlyList<GuardTermIR> Parse(string? expression)
    {
        var terms = new List<GuardTermIR>();
        if (string.IsNullOrWhiteSpace(expression)) return terms;

        var tokens = expression.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        bool expectAtom = true; // true at start and immediately after an "and"
        bool negate = false;
        while (i < tokens.Length)
        {
            var tok = tokens[i++];
            if (tok == "or")
                throw new InvalidOperationException(
                    $"guard expression '{expression}' contains a top-level `or`; the typed Ax25Guard model " +
                    "represents only conjunctions of (optionally negated) atoms. (No spec page emits a " +
                    "top-level `or` today — if one is added, extend GuardTerm to a disjunctive shape.)");

            if (tok == "and")
            {
                if (expectAtom)
                    throw new InvalidOperationException($"guard expression '{expression}': unexpected `and`.");
                expectAtom = true;
                continue;
            }

            if (tok == "not")
            {
                negate = true;
                expectAtom = true;
                continue;
            }

            // An atom.
            if (!expectAtom)
                throw new InvalidOperationException(
                    $"guard expression '{expression}': two atoms without a connecting `and`.");
            terms.Add(new GuardTermIR(tok, negate));
            negate = false;
            expectAtom = false;
        }

        if (expectAtom && terms.Count > 0)
            throw new InvalidOperationException($"guard expression '{expression}': dangling `not`/`and` with no trailing atom.");

        return terms;
    }

    /// <summary>
    /// Parse a single-atom predicate (a loop continue-predicate or undefined-
    /// branch predicate) into exactly one <see cref="GuardTermIR"/>. These are
    /// always a bare atom or <c>not</c>-atom — never a conjunction.
    /// </summary>
    /// <exception cref="InvalidOperationException">The predicate is empty or resolves to other than exactly one atom.</exception>
    public static GuardTermIR ParseSingle(string predicate)
    {
        var terms = Parse(predicate);
        if (terms.Count != 1)
            throw new InvalidOperationException(
                $"predicate '{predicate}' was expected to be a single (optionally negated) atom but parsed to " +
                $"{terms.Count} terms.");
        return terms[0];
    }

    /// <summary>Every distinct atom referenced by a composed guard string (negation stripped).</summary>
    public static IEnumerable<string> Atoms(string? expression) => Parse(expression).Select(t => t.Atom);
}
