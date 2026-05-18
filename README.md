# ax25sdl

**Canonical AX.25 v2.2 SDL transcriptions, plus a codegen pipeline that turns them into ready-to-consume libraries in seven languages.** The point of this repo is to encode the figc4.x state-machine figures from the AX.25 v2.2 specification *once*, with discipline, and emit that single source of truth as native idiomatic code for C#, Go, TypeScript, JSON, Rust, C, and Python. Downstream runtimes walk the generated tables — they don't have an opinion of their own about what AX.25 says.

> **Status:** prove-out repo. Whether [`packethacking/ax25spec`](https://github.com/packethacking) becomes the canonical community home for these transcriptions is still being decided with the original AX.25 authors. Until then, downstream consumers pull from here.

## Inputs

| Path | What |
| --- | --- |
| [`spec-sdl/v2.2-errata/data-link/`](spec-sdl/v2.2-errata/data-link/) | `*.sdl.yaml` transcriptions of figc4.1 – figc4.7 (v2.2 + errata applied) and their `*.graphml` yEd sources |
| [`spec-sdl/v2.2/`](spec-sdl/v2.2/) | Clean published v2.2 (black-only, no errata). **Currently empty — backfill pending.** |
| [`spec-sdl/schema/`](spec-sdl/schema/) | JSON Schema for `*.sdl.yaml` |
| [`spec-sdl/actions.yaml`](spec-sdl/actions.yaml) | Action-verb normalisation table (figure spellings → canonical verbs) |
| [`spec-sdl/events.yaml`](spec-sdl/events.yaml) | Canonical event catalog |
| Upstream | The AX.25 v2.2 specification figures themselves — the source of truth for every transcription |

## Outputs

| Artefact | Where | Name | In-repo source |
| --- | --- | --- | --- |
| C# library | NuGet | [`Packet.Ax25.Sdl`](https://www.nuget.org/packages/Packet.Ax25.Sdl) | [`spec/csharp/`](spec/csharp/) |
| TypeScript library | npm | [`ax25sdl`](https://www.npmjs.com/package/ax25sdl) | [`spec/ts/`](spec/ts/) |
| Go module | git | `github.com/m0lte/ax25sdl/spec/go` | [`spec/go/`](spec/go/) |
| Rust / C / Python / JSON | _not externally packaged_ | codegen output for in-tree consumers | per-backend dirs |

Tagging `v*` on `main` fires [`.github/workflows/publish.yml`](.github/workflows/publish.yml) — NuGet + npm publish from the same tag, version taken from the tag stripped of its leading `v`.

## Discipline

The transcription rules and how to add a new figure live in:

- [`docs/sdl-primer.md`](docs/sdl-primer.md) — SDL shape reference
- [`docs/sdl-transcription-runbook.md`](docs/sdl-transcription-runbook.md) — end-to-end per-figure workflow
- [`docs/sdl-verb-catalogue.md`](docs/sdl-verb-catalogue.md) — action-verb normalisation
- [`docs/adr/0001-sdl-dsl.md`](docs/adr/0001-sdl-dsl.md) — why YAML + codegen rather than hand-written tables

## Provenance

Extracted from `m0lte/packet.net` on 2026-05-17 — the transcriptions and codegen previously lived alongside the .NET runtime in that monorepo. History preserved via `git filter-repo`.

## Sibling repos

| Repo | What it consumes from here |
| --- | --- |
| [`m0lte/packet.net`](https://github.com/m0lte/packet.net) | `Packet.Ax25.Sdl` (NuGet) |
| [`m0lte/ax25-ts`](https://github.com/m0lte/ax25-ts) | `ax25sdl` (npm) |
| [`m0lte/packet-term-tui`](https://github.com/m0lte/packet-term-tui) | transitive: `Packet.Ax25` → `Packet.Ax25.Sdl` |
| [`m0lte/packet-term-web`](https://github.com/m0lte/packet-term-web) | transitive: `@packet-net/ax25` → `ax25sdl` |

## License

[MIT](LICENSE). Spec text and figures derive from the AX.25 v2.2 specification; the transcription discipline that turns figures into machine-checkable YAML is documented in [`docs/sdl-transcription-runbook.md`](docs/sdl-transcription-runbook.md).
