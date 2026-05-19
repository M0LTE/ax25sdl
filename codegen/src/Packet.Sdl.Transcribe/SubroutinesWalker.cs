namespace Packet.Sdl.Transcribe;

/// <summary>
/// Walks an SDL subroutines page (one with multiple Subroutine-start →
/// Return-from-Subroutine chains; no entry State node). Distinct from
/// the state-page walker because:
///   - there are no triggers (subroutines are CALLED, not event-driven)
///   - each subroutine has its own start node and one+ return terminus
///   - the yaml shape is sdl-subroutines.schema.json, not sdl-machine
/// </summary>
public static class SubroutinesWalker
{
    public static bool IsSubroutinesPage(GraphmlGraph graph) =>
        graph.Nodes.Any(n => n.ShapeClass == "Subroutine start");

    public static SubroutinePage Walk(GraphmlGraph graph, string graphmlFilename)
    {
        var machine = MachineFromFilename(graphmlFilename);
        var page = new SubroutinePage
        {
            Machine = machine,
            Source = new PageSource
            {
                Spec = "ax.25.2.2.4_Oct_25",
                Figure = "figc4.7",
            },
        };

        var subroutineStarts = graph.Nodes
            .Where(n => n.ShapeClass == "Subroutine start")
            .OrderBy(n => n.Y).ThenBy(n => n.X)
            .ToList();

        foreach (var start in subroutineStarts)
        {
            var name = NormaliseSubroutineName(start.Label);
            var sub = new Subroutine { Name = name };
            // Each subroutine has its own decisions dictionary — per the
            // sdl-subroutines schema, decisions are nested under the
            // subroutine, not at page level.
            var decisionsSeen = new Dictionary<string, Decision>();
            int pathIndex = 0;
            var startEdges = graph.OutgoingByNodeId[start.Id];
            if (startEdges.Count == 0) continue;  // empty subroutine (probably authoring stub)

            try
            {
                foreach (var (steps, branchLabels) in WalkFromSubroutineStart(graph, start, name, decisionsSeen))
                {
                    pathIndex++;
                    var pathId = BuildSubroutinePathId(pathIndex, name, branchLabels);
                    var p = new SubroutinePath { Id = pathId };
                    p.Steps.AddRange(steps);
                    sub.Paths.Add(p);
                }
            }
            catch (InvalidDataException ex)
            {
                // One subroutine's walk failed (typically a missing edge in the
                // graphml). Emit a warning, skip this subroutine, continue with
                // the others. Better than failing the entire page.
                Console.Error.WriteLine($"::warning::subroutine '{name}' (starting at {start.Id}): {ex.Message}");
                continue;
            }

            sub.Decisions.AddRange(decisionsSeen.Values);
            page.Subroutines.Add(sub);
        }

        return page;
    }

    private static string MachineFromFilename(string filename)
    {
        // "DataLink_Subroutines.graphml" → "data_link"
        var bare = Path.GetFileNameWithoutExtension(filename);
        var underscoreAt = bare.IndexOf('_');
        if (underscoreAt < 0)
            throw new InvalidDataException(
                $"subroutines filename '{filename}' must be <Machine>_Subroutines.graphml.");
        return PascalCaseToSnakeCase(bare[..underscoreAt]);
    }

