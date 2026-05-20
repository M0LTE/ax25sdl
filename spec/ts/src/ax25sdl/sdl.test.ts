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

// figc4.7 declares thirteen subroutines in the spec. The graphml edge
// fix for #11 (SABM out-edge from n50) restored Establish_Data_Link
// and Establish_Extended_Data_Link, so all 13 transcribe.
describe("figc4.7 subroutines", () => {
  it("has 13 entries", () => {
    expect(DataLinkSubroutines.subroutines).toHaveLength(13);
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
