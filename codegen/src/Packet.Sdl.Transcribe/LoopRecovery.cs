namespace Packet.Sdl.Transcribe;

/// <summary>
/// Recovers SDL loops from graphml back-edges so the walkers can emit a
/// <c>loop_while</c> step instead of silently dropping the cycle.
/// </summary>
/// <remarks>
/// AX.25's data-link figures contain exactly the simple, reducible loops you
/// can read straight off the page: one guarded back-edge, a straight-line
/// action body, no nesting. Two topologies appear:
/// <list type="bullet">
///   <item><b>Test-at-head (while)</b> — the controlling decision sits at the
///   loop entry (e.g. figc4.4/4.5 <c>V(r) I Frame Stored?</c>): one branch runs
///   the body and loops back to the decision, the other exits. Body runs
///   zero-or-more times.</item>
///   <item><b>Test-at-tail (do-while)</b> — the decision sits after the body
///   (e.g. figc4.7 <c>Invoke Retransmission</c>'s <c>V(s) == X?</c>): the body
///   runs, then the decision either loops back to the body's first node or
///   exits. Body runs one-or-more times.</item>
/// </list>
/// Anything that doesn't fit this shape (an unguarded back-edge, a body
/// containing a nested decision or loop, two loops sharing a header) throws —
/// we would rather fail transcription loudly than re-introduce a silently
/// dropped or mis-encoded loop. See m0lte/ax25sdl#44 / #48 / #49.
/// </remarks>
internal static class LoopRecovery
{
    private const string Decision = "Test or decision";

    /// <summary>
    /// One recovered loop, keyed in the returned map by <see cref="HeaderId"/>
    /// — the node at which a walker should emit the loop and jump to the exit.
    /// </summary>
    internal sealed record LoopInfo(
        string HeaderId,
        GraphmlNode TestNode,
        string ContinueBranch,                 // "Yes" / "No" — the decision edge that re-enters the body
        bool TestAtEnd,                        // false = while (head), true = do-while (tail)
        IReadOnlyList<GraphmlNode> BodyNodes,  // ordered, action-only
        string ExitTargetId,                   // node to continue the walk from after the loop
        IReadOnlySet<string> LoopNodeIds);     // header + body + test, marked visited so the exit walk can't re-enter

    /// <summary>
    /// Finds every loop in the graph, keyed by header node id. Throws on any
    /// back-edge that can't be recovered as one of the two supported shapes.
    /// </summary>
    internal static Dictionary<string, LoopInfo> FindLoops(GraphmlGraph graph)
    {
        var loops = new Dictionary<string, LoopInfo>(StringComparer.Ordinal);
        foreach (var backEdge in FindBackEdges(graph))
        {
            var loop = Classify(graph, backEdge);
            if (!loops.TryAdd(loop.HeaderId, loop))
                throw new InvalidDataException(
                    $"{Path.GetFileName(graph.SourcePath)}: two loops share header node {loop.HeaderId} " +
                    $"('{graph.NodesById[loop.HeaderId].Label}'); nested or overlapping loops are not supported.");
        }
        return loops;
    }

    /// <summary>DFS back-edge detection (edge whose target is grey = on the current stack).</summary>
    private static List<GraphmlEdge> FindBackEdges(GraphmlGraph graph)
    {
        const int White = 0, Grey = 1, Black = 2;
        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        var back = new List<GraphmlEdge>();

        void Dfs(string nodeId)
        {
            color[nodeId] = Grey;
            foreach (var e in graph.OutgoingByNodeId[nodeId])
            {
                var c = color.GetValueOrDefault(e.Target, White);
                if (c == Grey) back.Add(e);
                else if (c == White) Dfs(e.Target);
            }
            color[nodeId] = Black;
        }

        foreach (var n in graph.Nodes)
            if (color.GetValueOrDefault(n.Id, White) == White)
                Dfs(n.Id);
        return back;
    }

    private static LoopInfo Classify(GraphmlGraph graph, GraphmlEdge backEdge)
    {
        var u = graph.NodesById[backEdge.Source];   // bottom of the cycle (back-edge source)
        var v = graph.NodesById[backEdge.Target];   // top of the cycle (back-edge target)

        if (v.ShapeClass == Decision)
        {
            // Test-at-head: the back-edge target IS the controlling decision.
            // One of its branches re-enters the body (and forward-reaches u),
            // the other exits.
            var outs = graph.OutgoingByNodeId[v.Id];
            RequireTwoBranches(graph, v, outs);
            var continueEdge = outs.FirstOrDefault(e => ForwardReaches(graph, e.Target, u.Id))
                ?? throw new InvalidDataException(
                    $"{Path.GetFileName(graph.SourcePath)}: head-test loop at decision {v.Id} ('{v.Label}') " +
                    $"has no branch that reaches back-edge source {u.Id}.");
            var exitEdge = outs.First(e => !ReferenceEquals(e, continueEdge));
            var body = CollectBody(graph, continueEdge.Target, terminalId: u.Id, includeTerminal: true);
            var nodeIds = LoopNodeIds(v, body);
            return new LoopInfo(v.Id, v, BranchOf(continueEdge), TestAtEnd: false, body, exitEdge.Target, nodeIds);
        }

        if (u.ShapeClass == Decision)
        {
            // Test-at-tail: the decision sits at the bottom; its back-edge
            // re-enters the body header v, the other branch exits.
            var outs = graph.OutgoingByNodeId[u.Id];
            RequireTwoBranches(graph, u, outs);
            var exitEdge = outs.First(e => e.Target != v.Id);
            var body = CollectBody(graph, v.Id, terminalId: u.Id, includeTerminal: false);
            var nodeIds = LoopNodeIds(u, body);
            return new LoopInfo(v.Id, u, BranchOf(backEdge), TestAtEnd: true, body, exitEdge.Target, nodeIds);
        }

        throw new InvalidDataException(
            $"{Path.GetFileName(graph.SourcePath)}: back-edge {backEdge.Id} ({u.Id} '{u.Label}' → {v.Id} '{v.Label}') " +
            $"forms a loop with no controlling Test-or-decision at either end — SDL loops must be guarded.");
    }

