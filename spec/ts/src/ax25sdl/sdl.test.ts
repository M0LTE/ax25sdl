import { describe, it, expect } from "vitest";

import {
  DataLinkAwaitingConnection,
  DataLinkAwaitingV22Connection,
  DataLinkAwaitingRelease,
  DataLinkConnected,
  DataLinkDisconnected,
  DataLinkSubroutines,
} from "./index.js";
import type { StatePage, ActionKind } from "./index.js";

// State pages must declare at least one transition. The codegen
// validator rejects YAML pages with zero transitions before they
// reach the emitter, so an empty page is a generator bug.
describe("state pages", () => {
  const pages: readonly StatePage[] = [
    DataLinkAwaitingConnection,
    DataLinkAwaitingV22Connection,
    DataLinkAwaitingRelease,
    DataLinkConnected,
    DataLinkDisconnected,
  ];

  for (const p of pages) {
    it(`${p.machine}/${p.state} has transitions`, () => {
      expect(p.transitions.length).toBeGreaterThan(0);
    });
  }
});

// The figc4.7 page declares thirteen subroutines in the spec. The
// current graphml has authoring bugs on Establish_Data_Link and
// Establish_Extended_Data_Link (n50 SABM is missing its outgoing
// edge), so the tool currently emits 11 — those two subroutines are
// skipped with a warning. When the graphmls are redrawn this bumps
// back to 13.
describe("figc4.7 subroutines", () => {
  it("has 11 entries (will be 13 once Establish_Data_Link / Establish_Extended_Data_Link graphml edges are fixed)", () => {
    expect(DataLinkSubroutines.subroutines).toHaveLength(11);
  });

  it("UI_Check appears", () => {
    const names = DataLinkSubroutines.subroutines.map((s) => s.name);
    expect(names).toContain("UI_Check");
  });
});

// ActionKind is a string-literal union; spot-check that generated
// pages use values from the declared set. The compile-time check is
// already enforced by the type system; this is belt-and-braces.
describe("action kinds", () => {
  const allowed = new Set<ActionKind>([
    "signal_upper", "signal_lower", "processing", "subroutine", "internal_out",
  ]);

  it("every generated verb has a known kind", () => {
    for (const p of [DataLinkAwaitingConnection, DataLinkConnected, DataLinkDisconnected]) {
      for (const t of p.transitions) {
        for (const a of t.actions) {
          expect(allowed.has(a.kind)).toBe(true);
        }
      }
    }
  });
});
