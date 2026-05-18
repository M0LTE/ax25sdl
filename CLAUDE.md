# CLAUDE.md

Operating notes for Claude Code (and other agents) working in `m0lte/ax25sdl`.

## What this repo is

The canonical home for the AX.25 v2.2 SDL transcriptions + the codegen pipeline that emits language-specific libraries from them. **Spec data + codegen**, nothing else. Downstream consumers (e.g. `m0lte/packet.net`, `m0lte/ax25-ts`) pull the published artefacts.

Extracted from `m0lte/packet.net` on 2026-05-17 to give the spec its own release cadence + contributor surface. Tom is working with the original AX.25 authors on whether `packethacking/ax25spec` should be the canonical community home; this repo is the prove-out.

## Read first

- [`docs/sdl-primer.md`](docs/sdl-primer.md) — SDL shape reference. Mandatory before touching `/spec-sdl/`.
- [`docs/sdl-transcription-runbook.md`](docs/sdl-transcription-runbook.md) — end-to-end per-figure workflow (graphml → transcription PR → validation PR). Read this when starting a new SDL page.
- [`docs/sdl-verb-catalogue.md`](docs/sdl-verb-catalogue.md) — how `spec-sdl/actions.yaml` normalises figure-verbatim action spellings to canonical verbs at codegen time.
- [`docs/adr/0001-sdl-dsl.md`](docs/adr/0001-sdl-dsl.md) — why the SDL YAML DSL + codegen exists.

## Hard rules

### Trust the figure

The AX.25 SDL figures are the source of truth. When a figure surprises you, the surprise is yours. **Do not** "fix" a branch label, swap a Yes/No, or substitute "correct-looking" actions on the basis that the figure looks wrong to you. If you're uncertain, flag for human review with a `verification_pending:` note — never silently deviate.

### Encode-then-verify

Every transition in `/spec-sdl/` must come from an **explicit human-authored transcription** of the figure. You may *encode* paths that Tom has described in plain text; you may not *infer* paths by reading the PNG yourself.

### Pin implementation evidence

When transcribing any SDL transition whose semantics are non-obvious, cross-reference how at least one of the canonical implementations handles it. Drop the citation into the transition's `notes:` field.

### Reading SDL graphml: `d5` is the authoritative shape class

Each node in a `spec-sdl/**/*.graphml` file carries a `<data key="d5">` description (e.g. "Signal reception from Lower Layer", "Signal generation to upper layer", "Processing description", "Test or decision"). That `d5` text is the **only** authoritative source for the node's shape class.

**Do not** infer a node's meaning from the visual direction of its parallelogram (left-notch vs right-notch) or from your prior understanding of which layer DL-\* vs frame events "should" come from. The figures in the AX.25 spec do not always use shape direction the way figc1.1's legend suggests. The figure is the source of truth; `d5` records the figure's choice verbatim; we transcribe.

When the same label appears under two different `d5` values, those are **two distinct events** in the catalogue. Disambiguate with a `__from_<shape-class>` suffix on the event id.

### SDL revision provenance

The AX.25 v2.2 SDL figures use colour-coded version control: black is original published v2.2, red and green are errata that don't yet form part of the released spec. We don't distinguish red from green — they're both "errata".

Transcriptions are filed under a revision directory:

- `spec-sdl/v2.2/` — clean published v2.2 (black-only). **Currently empty** — Tom will backfill once the errata variant is complete. Until then, the `Published` revision is unavailable to consumers.
- `spec-sdl/v2.2-errata/` — v2.2 with all errata applied (black + red + green). This is what every existing `*.sdl.yaml` in the repo encodes.

A page with no errata is transcribed identically into both directories. A future CI lint will enforce byte-identity for those pages unless the errata variant explicitly declares an errata is applied.

**Package versioning** for `Packet.Ax25.Sdl` (and equivalents):

- `MAJOR.MINOR` = WG-published spec revision (`2.2`, `2.3`, `3.0`). One package per spec revision; parallel lines possible (like Linux LTS).
- `PATCH` = errata batch accumulation within that spec revision.
- Each `PATCH` artefact is **complete** — ships both clean spec tables and errata-applied tables.
- Consumers pick at runtime via `SdlRevision.{Published, WithErrata}`, defaulting to `Published`.

The codegen + `SdlRevision` enum + namespace split are **Phase 2** work, deferred until the `v2.2/` baseline is transcribed. Until then, codegen recursively walks `spec-sdl/**` and emits a single set of tables (sourced from `v2.2-errata/`).

## Common commands

```sh
# Build everything
dotnet build

# Run codegen tests
dotnet test

# Regenerate all backends
dotnet run --project codegen/src/Packet.Sdl.CodeGen

# Regenerate one backend
dotnet run --project codegen/src/Packet.Sdl.CodeGen -- --csharp
dotnet run --project codegen/src/Packet.Sdl.CodeGen -- --go
dotnet run --project codegen/src/Packet.Sdl.CodeGen -- --ts
# etc.

# Verify generated Go compiles + tests + gofmt clean
cd spec/go && go build ./... && go vet ./... && go test ./... && gofmt -l .

# Verify generated TS typechecks + tests pass
cd spec/ts && npm ci && npm run typecheck && npm test
```

## Things to avoid

- Don't hand-edit `spec/csharp/*.g.cs`, `spec/go/ax25sdl/*.g.go`, or `spec/ts/src/ax25sdl/*.g.ts`. They are generated. Edit the corresponding `*.sdl.yaml` and rerun the codegen. (`spec/go/ax25sdl/types.go`, `spec/ts/src/ax25sdl/types.ts`, and `spec/ts/src/ax25sdl/*.test.ts` ARE hand-written — keep the type files in sync with the C# types in `spec/csharp/`.)
- Don't add `[Version=...]` on `<PackageReference>` items — CPM enforces a central version table.
- Don't infer protocol semantics from the spec PNGs. See "Encode-then-verify" above.
- **Don't add new GitHub Actions jobs with `runs-on: ubuntu-latest`** (or any other GitHub-hosted runner label). This project has no Actions minutes budget for hosted runners — every workflow job MUST target `[self-hosted, Linux, X64]`. Same rule as `m0lte/packet.net`.

## When in doubt

Ask Tom. Spec-side surprises are best resolved by reference to the figure with human verification.
