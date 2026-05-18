using System.Xml.Linq;

namespace Packet.Sdl.Transcribe;

/// <summary>
/// Loads a yEd graphml file into the in-memory model below.
/// </summary>
/// <remarks>
/// We trust the yEd schema described at the top of every graphml file:
///   d4 = node url           (unused)
///   d5 = node description   ← SDL shape class (authoritative — see CLAUDE.md)
///   d6 = node graphics      ← we extract the label + x/y geometry from this
///   d9 = edge description   (unused)
///   d10 = edge graphics     ← we extract the edge label (e.g. "Yes" / "No") from this
/// All other keys are presentational and ignored.
/// </remarks>
public static class GraphmlReader
{
    private static readonly XNamespace G = "http://graphml.graphdrawing.org/xmlns";
    private static readonly XNamespace Y = "http://www.yworks.com/xml/graphml";

    public static GraphmlGraph Load(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidDataException($"empty graphml: {path}");

        var nodes = new List<GraphmlNode>();
        foreach (var nodeEl in root.Descendants(G + "node"))
        {
            var id = (string?)nodeEl.Attribute("id") ?? throw new InvalidDataException("node missing id");
            var shapeClass = ReadNodeShapeClass(nodeEl);
            var (label, x, y) = ReadNodeLabelAndPosition(nodeEl);
            nodes.Add(new GraphmlNode(id, shapeClass, label, x, y));
        }

        var edges = new List<GraphmlEdge>();
        foreach (var edgeEl in root.Descendants(G + "edge"))
        {
            var id = (string?)edgeEl.Attribute("id") ?? throw new InvalidDataException("edge missing id");
            var source = (string?)edgeEl.Attribute("source") ?? throw new InvalidDataException("edge missing source");
            var target = (string?)edgeEl.Attribute("target") ?? throw new InvalidDataException("edge missing target");
            var label = ReadEdgeLabel(edgeEl);
            edges.Add(new GraphmlEdge(id, source, target, label));
        }

        return new GraphmlGraph(path, nodes, edges);
    }

    private static string ReadNodeShapeClass(XElement nodeEl)
    {
        // d5 carries the SDL shape class, e.g. "Signal reception from Lower Layer"
        var d5 = nodeEl.Elements(G + "data").FirstOrDefault(e => (string?)e.Attribute("key") == "d5");
        return d5?.Value.Trim() ?? "";
    }

    private static (string Label, double X, double Y) ReadNodeLabelAndPosition(XElement nodeEl)
    {
        // d6 carries the y:SVGNode (and other node-graphics variants). We need
        // the inner y:NodeLabel text and the y:Geometry x/y.
        var d6 = nodeEl.Elements(G + "data").FirstOrDefault(e => (string?)e.Attribute("key") == "d6");
        if (d6 is null) return ("", 0, 0);

        var label = d6.Descendants(Y + "NodeLabel").FirstOrDefault()?.Value.Trim() ?? "";
        var geom = d6.Descendants(Y + "Geometry").FirstOrDefault();
        var x = geom is null ? 0 : double.Parse((string?)geom.Attribute("x") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        var y = geom is null ? 0 : double.Parse((string?)geom.Attribute("y") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        return (label, x, y);
    }

    private static string ReadEdgeLabel(XElement edgeEl)
    {
        // d10 carries y:PolyLineEdge → y:EdgeLabel. EdgeLabel.Value contains
        // the visible label text first ("Yes"/"No") followed by child
        // elements (LabelModel, ModelParameter, ...), so we take only the
        // first text-node child.
        var d10 = edgeEl.Elements(G + "data").FirstOrDefault(e => (string?)e.Attribute("key") == "d10");
        if (d10 is null) return "";
        var labelEl = d10.Descendants(Y + "EdgeLabel").FirstOrDefault();
        if (labelEl is null) return "";
        // EdgeLabel.FirstNode is the literal text content before the nested element model.
        var firstText = labelEl.Nodes().OfType<XText>().FirstOrDefault();
        return firstText?.Value.Trim() ?? "";
    }
}

public sealed record GraphmlNode(string Id, string ShapeClass, string Label, double X, double Y);
public sealed record GraphmlEdge(string Id, string Source, string Target, string Label);

public sealed class GraphmlGraph
{
    public string SourcePath { get; }
    public IReadOnlyList<GraphmlNode> Nodes { get; }
    public IReadOnlyList<GraphmlEdge> Edges { get; }
    public IReadOnlyDictionary<string, GraphmlNode> NodesById { get; }
    public IReadOnlyDictionary<string, List<GraphmlEdge>> OutgoingByNodeId { get; }
    public IReadOnlyDictionary<string, List<GraphmlEdge>> IncomingByNodeId { get; }

    public GraphmlGraph(string path, IReadOnlyList<GraphmlNode> nodes, IReadOnlyList<GraphmlEdge> edges)
    {
        SourcePath = path;
        Nodes = nodes;
        Edges = edges;
        NodesById = nodes.ToDictionary(n => n.Id);
        OutgoingByNodeId = nodes.ToDictionary(n => n.Id, n => edges.Where(e => e.Source == n.Id).ToList());
        IncomingByNodeId = nodes.ToDictionary(n => n.Id, n => edges.Where(e => e.Target == n.Id).ToList());
    }
}
