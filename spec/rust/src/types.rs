//! Hand-written runtime types for the generated SDL pages.
//!
//! The `.g.rs` files in this crate are produced by
//! `codegen/src/Packet.Sdl.CodeGen.Rust` and reference the types
//! defined here via `use crate::types::*`. This file is **not**
//! generated — it's the analogue of `spec/go/ax25sdl/types.go`
//! and `spec/csharp/`'s hand-written shape, providing the language-
//! idiomatic structs the codegen targets.
//!
//! Empty-string / zero-int conventions are used in place of
//! `Option<…>` throughout, to keep the static initialisers
//! readable: a `Guard` of `""` means "no guard"; a `Line` of `0`
//! in an `ImplementationReference` means "no line citation"; etc.

/// SDL action-kind classifier — mirrors `spec-sdl/actions.yaml`
/// kind groups and the C# / Go equivalents.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub enum ActionKind {
    SignalUpper,
    SignalLower,
    Processing,
    Subroutine,
    InternalOut,
}

/// One verb + kind pair along a transition or subroutine path.
/// `verb` is the canonical spelling from `spec-sdl/actions.yaml`;
/// aliases are normalised at codegen time.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct ActionStep {
    pub verb: &'static str,
    pub kind: ActionKind,
}

/// Identifies which figure of which specification a page was
/// transcribed from.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct SdlSource {
    pub spec: &'static str,
    pub figure: &'static str,
    /// Empty = no URL recorded.
    pub url: &'static str,
}

/// A `loop_while` construct rendered as a slice over the flat
/// `actions` list. `start` + `length` describe the body; `predicate`
/// is the continue condition (already negated where the figure's
/// continuing edge is the decision's No branch). `test_at_end` selects
/// the loop topology: `false` = test-at-head (while; body may run zero
/// times), `true` = test-at-tail (do-while; body runs at least once).
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct LoopRange {
    pub start: usize,
    pub length: usize,
    pub predicate: &'static str,
    pub test_at_end: bool,
}

/// One citation supporting a transition or subroutine path.
/// `source` is `"spec_prose"` or the key of a `pinned_refs` entry.
/// Spec-prose citations populate `cite` / `quote`; code citations
/// populate `path` / `function` / `line`.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct ImplementationReference {
    pub source: &'static str,
    pub cite: &'static str,
    pub quote: &'static str,
    pub path: &'static str,
    pub function: &'static str,
    /// `0` = no line citation.
    pub line: u32,
    pub note: &'static str,
}

/// One SDL transition column on a state-machine page.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct TransitionSpec {
    pub id: &'static str,
    pub from: &'static str,
    pub on: &'static str,
    /// Empty = no guard.
    pub guard: &'static str,
    pub actions: &'static [ActionStep],
    pub next: &'static str,
    /// Empty = no notes.
    pub notes: &'static str,
    pub references: &'static [ImplementationReference],
    pub loops: &'static [LoopRange],
}

/// One path through a subroutine. Unlike a `TransitionSpec` there
/// is no incoming event or destination state.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct SubroutinePath {
    pub id: &'static str,
    pub guard: &'static str,
    pub actions: &'static [ActionStep],
    pub notes: &'static str,
    pub references: &'static [ImplementationReference],
    pub loops: &'static [LoopRange],
}

/// One subroutine on a subroutine page.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct SubroutineSpec {
    pub name: &'static str,
    pub paths: &'static [SubroutinePath],
    pub notes: &'static str,
    pub references: &'static [ImplementationReference],
}

/// One generated state-machine page (figc4.1 / 4.2 / 4.3 / 4.4 /
/// 4.6 / etc.).
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct StatePage {
    pub machine: &'static str,
    pub state: &'static str,
    pub source: SdlSource,
    pub transitions: &'static [TransitionSpec],
}

/// One generated subroutine page (figc4.7).
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub struct SubroutinesPage {
    pub machine: &'static str,
    pub source: SdlSource,
    pub subroutines: &'static [SubroutineSpec],
}
