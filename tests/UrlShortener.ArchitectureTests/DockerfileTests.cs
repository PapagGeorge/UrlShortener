using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UrlShortener.ArchitectureTests;

/// <summary>
/// The dev machine runs no Docker, so a broken Dockerfile would only show up in CI. These checks catch the usual mistakes
/// (a forgotten project file breaks restore, a root user, the wrong publish target) without building anything.
/// </summary>
public sealed class DockerfileTests
{
    public static TheoryData<string, string> Images => new()
    {
        { "src/Gateway/Dockerfile", "src/Gateway/UrlShortener.Gateway/UrlShortener.Gateway.csproj" },
        { "src/Services/WriteService/Dockerfile", "src/Services/WriteService/UrlShortener.WriteService.Api/UrlShortener.WriteService.Api.csproj" },
        { "src/Services/ReadService/Dockerfile", "src/Services/ReadService/UrlShortener.ReadService.Api/UrlShortener.ReadService.Api.csproj" },
    };

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UrlShortener.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string[] Lines(string dockerfile) =>
        File.ReadAllLines(Path.Combine(RepoRoot(), dockerfile)).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();

    private static HashSet<string> ProjectClosure(string entry)
    {
        var root = RepoRoot();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([entry]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(current)!;
            foreach (var reference in XDocument.Load(Path.Combine(root, current)).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")!.Value.Replace('\\', '/');
                pending.Push(Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(root, directory, include))).Replace('\\', '/'));
            }
        }

        return seen;
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Dockerfile_CopiesEveryProjectFileItNeedsBeforeRestore(string dockerfile, string entry)
    {
        var copied = Lines(dockerfile)
            .Where(l => l.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase) && !l.Contains("--from", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
            .Where(source => source.EndsWith(".csproj", StringComparison.Ordinal))
            .ToHashSet();

        ProjectClosure(entry).Except(copied).ShouldBeEmpty($"{dockerfile} must COPY these project files before `dotnet restore`");
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Dockerfile_CopiesTheBuildConfigurationBeforeRestore(string dockerfile, string entry)
    {
        _ = entry;
        var text = string.Join('\n', Lines(dockerfile));

        text.ShouldContain("Directory.Build.props");
        text.ShouldContain("Directory.Packages.props");
        text.ShouldContain("global.json");
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Dockerfile_PublishesItsOwnProject(string dockerfile, string entry)
    {
        var publish = Lines(dockerfile).Single(l => l.Contains("dotnet publish", StringComparison.Ordinal));

        publish.ShouldContain(entry);
        publish.ShouldContain("-c Release");
    }

    [Theory]
    [MemberData(nameof(Images))]
    public void Dockerfile_RunsAsNonRootOnTheAspNetRuntimeImage(string dockerfile, string entry)
    {
        _ = entry;
        var lines = Lines(dockerfile);

        lines.Count(l => l.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase) && l.Contains("mcr.microsoft.com/dotnet/aspnet:10.0", StringComparison.Ordinal)).ShouldBe(1);
        lines.Last(l => l.StartsWith("USER ", StringComparison.OrdinalIgnoreCase)).ShouldBe("USER $APP_UID");
        lines.ShouldContain("EXPOSE 8080");
        lines.Last(l => l.StartsWith("ENTRYPOINT", StringComparison.OrdinalIgnoreCase)).ShouldMatch(new Regex(@"^ENTRYPOINT \[""dotnet"", ""UrlShortener\.[A-Za-z.]+\.dll""\]$").ToString());
    }

    [Fact]
    public void DockerIgnore_KeepsBuildOutputsAndSecretsOutOfTheContext()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), ".dockerignore"));

        foreach (var pattern in new[] { "**/bin", "**/obj", ".git", ".env", "**/.env" })
        {
            text.ShouldContain(pattern);
        }
    }
}
