using System.Text.RegularExpressions;

namespace Packet.Sdl.Transcribe;

/// <summary>
/// Walks the graphml graph and builds the SDL IR (one SdlPage).
/// </summary>
public static class Walker
{
    private static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];

    public static SdlPage Walk(GraphmlGraph graph, string graphmlFilename)
    {
        var (machine, state) = MachineAndStateFromFilename(graphmlFilename);

        var entry = FindEntryStateNode(graph, state);

        // Triggers are the immediate out-neighbours of the entry State,
        // sorted left-to-right by x position to match the figure's column
        // order.
        var triggerEdges = graph.OutgoingByNodeId[entry.Id]
            .Select(e => new { Edge = e, Node = graph.NodesById[e.Target] })
            .OrderBy(p => p.Node.X)
            .ToList();

        var page = new SdlPage
        {
            Machine = machine,
            State = state,
        };

        var decisionsSeen = new Dictionary<string, Decision>();
        int triggerIndex = 0;

        foreach (var trigger in triggerEdges)
        {
            triggerIndex++;
            var triggerNode = trigger.Node;
            var triggerEvent = EventResolver.ResolveTriggerEvent(triggerNode.Label, triggerNode.ShapeClass);

            // Save column: the trigger feeds directly into a Save shape with
            // no further outgoing edges. The event is queued, no transition
            // fires.
            if (LeadsToSaveTerminus(graph, triggerNode))
            {
                if (!page.Save.Contains(triggerEvent))
                    page.Save.Add(triggerEvent);
                continue;
            }

            // Decisions are recorded per-trigger so an "Able to Establish?"
            // decision under SABM gets a different id from the same question
            // under SABME — matches the existing yaml convention.
            var triggerPrefix = TriggerPrefixFromEvent(triggerEvent);

            // From the trigger, recursively walk paths. Decisions cause branching.
            foreach (var (path, next, branchLabels) in WalkFromTrigger(graph, triggerNode, triggerPrefix, decisionsSeen))
            {
                var transitionId = BuildTransitionId(triggerIndex, triggerEvent, branchLabels);
                var t = new Transition
                {
                    Id = transitionId,
                    On = triggerEvent,
                    Next = next,
                };
                t.Path.AddRange(path);
                page.Transitions.Add(t);
            }
        }

        // Order decisions by their first-encountered transition index.
        page.Decisions.AddRange(decisionsSeen.Values);

        return page;
    }

    private static (string Machine, string State) MachineAndStateFromFilename(string filename)
    {
        // "DataLink_Disconnected.graphml" → ("data_link", "Disconnected")
        var bare = Path.GetFileNameWithoutExtension(filename);
        var underscoreAt = bare.IndexOf('_');
        if (underscoreAt < 0)
            throw new InvalidDataException(
                $"graphml filename '{filename}' doesn't follow the <Machine>_<State>.graphml convention.");
        var machinePascal = bare[..underscoreAt];                 // "DataLink"
        var statePascal = bare[(underscoreAt + 1)..];             // "Disconnected"
        return (PascalCaseToSnakeCase(machinePascal), statePascal);
    }

    private static string PascalCaseToSnakeCase(string pascal)
    {
        // "DataLink" → "data_link"
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Finds the State node that's the entry to this page. There are
    /// usually many State-shape-class nodes (one per transition terminus),
    /// but only one has outgoing edges to triggers — the entry.
    /// </summary>
    private static GraphmlNode FindEntryStateNode(GraphmlGraph graph, string expectedState)
    {
        var stateNodes = graph.Nodes.Where(n => n.ShapeClass == "State").ToList();
        var candidates = stateNodes
            .Where(n => graph.OutgoingByNodeId[n.Id].Count > 0)
            .ToList();
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0)
            throw new InvalidDataException(
                $"no State node with outgoing edges found — can't identify entry state.");
        // Multiple candidates: prefer the one whose label matches expectedState.
        var byLabel = candidates.FirstOrDefault(n => StateLabelMatches(n.Label, expectedState));
        if (byLabel is not null) return byLabel;
        throw new InvalidDataException(
            $"multiple State nodes with outgoing edges ({string.Join(", ", candidates.Select(c => c.Id))}); none match expected state '{expectedState}'.");
    }

    private static bool StateLabelMatches(string label, string expectedState)
    {
        // State labels are formatted like "0 Disconnected" (number + state name).
        // The number is the figc4.x table-column index.
        var trimmed = label.Trim();
        var spaceAt = trimmed.IndexOf(' ');
        if (spaceAt < 0) return trimmed == expectedState;
        return trimmed[(spaceAt + 1)..] == expectedState;
    }

    /// <summary>
    /// Walks every path from a trigger to a terminal state, enumerating
    /// decision branches into separate paths. Returns one tuple per path.
    /// </summary>
    private static IEnumerable<(List<PathStep> Path, string Next, List<string> BranchLabels)>
        WalkFromTrigger(GraphmlGraph graph, GraphmlNode triggerNode, string triggerPrefix,
                        Dictionary<string, Decision> decisionsSeen)
    {
        return WalkFrom(graph, triggerNode, [], [], [triggerNode.Id], triggerPrefix, decisionsSeen);
    }

    private static IEnumerable<(List<PathStep>, string, List<string>)>
        WalkFrom(GraphmlGraph graph, GraphmlNode current, List<PathStep> pathSoFar, List<string> branchLabelsSoFar,
                 HashSet<string> visitedInThisWalk, string triggerPrefix,
                 Dictionary<string, Decision> decisionsSeen)
    {
        var outEdges = graph.OutgoingByNodeId[current.Id];

        // Reached a State terminus → yield the path.
        if (outEdges.Count == 0 && current.ShapeClass == "State")
        {
            var nextState = ParseStateName(current.Label);
            yield return (pathSoFar, nextState, branchLabelsSoFar);
            yield break;
        }

        if (outEdges.Count == 0)
            throw new InvalidDataException(
                $"dead end at non-terminal node {current.Id} ('{current.Label}', d5='{current.ShapeClass}')");

        // Skip the trigger itself in the path (it becomes the `on:` field instead).
        // For every other node, append step(s) before recursing.
        var stepsToAppend = NodeToPathSteps(current, decisionsSeen);

        if (current.ShapeClass == "Test or decision")
        {
            // Each outgoing edge carries a branch label (Yes / No / sometimes other).
            // Append the decision-branch step then recurse along each edge.
            // Skip edges that lead back to an already-visited node (SDL loop body
            // — the existing yaml convention is to enumerate only the non-cycling
            // branch and document the loop in a transition-level note).
            var decisionId = ContextualDecisionId(triggerPrefix, current.Label);
            RecordDecision(current, decisionId, decisionsSeen);
            var resolvedBranchLabels = ResolveBranchLabels(current, outEdges);
            for (int i = 0; i < outEdges.Count; i++)
            {
                var edge = outEdges[i];
                if (visitedInThisWalk.Contains(edge.Target))
                    continue;
                var branchLabel = resolvedBranchLabels[i];
                if (string.IsNullOrWhiteSpace(branchLabel))
                    throw new InvalidDataException(
                        $"decision edge {edge.Id} (from {current.Id} '{current.Label}') has no Yes/No label and can't be inferred");
                var newPath = new List<PathStep>(pathSoFar);
                newPath.Add(new DecisionBranch(decisionId, branchLabel));
                var newBranchLabels = new List<string>(branchLabelsSoFar) { branchLabel };
                var newVisited = new HashSet<string>(visitedInThisWalk) { edge.Target };
                foreach (var result in WalkFrom(graph, graph.NodesById[edge.Target], newPath, newBranchLabels, newVisited, triggerPrefix, decisionsSeen))
                    yield return result;
            }
            yield break;
        }

        // Non-decision: append this node's steps and continue along the
        // single outgoing edge (the SDL convention — every non-decision
        // node has exactly one out-edge).
        if (outEdges.Count > 1 && current.ShapeClass is not ("State" or "Subroutine start"))
            throw new InvalidDataException(
                $"non-decision node {current.Id} ('{current.Label}', d5='{current.ShapeClass}') has {outEdges.Count} outgoing edges — only Test-or-decision shapes are allowed to fan out.");

        var newPathLinear = new List<PathStep>(pathSoFar);
        newPathLinear.AddRange(stepsToAppend);

        if (current.ShapeClass == "State")
        {
            // State node mid-path — that means we're walking THROUGH another
            // state machine's state, which doesn't happen in well-formed pages.
            // Defensive: treat as a terminus.
            yield return (newPathLinear, ParseStateName(current.Label), branchLabelsSoFar);
            yield break;
        }

        var nextTarget = outEdges[0].Target;
        if (visitedInThisWalk.Contains(nextTarget))
        {
            // Defensive: a non-decision node forming a cycle. Terminate
            // the walk at the cycle origin; the caller will see a
            // truncated transition. Shouldn't happen in well-formed SDL.
            yield break;
        }
        var nextNode = graph.NodesById[nextTarget];
        var newVisitedLinear = new HashSet<string>(visitedInThisWalk) { nextTarget };
        foreach (var result in WalkFrom(graph, nextNode, newPathLinear, branchLabelsSoFar, newVisitedLinear, triggerPrefix, decisionsSeen))
            yield return result;
    }

    private static IEnumerable<PathStep> NodeToPathSteps(GraphmlNode node, Dictionary<string, Decision> decisionsSeen)
    {
        if (node.ShapeClass == "Test or decision")
        {
            // Decisions are handled by the caller (it emits one branch step
            // per outgoing edge AND records the decision via its contextual id).
            yield break;
        }

        var kind = EventResolver.ShapeClassToActionKind(node.ShapeClass);
        if (kind is null)
        {
            // State / trigger / subroutine-start / return — these aren't action nodes.
            // Walker handles them as path boundaries; nothing to emit.
            yield break;
        }

        // Multi-line node = one path step per line.
        var lines = node.Label
            .Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(EventResolver.NormaliseActionLabel)
            .Where(s => !string.IsNullOrEmpty(s));
        foreach (var line in lines)
            yield return new ActionStep(line, kind);
    }

    /// <summary>Shared with SubroutinesWalker.</summary>
    internal static void RecordDecisionPublic(GraphmlNode node, string contextualId, Dictionary<string, Decision> decisionsSeen)
        => RecordDecision(node, contextualId, decisionsSeen);

    /// <summary>Shared with SubroutinesWalker.</summary>
    internal static string ContextualDecisionIdPublic(string triggerPrefix, string questionLabel)
        => ContextualDecisionId(triggerPrefix, questionLabel);

    private static void RecordDecision(GraphmlNode node, string contextualId, Dictionary<string, Decision> decisionsSeen)
    {
        if (decisionsSeen.ContainsKey(contextualId)) return;
        decisionsSeen[contextualId] = new Decision
        {
            Id = contextualId,
            Question = node.Label.Trim(),
            Predicate = DecisionPredicate(node.Label),
        };
    }

    private static string ContextualDecisionId(string triggerPrefix, string questionLabel)
    {
        // "SABM" + "Able to Establish?" → "sabm_able_to_establish"
        var bare = NormaliseQuestionToId(questionLabel);
        return string.IsNullOrEmpty(triggerPrefix) ? bare : $"{triggerPrefix}_{bare}";
    }

    private static string NormaliseQuestionToId(string label)
    {
        // "P == 1?" → "p_eq_1"   |   "Able to Establish?" → "able_to_establish"
        var stripped = label.TrimEnd('?').Trim();
        var snake = Regex.Replace(stripped, @"\s+", "_")
                         .Replace("==", "eq")
                         .Replace("!=", "ne")
                         .Replace(">=", "ge")
                         .Replace("<=", "le")
                         .Replace(">",  "gt")
                         .Replace("<",  "lt")
                         .Replace("(",  "")
                         .Replace(")",  "")
                         .ToLowerInvariant();
        return Regex.Replace(snake, @"_+", "_").Trim('_');
    }

    private static string DecisionPredicate(string label)
    {
        // Predicates preserve original case for spec-variable tokens
        // (single-letter or all-caps — P, F, V, I) and lowercase everything
        // else. Matches the existing yaml convention: P_eq_1 (keep P), but
        // able_to_establish (lowercase the regular words).
        var stripped = label.TrimEnd('?').Trim();
        var snake = Regex.Replace(stripped, @"\s+", "_")
                         .Replace("==", "eq")
                         .Replace("!=", "ne")
                         .Replace(">=", "ge")
                         .Replace("<=", "le")
                         .Replace(">",  "gt")
                         .Replace("<",  "lt")
                         .Replace("(",  "")
                         .Replace(")",  "");
        var collapsed = Regex.Replace(snake, @"_+", "_").Trim('_');
        var tokens = collapsed.Split('_', StringSplitOptions.RemoveEmptyEntries)
                              .Select(PreserveSpecVariable);
        return string.Join('_', tokens);
    }

    private static string PreserveSpecVariable(string token)
    {
        // All-caps token → leave as-is. Otherwise lowercase.
        return Regex.IsMatch(token, "^[A-Z]+[0-9]*$") ? token : token.ToLowerInvariant();
    }

    /// <summary>
    /// Turns an event name (DL_DISCONNECT_request, SABM_received) into a
    /// short prefix used for contextual decision ids (dl_disconnect, sabm).
    /// </summary>
    private static string TriggerPrefixFromEvent(string triggerEvent)
    {
        var lower = triggerEvent.ToLowerInvariant();
        // Strip common suffixes that don't add disambiguating value.
        foreach (var suffix in new[] { "_received", "_request", "_indication", "_confirm" })
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
            {
                lower = lower[..^suffix.Length];
                break;
            }
        }
        return lower;
    }

    private static string ParseStateName(string label)
    {
        // "0 Disconnected"  → "Disconnected"
        // "1 Awaiting Connection" → "AwaitingConnection"  (strip the leading
        // figure-column number AND fuse internal whitespace into PascalCase
        // — yaml convention per the schema's [A-Z][A-Za-z0-9]* state name pattern).
        var trimmed = label.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var stateText = firstSpace < 0 ? trimmed : trimmed[(firstSpace + 1)..];
        return string.Concat(stateText.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// For a 2-branch decision with one labeled edge and one unlabeled,
    /// infer the unlabeled edge's label as the opposite. yEd authors
    /// occasionally forget to label one edge; this fixes that without
    /// silently picking the wrong branch.
    /// </summary>
    private static List<string> ResolveBranchLabels(GraphmlNode decision, List<GraphmlEdge> outEdges)
    {
        var labels = outEdges.Select(e => e.Label).ToList();
        if (outEdges.Count != 2) return labels;
        var blanks = labels.Where(string.IsNullOrWhiteSpace).Count();
        if (blanks != 1) return labels;  // 0 → all good; 2 → can't infer
        var labelled = labels.First(l => !string.IsNullOrWhiteSpace(l));
        var opposite = labelled.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "No"
                     : labelled.Equals("No",  StringComparison.OrdinalIgnoreCase) ? "Yes"
                     : "";  // give up if the existing label isn't Yes/No
        if (string.IsNullOrEmpty(opposite)) return labels;
        return labels.Select(l => string.IsNullOrWhiteSpace(l) ? opposite : l).ToList();
    }

    /// <summary>
    /// True if walking from the trigger reaches a "Save a signal until a
    /// new state is reached" shape with no further outgoing edges, optionally
    /// after passing through (single-out-edge) intermediate nodes.
    /// </summary>
    private static bool LeadsToSaveTerminus(GraphmlGraph graph, GraphmlNode trigger)
    {
        var cur = trigger;
        var guard = 0;
        while (true)
        {
            if (++guard > 50) return false;  // pathological case, fall through to normal walk
            var outs = graph.OutgoingByNodeId[cur.Id];
            if (outs.Count != 1) return false;
            cur = graph.NodesById[outs[0].Target];
            if (cur.ShapeClass == "Save a signal until a new state is reached")
                return graph.OutgoingByNodeId[cur.Id].Count == 0;
        }
    }

    private static string BuildTransitionId(int triggerIndex, string triggerEvent, List<string> branchLabels)
    {
        // "t01_dl_disconnect_request" or "t12_sabm_received_yes" / "_no" if branched.
        var prefix = $"t{triggerIndex:D2}_{triggerEvent.ToLowerInvariant()}";
        if (branchLabels.Count == 0) return prefix;
        return prefix + "_" + string.Join('_', branchLabels.Select(b => b.ToLowerInvariant()));
    }
}
