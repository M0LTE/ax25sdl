# ax25sdl

AX.25 v2.2 SDL transcriptions, codegen, and multi-language artefacts.

This repo holds the canonical SDL transcriptions for the connected-mode link-layer state machines (figc4.1 through figc4.7) and the codegen pipeline that turns them into ready-to-consume libraries in seven languages: **C#, Go, TypeScript, JSON, Rust, C, and Python**.

> **Status:** prove-out repo. Tom (M0LTE) is working with the original AX.25 spec authors on whether `packethacking/ax25spec` should be the canonical community home. Until that's agreed, this repo lives at `m0lte/ax25sdl` and downstream consumers (e.g. [`m0lte/packet.net`](https://github.com/m0lte/packet.net)) pull from here.

## Layout

```
spec-sdl/                          YAML DSL + JSON schema — human-authored transcriptions
spec-sdl/v2.2/                     clean published v2.2 transcriptions (pending backfill — see README)
spec-sdl/v2.2-errata/              v2.2 + errata (red/green annotations in the figures applied)
spec-sdl/v2.2-errata/data-link/    transcribed state pages (figc4.x) + their yEd graphml sources
spec-sdl/schema/                   JSON Schema for the *.sdl.yaml files
spec-sdl/actions.yaml              action-verb normalisation table (shared across revisions)
spec-sdl/events.yaml               canonical event catalog (shared across revisions)

tools/Packet.Sdl.IR/               language-neutral IR + validation
tools/Packet.Sdl.CodeGen/          thin orchestrator (driver)
tools/Packet.Sdl.CodeGen.Csharp/   C# emitter (Scriban + Roslyn)
tools/Packet.Sdl.CodeGen.Go/       Go emitter (hand-rolled, gofmt-finalised)
tools/Packet.Sdl.CodeGen.Ts/       TypeScript emitter
tools/Packet.Sdl.CodeGen.Json/     JSON emitter (codified IR for non-C# consumers)
tools/Packet.Sdl.CodeGen.Rust/     Rust emitter
tools/Packet.Sdl.CodeGen.C/        C emitter
tools/Packet.Sdl.CodeGen.Python/   Python emitter
tools/Packet.Sdl.Lint/             standalone schema lint

src/Packet.Ax25.Sdl/               C# package (NuGet: Packet.Ax25.Sdl)
go-spec/                           Go module (github.com/m0lte/ax25sdl/go-spec)
ts-spec/                           npm package (ax25sdl)

tests/Packet.Sdl.CodeGen.Tests/    codegen tests

docs/sdl-primer.md                 SDL shape reference
docs/sdl-transcription-runbook.md  end-to-end per-figure workflow
docs/sdl-verb-catalogue.md         action-verb normalisation reference
docs/adr/0001-sdl-dsl.md           why the YAML DSL + codegen exists
```

## Common commands

```sh
# Build the codegen tools + C# library
dotnet build

# Run codegen tests
dotnet test

# Regenerate all backends (writes into src/Packet.Ax25.Sdl/, ts-spec/, go-spec/, etc.)
dotnet run --project tools/Packet.Sdl.CodeGen

# Regenerate a single backend
dotnet run --project tools/Packet.Sdl.CodeGen -- --csharp
dotnet run --project tools/Packet.Sdl.CodeGen -- --go
dotnet run --project tools/Packet.Sdl.CodeGen -- --ts
dotnet run --project tools/Packet.Sdl.CodeGen -- --rust
dotnet run --project tools/Packet.Sdl.CodeGen -- --c
dotnet run --project tools/Packet.Sdl.CodeGen -- --python
dotnet run --project tools/Packet.Sdl.CodeGen -- --json

# Verify the generated Go compiles + passes gofmt
cd go-spec && go build ./... && go vet ./... && go test ./... && gofmt -l .

# Verify the generated TS typechecks + tests pass
cd ts-spec && npm ci && npm run typecheck && npm test
```

## What's published

| Artefact | Package manager | Name | Source |
| --- | --- | --- | --- |
| C# library | NuGet | `Packet.Ax25.Sdl` | `src/Packet.Ax25.Sdl/` |
| TypeScript library | npm | `ax25sdl` | `ts-spec/` |
| Go module | git | `github.com/m0lte/ax25sdl/go-spec` | `go-spec/` |
| Rust crate | _tbd_ | _tbd_ | `tools/Packet.Sdl.CodeGen.Rust/` output |
| C / Python / JSON | _tbd_ | _tbd_ | codegen output |

## License

[MIT](LICENSE) — see `LICENSE` for details. Spec text and figures are derived from the AX.25 v2.2 specification; this repo's transcription discipline is documented in [`docs/sdl-transcription-runbook.md`](docs/sdl-transcription-runbook.md).
