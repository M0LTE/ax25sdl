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
            Source = new PageSource
            {
                Spec = "ax.25.2.2.4_Oct_25",
                Figure = FigureForState(state),
            },
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

        // Drop decisions whose other branch was cycle-skipped. After cycle
        // truncation, the surviving branch's transition is effectively
        // unconditional — by the time the runtime reaches it, the loop
        // predicate has terminated — so the DecisionBranch step is
        // semantically redundant and codegen's bilateral-branch lint trips
        // on the orphaned one-sided decision. Strip both.
        PruneOneSidedDecisions(page);

        return page;
    }

    private static void PruneOneSidedDecisions(SdlPage page)
    {
        var branchesByDecision = new Dictionary<string, HashSet<string>>();
        foreach (var t in page.Transitions)
            foreach (var step in t.Path.OfType<DecisionBranch>())
            {
                if (!branchesByDecision.TryGetValue(step.DecisionId, out var set))
                    branchesByDecision[step.DecisionId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(step.Branch);
            }
        var oneSided = branchesByDecision
            .Where(kv => kv.Value.Count < 2)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (oneSided.Count == 0) return;

        page.Decisions.RemoveAll(d => oneSided.Contains(d.Id));
        foreach (var t in page.Transitions)
        {
            for (int i = t.Path.Count - 1; i >= 0; i--)
                if (t.Path[i] is DecisionBranch db && oneSided.Contains(db.DecisionId))
                    t.Path.RemoveAt(i);
        }
    }

    /// <summary>
    /// Maps a state name to its figc4.x figure designator. AX.25 v2.2 §C
    /// lays out one state per figure. If the tool encounters a state name
    /// not in this map (e.g. a future state machine), the source.figure
    /// field falls back to a placeholder — that's a soft default; the
    /// loader needs *something* but doesn't act on the value.
    /// </summary>
    private static string FigureForState(string state) => state switch
    {
        "Disconnected"          => "figc4.1",
        "AwaitingConnection"    => "figc4.2",
        "AwaitingRelease"       => "figc4.3",
        "Connected"             => "figc4.4",
        "TimerRecovery"         => "figc4.5",
        "AwaitingV22Connection" => "figc4.6",
        _ => "figc4.x",
    };

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
                // Strip annotations like "Yes (Note: assumed; missing from spec)"
                // down to bare "Yes" / "No" — the codegen validator requires
                // exactly these two values for branch labels.
                var canonicalBranch = NormaliseBranchLabel(branchLabel);
                var newPath = new List<PathStep>(pathSoFar);
                newPath.Add(new DecisionBranch(decisionId, canonicalBranch));
                var newBranchLabels = new List<string>(branchLabelsSoFar) { canonicalBranch };
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
        // "0 Disconnected"          → "Disconnected"
        // "1 Awaiting Connection"   → "AwaitingConnection"
        // "5 Awaiting V2.2 Connection" → "AwaitingV22Connection" (drop dots)
        // "Awaiting 2.2 Connection" (LLM-era typo: missing V) → "AwaitingV22Connection"
        //
        // The source graphmls use multiple inconsistent labels for the same
        // SDL state. We normalise: strip leading figure-column number, fuse
        // spaces into PascalCase, drop dots, then apply a small alias map
        // for known typo variants. The aliases will go away when Tom
        // redraws the graphmls with consistent labels.
        var trimmed = label.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        var stateText = firstSpace < 0 ? trimmed : trimmed[(firstSpace + 1)..];
        var raw = string.Concat(stateText.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace(".", "");
        return StateNameAliases.TryGetValue(raw, out var canon) ? canon : raw;
    }

    /// <summary>
    /// Maps inconsistent state-name labels in the current graphmls to their
    /// canonical form. Empty once the graphmls are consistent.
    /// </summary>
    private static readonly Dictionary<string, string> StateNameAliases = new(StringComparer.Ordinal)
    {
        ["Awaiting22Connection"] = "AwaitingV22Connection",  // missing "V" in some graphmls
    };

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
        // Branch labels are sanitised to their leading word — authors sometimes
        // annotate a branch with parenthetical notes (e.g. "Yes (Note: assumed)")
        // which would break the id otherwise.
        var prefix = $"t{triggerIndex:D2}_{triggerEvent.ToLowerInvariant()}";
        if (branchLabels.Count == 0) return prefix;
        var sanitised = branchLabels.Select(SanitiseBranchLabelForId);
        return prefix + "_" + string.Join('_', sanitised);
    }

    private static string SanitiseBranchLabelForId(string label)
        => NormaliseBranchLabel(label).ToLowerInvariant();

    /// <summary>
    /// Reduces a free-form branch label to its canonical "Yes"/"No" form by
    /// taking the leading word. Authors sometimes annotate a branch with
    /// parenthetical context ("Yes (Note: assumed; missing from spec)") —
    /// those notes belong in commentary, not in the branch label itself.
    /// </summary>
    private static string NormaliseBranchLabel(string label)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in label.Trim())
        {
            if (char.IsLetter(c)) sb.Append(c);
            else break;
        }
        var bare = sb.ToString();
        // Title-case it: "yes" → "Yes" / "no" → "No".
        if (bare.Length == 0) return "";
        return char.ToUpperInvariant(bare[0]) + bare[1..].ToLowerInvariant();
    }
}
