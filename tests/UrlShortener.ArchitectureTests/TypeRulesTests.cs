using System.Reflection;
using NetArchTest.Rules;
using UrlShortener.BuildingBlocks.Results;
using UrlShortener.Domain;

namespace UrlShortener.ArchitectureTests;

/// <summary>Type-level rules, checked once there are types to inspect (Phase 1 onwards).</summary>
public sealed class TypeRulesTests
{
    private static readonly Assembly DomainAssembly = typeof(ShortUrl).Assembly;
    private static readonly Assembly BuildingBlocksAssembly = typeof(Result<>).Assembly;

    private static readonly string[] ForbiddenNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "OpenTelemetry",
        "Microsoft.AspNetCore",
        "Yarp",
    ];

    public static TheoryData<Assembly> SharedAssemblies => [DomainAssembly, BuildingBlocksAssembly];

    [Theory]
    [MemberData(nameof(SharedAssemblies))]
    public void SharedKernel_Classes_AreSealed(Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .That().AreClasses().And().DoNotHaveNameMatching("<.*>")
            .Should().BeSealed()
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(SharedAssemblies))]
    public void SharedKernel_DoesNotDependOnInfrastructure(Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces)
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty();
    }

    [Fact]
    public void ProductionCode_NeverReadsTheClockDirectly()
    {
        // Inject TimeProvider instead (CLAUDE.md). Allowed only in tests.
        string[] forbidden = ["DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow"];
        var srcRoot = Path.Combine(ProjectGraph.FindRepositoryRoot(), "src");

        var offenders = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => forbidden.Any(f => File.ReadAllText(path).Contains(f, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(srcRoot, path))
            .ToArray();

        offenders.ShouldBeEmpty();
    }
}
