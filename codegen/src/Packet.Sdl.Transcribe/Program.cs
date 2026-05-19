using CommandLine;

namespace Packet.Sdl.Transcribe;

public static class Program
{
    public static int Main(string[] args)
    {
        return Parser.Default.ParseArguments<Options>(args)
            .MapResult(Run, _ => 1);
    }

    private static int Run(Options opt)
    {
        var inputs = ResolveInputs(opt).ToList();
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine($"::error::No graphml files found at {opt.Input}.");
            return 1;
        }

        var warnings = 0;
        foreach (var input in inputs)
        {
            try
            {
                var graph = GraphmlReader.Load(input);
                string yaml;
                string summary;
                if (SubroutinesWalker.IsSubroutinesPage(graph))
                {
                    var page = SubroutinesWalker.Walk(graph, input);
                    page.SourceGraphmlPath = input;
                    yaml = YamlEmitter.EmitSubroutines(page);
                    var totalDecisions = page.Subroutines.Sum(s => s.Decisions.Count);
                    summary = $"{page.Subroutines.Count} subroutines, {totalDecisions} decisions";
                }
                else
                {
                    var page = Walker.Walk(graph, input);
                    page.SourceGraphmlPath = input;
                    yaml = YamlEmitter.Emit(page);
                    summary = $"{page.Transitions.Count} transitions, {page.Decisions.Count} decisions";
                }

                var outPath = ResolveOutputPath(input, opt);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllText(outPath, yaml);
                Console.WriteLine($"  {input}  →  {outPath}  ({summary})");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"::warning::{input}: {ex.Message}");
                warnings++;
            }
        }

        if (warnings > 0)
        {
            Console.Error.WriteLine($"completed with {warnings} warning(s).");
        }
        return warnings == 0 ? 0 : 2;
    }

    private static IEnumerable<string> ResolveInputs(Options opt)
    {
        if (File.Exists(opt.Input)) return [opt.Input];
        if (Directory.Exists(opt.Input))
            return Directory.EnumerateFiles(opt.Input, "*.graphml", SearchOption.AllDirectories);
        return [];
    }

    private static string ResolveOutputPath(string graphmlPath, Options opt)
    {
        // disconnected.graphml  →  disconnected.sdl.yaml
        // DataLink_Disconnected.graphml  →  disconnected.sdl.yaml  (PascalCase → snake_case)
        var filename = Path.GetFileNameWithoutExtension(graphmlPath);
        var underscore = filename.IndexOf('_');
        var statePart = underscore < 0 ? filename : filename[(underscore + 1)..];
        // PascalCase → snake_case for the yaml filename.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < statePart.Length; i++)
        {
            if (i > 0 && char.IsUpper(statePart[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(statePart[i]));
        }
        var yamlName = sb + ".sdl.yaml";
        return Path.Combine(opt.OutputDir, yamlName);
    }
}

public class Options
{
    [Option('i', "input", Required = true,
        HelpText = "Path to a single graphml file or a directory containing graphml files (recursive).")]
    public string Input { get; init; } = "";

    [Option('o', "output-dir", Required = true,
        HelpText = "Directory to write the generated *.sdl.yaml files into.")]
    public string OutputDir { get; init; } = "";
}
