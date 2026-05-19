namespace Packet.Sdl.Transcribe;

/// <summary>
/// In-memory representation of one SDL state page or subroutines page.
/// Designed to round-trip through the YAML schema at spec-sdl/schema/.
/// </summary>
public sealed class SdlPage
{
    public required string Machine { get; init; }
    public required string State { get; init; }
    public string Coverage { get; init; } = "complete";
    public PageSource Source { get; init; } = new();
    public List<string> Save { get; } = [];
    public List<Decision> Decisions { get; } = [];
    public List<Transition> Transitions { get; } = [];
    /// <summary>Filename of the source graphml — used by the emitter for the
    /// auto-generation header comment. Set by the caller.</summary>
    public string? SourceGraphmlPath { get; set; }
}

public sealed class PageSource
{
    public string Spec { get; init; } = "";
    public string Figure { get; init; } = "";
    public string? Url { get; init; }
}

public sealed class Decision
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required string Predicate { get; init; }
}

public sealed class Transition
{
    public required string Id { get; init; }
    public required string On { get; init; }
    public List<PathStep> Path { get; } = [];
    public required string Next { get; init; }
}

public abstract record PathStep;

public sealed record ActionStep(string Action, string Kind) : PathStep;

public sealed record DecisionBranch(string DecisionId, string Branch) : PathStep;

/// <summary>
/// In-memory representation of an SDL subroutines page (figc4.7-style).
/// Schema at spec-sdl/schema/sdl-subroutines.schema.json.
/// </summary>
public sealed class SubroutinePage
{
    public required string Machine { get; init; }
    public PageSource Source { get; init; } = new();
    public List<Subroutine> Subroutines { get; } = [];
    public string? SourceGraphmlPath { get; set; }
}

public sealed class Subroutine
{
    public required string Name { get; init; }
    public List<Decision> Decisions { get; } = [];
    public List<SubroutinePath> Paths { get; } = [];
}

public sealed class SubroutinePath
{
    public required string Id { get; init; }
    public List<PathStep> Steps { get; } = [];
}
