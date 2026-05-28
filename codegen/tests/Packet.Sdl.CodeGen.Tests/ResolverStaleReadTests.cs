using AwesomeAssertions;
using Packet.Sdl.IR;

namespace Packet.Sdl.CodeGen.Tests;

/// <summary>
/// Regression tests for ax25sdl#53 — the Resolver's stale-read substitution.
///
/// A transition guard is the conjunction of its decision predicates, evaluated
/// once *before* any action runs. When the figure draws a decision *after* an
/// assignment to one of the variables it tests (e.g. the figc4.5
/// recovery-complete "V(s) = V(a)?" drawn after "V(a) := N(r)"), SDL semantics
/// say the decision reads the post-assignment value — so the Resolver must
/// substitute the assigned value into the guard predicate. The substitution is
/// path-precise (only where the assignment actually precedes the decision),
/// leaves loop continue-predicates alone (the runtime re-evaluates those each
/// iteration), and throws rather than silently emit a guard it cannot flatten.
/// </summary>
public class ResolverStaleReadTests
{
    private static SdlPage Page(List<SdlDecision> decisions, params SdlPathStep[] path) => new()
    {
        Machine     = "test",
        State       = "Test",
        Source      = new SdlSourceYaml { Spec = "test", Figure = "test" },
        Decisions   = decisions,
        Transitions = new() { new SdlTransition { Id = "t", On = "E", Path = path.ToList(), Next = "Test" } },
    };

    private static SdlDecision Dec(string id, string question, string predicate) =>
        new() { Id = id, Question = question, Predicate = predicate };

    private static SdlPathStep Branch(string id, string branch) => new() { Decision = id, Branch = branch };
    private static SdlPathStep Do(string action)                 => new() { Action = action, Kind = "processing" };

    private static string? GuardOf(SdlPage page) => Resolver.Resolve(page).Transitions[0].Guard;

    [Fact]
    public void Substitutes_an_assigned_variable_into_a_following_decision_guard()
    {
        // "V(a) := N(r)" then "V(s) == V(a)?" Yes  ≡  V(s) == N(r)  →  vs_eq_nr
        var page = Page(
            new() { Dec("d", "V(s) == V(a)?", "vs_eq_va") },
            Do("V(a) := N(r)"),
            Branch("d", "Yes"));

        GuardOf(page).Should().Be("vs_eq_nr");
    }

    [Fact]
    public void Substitutes_on_the_negated_No_branch_too()
    {
        var page = Page(
            new() { Dec("d", "V(s) == V(a)?", "vs_eq_va") },
            Do("V(a) := N(r)"),
            Branch("d", "No"));

        GuardOf(page).Should().Be("not vs_eq_nr");
    }

    [Fact]
    public void Leaves_the_same_decision_untouched_when_no_assignment_precedes_it()
    {
        // Path-precise: the SREJ P=0 case — same diamond, but V(a) was not
        // updated on this path, so the guard must keep reading current V(a).
        var page = Page(
            new() { Dec("d", "V(s) == V(a)?", "vs_eq_va") },
            Branch("d", "Yes"));

        GuardOf(page).Should().Be("vs_eq_va");
    }

    [Fact]
    public void Ignores_an_assignment_that_comes_after_the_decision()
    {
        var page = Page(
            new() { Dec("d", "V(s) == V(a)?", "vs_eq_va") },
            Branch("d", "Yes"),
            Do("V(a) := N(r)"));

        GuardOf(page).Should().Be("vs_eq_va");
    }

    [Fact]
    public void Throws_when_a_stale_read_cannot_be_flattened_to_a_pre_action_guard()
    {
        // RHS is not a bare variable, so there is no pre-action predicate that
        // equals the post-assignment test — the table must not be emitted.
        var page = Page(
            new() { Dec("d", "V(s) == V(a)?", "vs_eq_va") },
            Do("V(s) := 0"),
            Branch("d", "Yes"));

        var act = () => Resolver.Resolve(page);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ax25sdl#53*");
    }

    [Fact]
    public void Does_not_substitute_a_loop_continue_predicate()
    {
        // The I-frame reassembly loop: V(r) is incremented before and inside the
        // loop, and the continue test reads V(r). It is re-evaluated each
        // iteration at runtime, so it must stay verbatim and must not throw on
        // the (non-variable) increment.
        var page = Page(
            new() { Dec("loop", "I frame stored for V(r)?", "vr_I_frame_stored") },
            Do("V(r) := V(r) + 1"),
            new SdlPathStep
            {
                LoopWhile = "loop",
                Body      = new() { new SdlPathStep { Action = "V(r) := V(r) + 1", Kind = "processing" } },
            });

        var resolved = Resolver.Resolve(page).Transitions[0];

        resolved.Loops.Should().ContainSingle();
        resolved.Loops[0].Predicate.Should().Be("vr_I_frame_stored");
        resolved.Guard.Should().BeNull();
    }
}