    private static string PascalCaseToSnakeCase(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// "N(r) Error Recovery" → "N_r_Error_Recovery"
    /// "UI Check"            → "UI_Check"
    /// "Set Version 2.0"     → "Set_Version_2_0"
    /// Matches sdl-subroutines.schema.json's `name` pattern
    /// `^[A-Z][A-Za-z0-9_]*$` (PascalCase with underscores).
    /// Parens become word separators so V(r), N(r), etc. tokenise into
    /// their subscripts (V_r, N_r) rather than being collapsed (Vr, Nr).
    /// </summary>
    private static string NormaliseSubroutineName(string label)
    {
        var stripped = label.Trim()
                            .Replace("(", " ")
                            .Replace(")", " ")
                            .Replace(".", "_");
        return string.Join('_', stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Walks every path from a subroutine-start node to a Return-from-Subroutine
    /// terminus. Decision branches produce multiple paths just like state-page
    /// transitions. Cycle-back edges are skipped (same convention).
    /// </summary>
    private static IEnumerable<(List<PathStep> Steps, List<string> BranchLabels)>
        WalkFromSubroutineStart(GraphmlGraph graph, GraphmlNode start, string subroutineName,
                                Dictionary<string, Decision> decisionsSeen)
    {
        // Prefix decisions with the subroutine name so the same question text
        // gets a different id in different subroutines (mirrors the state-page
        // convention with trigger prefix).
        var triggerPrefix = subroutineName.ToLowerInvariant();
        return WalkFrom(graph, start, [], [], [start.Id], triggerPrefix, decisionsSeen);
    }

    private static IEnumerable<(List<PathStep>, List<string>)>
        WalkFrom(GraphmlGraph graph, GraphmlNode current, List<PathStep> stepsSoFar, List<string> branchLabelsSoFar,
                 HashSet<string> visited, string triggerPrefix, Dictionary<string, Decision> decisionsSeen)
    {
        // Reached a Return → emit the path.
        if (current.ShapeClass == "Return from Subroutine")
        {
            yield return (stepsSoFar, branchLabelsSoFar);
            yield break;
        }

        var outEdges = graph.OutgoingByNodeId[current.Id];
        if (outEdges.Count == 0)
            throw new InvalidDataException(
                $"dead end at non-Return node {current.Id} ('{current.Label}', d5='{current.ShapeClass}')");

        // Skip the start node itself in the steps (just like state-page triggers).
        var stepsToAppend = current.ShapeClass == "Subroutine start"
            ? Enumerable.Empty<PathStep>()
            : NodeToPathSteps(current);

        if (current.ShapeClass == "Test or decision")
        {
            var decisionId = Walker.ContextualDecisionIdPublic(triggerPrefix, current.Label);
            Walker.RecordDecisionPublic(current, decisionId, decisionsSeen);
            var resolvedLabels = ResolveBranchLabels(outEdges);
            for (int i = 0; i < outEdges.Count; i++)
            {
                var edge = outEdges[i];
                if (visited.Contains(edge.Target)) continue;
                var branchLabel = resolvedLabels[i];
                if (string.IsNullOrWhiteSpace(branchLabel))
                    throw new InvalidDataException(
                        $"decision edge {edge.Id} (from {current.Id} '{current.Label}') has no Yes/No label");
                var newSteps = new List<PathStep>(stepsSoFar) { new DecisionBranch(decisionId, branchLabel) };
                var newBranchLabels = new List<string>(branchLabelsSoFar) { branchLabel };
                var newVisited = new HashSet<string>(visited) { edge.Target };
                foreach (var r in WalkFrom(graph, graph.NodesById[edge.Target], newSteps, newBranchLabels, newVisited, triggerPrefix, decisionsSeen))
                    yield return r;
            }
            yield break;
        }

        if (outEdges.Count > 1)
            throw new InvalidDataException(
                $"non-decision node {current.Id} ('{current.Label}', d5='{current.ShapeClass}') has {outEdges.Count} outgoing edges");

        var newSteps2 = new List<PathStep>(stepsSoFar);
        newSteps2.AddRange(stepsToAppend);
        var nextTarget = outEdges[0].Target;
        if (visited.Contains(nextTarget))
            yield break;  // cycle — terminate this branch
        var newVisited2 = new HashSet<string>(visited) { nextTarget };
        foreach (var r in WalkFrom(graph, graph.NodesById[nextTarget], newSteps2, branchLabelsSoFar, newVisited2, triggerPrefix, decisionsSeen))
            yield return r;
    }

    private static IEnumerable<PathStep> NodeToPathSteps(GraphmlNode node)
    {
        if (node.ShapeClass == "Test or decision") yield break;
        var kind = EventResolver.ShapeClassToActionKind(node.ShapeClass);
        if (kind is null) yield break;
        var lines = node.Label
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(EventResolver.NormaliseActionLabel)
            .Where(s => !string.IsNullOrEmpty(s));
        foreach (var line in lines)
            yield return new ActionStep(line, kind);
    }

    private static List<string> ResolveBranchLabels(List<GraphmlEdge> outEdges)
    {
        var labels = outEdges.Select(e => e.Label).ToList();
        if (outEdges.Count != 2) return labels;
        var blanks = labels.Count(string.IsNullOrWhiteSpace);
        if (blanks != 1) return labels;
        var labelled = labels.First(l => !string.IsNullOrWhiteSpace(l));
        var opposite = labelled.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "No"
                     : labelled.Equals("No",  StringComparison.OrdinalIgnoreCase) ? "Yes"
                     : "";
        if (string.IsNullOrEmpty(opposite)) return labels;
        return labels.Select(l => string.IsNullOrWhiteSpace(l) ? opposite : l).ToList();
    }

    private static string BuildSubroutinePathId(int pathIndex, string subroutineName, List<string> branchLabels)
    {
        var idx = pathIndex.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var nameLower = subroutineName.ToLowerInvariant();
        if (branchLabels.Count == 0) return $"t{idx}_{nameLower}";
        return $"t{idx}_{nameLower}_" + string.Join('_', branchLabels.Select(b => b.ToLowerInvariant()));
    }
}
