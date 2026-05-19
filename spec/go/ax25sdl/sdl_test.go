package ax25sdl

import "testing"

// TestStatePagesHaveTransitions asserts every generated state-machine
// page declared a non-empty Transitions slice. A page with no
// transitions is almost certainly a codegen bug — the validator
// rejects YAML pages with zero transitions before they reach the
// emitter.
func TestStatePagesHaveTransitions(t *testing.T) {
	pages := []StatePage{
		DataLinkAwaitingConnection,
		DataLinkAwaitingV22Connection,
		DataLinkAwaitingRelease,
		DataLinkConnected,
		DataLinkDisconnected,
	}
	for _, p := range pages {
		if len(p.Transitions) == 0 {
			t.Errorf("%s/%s has no transitions", p.Machine, p.State)
		}
	}
}

// TestSubroutinesPageHasBodies asserts the figc4.7 page declared its
// subroutines. The spec figure has 13; the current graphml has authoring
// bugs on Establish_Data_Link and Establish_Extended_Data_Link (n50
// SABM is missing its outgoing edge), so the tool emits 11 — those two
// are skipped with a warning. When the graphmls are redrawn this bumps
// back to 13.
func TestSubroutinesPageHasBodies(t *testing.T) {
	const expected = 11
	if got := len(DataLinkSubroutines.Subroutines); got != expected {
		t.Errorf("expected %d subroutines on figc4.7, got %d", expected, got)
	}
}

// TestActionKindStringRoundTrips asserts every ActionKind value
// produces a non-"unknown" String, catching a generated file that
// names an out-of-range kind constant.
func TestActionKindStringRoundTrips(t *testing.T) {
	for k := SignalUpper; k <= InternalOut; k++ {
		if k.String() == "unknown" {
			t.Errorf("ActionKind(%d) → unknown", k)
		}
	}
}