    /// <summary>
    /// Collects the linear, action-only body chain from <paramref name="startId"/>,
    /// following the single out-edge of each node, up to <paramref name="terminalId"/>.
    /// Throws if the body forks, dead-ends, or contains a decision/non-action shape.
    /// </summary>
    private static List<GraphmlNode> CollectBody(GraphmlGraph graph, string startId, string terminalId, bool includeTerminal)
    {
        var body = new List<GraphmlNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var curId = startId;
        while (true)
        {
            if (!seen.Add(curId))
                throw new InvalidDataException(
                    $"{Path.GetFileName(graph.SourcePath)}: loop body starting at {startId} revisits {curId} — unsupported body shape.");
            var cur = graph.NodesById[curId];
            var atTerminal = curId == terminalId;
            if (atTerminal && !includeTerminal)
                break;

            RequireActionShape(graph, cur);
            body.Add(cur);
            if (atTerminal) break;

            var outs = graph.OutgoingByNodeId[curId];
            if (outs.Count != 1)
                throw new InvalidDataException(
                    $"{Path.GetFileName(graph.SourcePath)}: loop body node {curId} ('{cur.Label}') has {outs.Count} out-edges; " +
                    $"loop bodies must be a straight-line action sequence (nested decisions/loops unsupported — refactor as a subroutine).");
            curId = outs[0].Target;
        }
        if (body.Count == 0)
            throw new InvalidDataException(
                $"{Path.GetFileName(graph.SourcePath)}: recovered an empty loop body starting at {startId}.");
        return body;
    }

    /// <summary>
    /// True if, following single out-edges from <paramref name="fromId"/> (a
    /// straight-line action chain), we reach <paramref name="targetId"/>. Stops
    /// at any decision, fork, or terminal — those mark the exit branch, not the body.
    /// </summary>
    private static bool ForwardReaches(GraphmlGraph graph, string fromId, string targetId)
    {
        var curId = fromId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(curId))
        {
            if (curId == targetId) return true;
            var cur = graph.NodesById[curId];
            if (cur.ShapeClass == Decision) return false;
            var outs = graph.OutgoingByNodeId[curId];
            if (outs.Count != 1) return false;
            curId = outs[0].Target;
        }
        return false;
    }

    private static void RequireTwoBranches(GraphmlGraph graph, GraphmlNode decision, List<GraphmlEdge> outs)
    {
        if (outs.Count != 2)
            throw new InvalidDataException(
                $"{Path.GetFileName(graph.SourcePath)}: loop-controlling decision {decision.Id} ('{decision.Label}') " +
                $"has {outs.Count} out-edges; expected exactly two (continue + exit).");
        foreach (var e in outs)
            if (string.IsNullOrWhiteSpace(e.Label))
                throw new InvalidDataException(
                    $"{Path.GetFileName(graph.SourcePath)}: loop-controlling decision {decision.Id} ('{decision.Label}') " +
                    $"edge {e.Id} has no Yes/No label.");
    }

    private static void RequireActionShape(GraphmlGraph graph, GraphmlNode node)
    {
        if (node.ShapeClass == Decision)
            throw new InvalidDataException(
                $"{Path.GetFileName(graph.SourcePath)}: loop body contains a nested decision {node.Id} ('{node.Label}'); " +
                $"unsupported — refactor as a subroutine.");
        if (EventResolver.ShapeClassToActionKind(node.ShapeClass) is null)
            throw new InvalidDataException(
                $"{Path.GetFileName(graph.SourcePath)}: loop body contains non-action node {node.Id} " +
                $"('{node.Label}', d5='{node.ShapeClass}'); only action shapes are allowed in a loop body.");
    }

    /// <summary>Reduce a figure edge label to canonical "Yes"/"No".</summary>
    private static string BranchOf(GraphmlEdge edge)
    {
        var t = edge.Label.Trim();
        if (t.StartsWith("Yes", StringComparison.OrdinalIgnoreCase)) return "Yes";
        if (t.StartsWith("No", StringComparison.OrdinalIgnoreCase)) return "No";
        throw new InvalidDataException($"loop-controlling edge {edge.Id} has non-Yes/No label '{edge.Label}'.");
    }

    private static HashSet<string> LoopNodeIds(GraphmlNode test, IReadOnlyList<GraphmlNode> body)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { test.Id };
        foreach (var b in body) ids.Add(b.Id);
        return ids;
    }

    /// <summary>
    /// Converts a recovered loop's body nodes into action path steps. The body
    /// is guaranteed action-only by <see cref="CollectBody"/>; multi-line action
    /// boxes become one step per line, matching <c>NodeToPathSteps</c>.
    /// </summary>
    internal static List<PathStep> BuildBodySteps(IReadOnlyList<GraphmlNode> bodyNodes)
    {
        var steps = new List<PathStep>();
        foreach (var node in bodyNodes)
        {
            var kind = EventResolver.ShapeClassToActionKind(node.ShapeClass)!;  // non-null: CollectBody enforced it
            var lines = node.Label
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
                .Select(EventResolver.NormaliseActionLabel)
                .Where(s => !string.IsNullOrEmpty(s));
            foreach (var line in lines)
                steps.Add(new ActionStep(line, kind));
        }
        return steps;
    }
}
