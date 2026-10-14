using System.Xml.Linq;

namespace UrlShortener.ArchitectureTests;

/// <summary>A production project as declared in its .csproj: direct project, package and framework references.</summary>
internal sealed record SourceProject(
    string Name,
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences,
    IReadOnlySet<string> FrameworkReferences);

/// <summary>
/// The dependency graph of everything under <c>src/</c>, read from the .csproj files.
/// Reading project files (not assemblies) keeps the rules enforceable for empty projects too.
/// </summary>
internal sealed class ProjectGraph
{
    private readonly Dictionary<string, SourceProject> _projects;

    private ProjectGraph(Dictionary<string, SourceProject> projects) => _projects = projects;

    public static ProjectGraph Load()
    {
        var srcRoot = Path.Combine(FindRepositoryRoot(), "src");
        var projects = Directory
            .EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(Parse)
            .ToDictionary(p => p.Name);
        return new ProjectGraph(projects);
    }

    public IReadOnlyCollection<SourceProject> All => _projects.Values;

    public SourceProject Get(string name) =>
        _projects.TryGetValue(name, out var project)
            ? project
            : throw new InvalidOperationException($"Project '{name}' does not exist under src/.");

    public bool Contains(string name) => _projects.ContainsKey(name);

    /// <summary>Every project reachable through ProjectReferences, excluding the project itself.</summary>
    public IReadOnlySet<string> TransitiveProjectReferences(string name)
    {
        var visited = new HashSet<string>();
        var pending = new Stack<string>(Get(name).ProjectReferences);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current) || !_projects.TryGetValue(current, out var project))
            {
                continue;
            }

            foreach (var next in project.ProjectReferences)
            {
                pending.Push(next);
            }
        }

        return visited;
    }

    private static SourceProject Parse(string path)
    {
        var document = XDocument.Load(path);

        HashSet<string> Collect(string element, string attribute, Func<string, string> map) =>
            document.Descendants(element)
                .Select(e => e.Attribute(attribute)?.Value)
                .OfType<string>()
                .Select(map)
                .ToHashSet(StringComparer.Ordinal);

        return new SourceProject(
            Name: Path.GetFileNameWithoutExtension(path),
            ProjectReferences: Collect("ProjectReference", "Include", v => Path.GetFileNameWithoutExtension(v.Replace('\\', '/'))),
            PackageReferences: Collect("PackageReference", "Include", v => v),
            FrameworkReferences: Collect("FrameworkReference", "Include", v => v));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (global.json).");
    }
}
