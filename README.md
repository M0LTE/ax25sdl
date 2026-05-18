# ax25sdl

Canonical AX.25 v2.2 SDL transcriptions + codegen. Turns the figc4.x state-machine diagrams from the AX.25 v2.2 specification into ready-to-consume libraries in **C#, Go, TypeScript, JSON, Rust, C, and Python**.

> **Status:** prove-out repo. The team will work with the original AX.25 spec contributors on whether `packethacking/ax25spec` should become the canonical community home for SDL transcriptions. Until that's agreed, `m0lte/ax25sdl` is the prove-out venue and downstream consumers pull from here.

## How it works

1. **Human-authored transcriptions** under `spec-sdl/v2.2-errata/data-link/` — one `*.sdl.yaml` per state page (figc4.1 through figc4.7), encoding every transition verbatim from the figure. Each page has a companion `*.graphml` yEd source.
2. **Codegen** under `tools/Packet.Sdl.CodeGen/` reads the YAML, validates against the JSON Schema (`spec-sdl/schema/`), and emits transition tables + type stubs for each backend. All seven backends share the same IR (`tools/Packet.Sdl.IR/`).
3. **Per-backend publish.** The C# library publishes to NuGet; the TypeScript library publishes to npm; the Go module is consumable from this repo via `go get`.

## Revision provenance

The AX.25 v2.2 figures use colour-coded version control: black is the published v2.2 spec, red and green are errata that don't form part of the released spec. Today, all existing transcriptions encode the v2.2 + errata variant.

- `spec-sdl/v2.2/` — clean published v2.2 (black-only). **Currently empty** — backfill pending.
- `spec-sdl/v2.2-errata/` — v2.2 + errata applied. This is what every existing `*.sdl.yaml` encodes.

The `Packet.Ax25.Sdl` package versioning scheme: `MAJOR.MINOR` = WG-published spec revision (`2.2`, `2.3`, `3.0`), `PATCH` = errata-batch accumulation. Each published artefact will ship **both** variants and consumers will pick at runtime via an `SdlRevision` enum (planned — current consumers get the errata variant unconditionally). See [`CLAUDE.md` § SDL revision provenance](CLAUDE.md) for the full plan.

## Layout

```
spec-sdl/v2.2/                     clean published v2.2 transcriptions (pending backfill)
spec-sdl/v2.2-errata/data-link/    v2.2 + errata: transcribed state pages (figc4.x) + graphml sources
spec-sdl/schema/                   JSON Schema for *.sdl.yaml
spec-sdl/actions.yaml              action-verb normalisation table
spec-sdl/events.yaml               canonical event catalog

tools/Packet.Sdl.IR/               language-neutral IR + validation
tools/Packet.Sdl.CodeGen/          orchestrator (driver)
tools/Packet.Sdl.CodeGen.{Csharp,Go,Ts,Json,Rust,C,Python}/   per-backend emitters
tools/Packet.Sdl.Lint/             standalone schema lint

src/Packet.Ax25.Sdl/               C# library — publishes to NuGet
go-spec/                           Go module — github.com/m0lte/ax25sdl/go-spec
ts-spec/                           npm package source — publishes as `ax25sdl`

docs/sdl-primer.md                 SDL shape reference
docs/sdl-transcription-runbook.md  end-to-end per-figure workflow
docs/sdl-verb-catalogue.md         action-verb normalisation reference
docs/adr/0001-sdl-dsl.md           why the YAML DSL + codegen exists
```

## Common commands

```sh
# Build the codegen tools + C# library
dotnet build

# Regenerate all backends
dotnet run --project tools/Packet.Sdl.CodeGen

# Regenerate one backend
dotnet run --project tools/Packet.Sdl.CodeGen -- --csharp
dotnet run --project tools/Packet.Sdl.CodeGen -- --go
dotnet run --project tools/Packet.Sdl.CodeGen -- --ts
# ... --rust, --c, --python, --json

# Verify generated Go compiles + tests + gofmt clean
cd go-spec && go build ./... && go vet ./... && go test ./... && gofmt -l .

# Verify generated TS typechecks + tests pass
cd ts-spec && npm ci && npm run typecheck && npm test
```

Tag a `v*` release to fire [`.github/workflows/publish.yml`](.github/workflows/publish.yml) — both NuGet and npm publish from the tag (version taken from the tag, stripping the `v` prefix).

## What's published

| Artefact | Package manager | Name |
| --- | --- | --- |
| C# library | NuGet | [`Packet.Ax25.Sdl`](https://www.nuget.org/packages/Packet.Ax25.Sdl) |
| TypeScript library | npm | [`ax25sdl`](https://www.npmjs.com/package/ax25sdl) |
| Go module | git | `github.com/m0lte/ax25sdl/go-spec` |
| Rust crate | _planned_ | _tbd_ |
| C / Python / JSON | _consumed in-tree_ | codegen output (not packaged externally) |

## Provenance

Extracted from `m0lte/packet.net` on 2026-05-17 — the SDL transcriptions + codegen pipeline used to live under `spec-sdl/` and `tools/Packet.Sdl.*/` in that monorepo. History preserved via `git filter-repo`. The .NET runtime that consumes these tables stays in `m0lte/packet.net`; the TypeScript runtime moved to `m0lte/ax25-ts`.

## Sibling repos

| Repo | What it is | Relation to here |
| --- | --- | --- |
| **`m0lte/ax25sdl`** *(here)* | SDL transcriptions + codegen | source of truth |
| [`m0lte/packet.net`](https://github.com/m0lte/packet.net) | .NET libraries + node host | consumes [`Packet.Ax25.Sdl`](https://www.nuget.org/packages/Packet.Ax25.Sdl) from NuGet |
| [`m0lte/ax25-ts`](https://github.com/m0lte/ax25-ts) | `@packet-net/ax25` browser TS library | consumes [`ax25sdl`](https://www.npmjs.com/package/ax25sdl) from npm |
| [`m0lte/packet-term-tui`](https://github.com/m0lte/packet-term-tui) | C# Terminal.Gui v2 TUI | transitive: via `Packet.Ax25` → `Packet.Ax25.Sdl` |
| [`m0lte/packet-term-web`](https://github.com/m0lte/packet-term-web) | Browser TNC2 emulator at https://packet-term.m0lte.uk | transitive: via `@packet-net/ax25` → `ax25sdl` |

## License

[MIT](LICENSE). Spec text and figures are derived from the AX.25 v2.2 specification; this repo's transcription discipline is documented in [`docs/sdl-transcription-runbook.md`](docs/sdl-transcription-runbook.md).
