namespace UrlShortener.ArchitectureTests;

/// <summary>The dependency rules from CLAUDE.md ("Dependency rules"), enforced on the project graph.</summary>
public sealed class DependencyRulesTests
{
    private const string Domain = "UrlShortener.Domain";
    private const string BuildingBlocks = "UrlShortener.BuildingBlocks";
    private const string ServiceDefaults = "UrlShortener.ServiceDefaults";
    private const string Gateway = "UrlShortener.Gateway";

    // Infrastructure concerns that must never leak into Domain, BuildingBlocks or Application.
    private static readonly string[] InfrastructurePackagePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "OpenTelemetry",
        "Yarp",
        "Microsoft.AspNetCore",
    ];

    private static readonly ProjectGraph Graph = ProjectGraph.Load();

    public static TheoryData<string> Services => ["WriteService", "ReadService"];

    [Fact]
    public void Solution_ExpectedProjects_Exist()
    {
        string[] expected =
        [
            Domain, BuildingBlocks, ServiceDefaults, Gateway,
            "UrlShortener.WriteService.Application", "UrlShortener.WriteService.Infrastructure", "UrlShortener.WriteService.Api",
            "UrlShortener.ReadService.Application", "UrlShortener.ReadService.Infrastructure", "UrlShortener.ReadService.Api",
        ];

        var missing = expected.Where(name => !Graph.Contains(name)).ToArray();

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Domain_DependsOnlyOnBuildingBlocks()
    {
        var domain = Graph.Get(Domain);

        domain.ProjectReferences.ShouldBeSubsetOf([BuildingBlocks]);
        domain.PackageReferences.ShouldBeEmpty();
        domain.FrameworkReferences.ShouldBeEmpty();
    }

    [Fact]
    public void BuildingBlocks_DependsOnlyOnLoggingAbstractions()
    {
        var buildingBlocks = Graph.Get(BuildingBlocks);

        buildingBlocks.ProjectReferences.ShouldBeEmpty();
        buildingBlocks.FrameworkReferences.ShouldBeEmpty();
        buildingBlocks.PackageReferences.ShouldBeSubsetOf(["Microsoft.Extensions.Logging.Abstractions"]);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Application_DependsOnlyOnDomainAndBuildingBlocks(string service)
    {
        var application = Graph.Get($"UrlShortener.{service}.Application");

        application.ProjectReferences.ShouldBeSubsetOf([Domain, BuildingBlocks]);
        application.FrameworkReferences.ShouldBeEmpty();
        application.PackageReferences
            .Where(IsInfrastructurePackage)
            .ShouldBeEmpty($"{application.Name} must stay free of infrastructure packages");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Infrastructure_DependsOnlyOnItsApplication(string service)
    {
        var infrastructure = Graph.Get($"UrlShortener.{service}.Infrastructure");

        infrastructure.ProjectReferences.ShouldBeSubsetOf([$"UrlShortener.{service}.Application"]);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Api_DependsOnlyOnItsLayersAndServiceDefaults(string service)
    {
        var api = Graph.Get($"UrlShortener.{service}.Api");

        api.ProjectReferences.ShouldBeSubsetOf(
            [$"UrlShortener.{service}.Infrastructure", $"UrlShortener.{service}.Application", ServiceDefaults]);
    }

    [Theory]
    [InlineData("WriteService", "ReadService")]
    [InlineData("ReadService", "WriteService")]
    public void Service_NeverReferencesTheOtherService(string service, string other)
    {
        var offending = Graph.All
            .Where(p => p.Name.StartsWith($"UrlShortener.{service}.", StringComparison.Ordinal))
            .SelectMany(p => Graph.TransitiveProjectReferences(p.Name).Prepend(p.Name).Select(r => (From: p.Name, To: r)))
            .Where(edge => edge.To.Contains($".{other}.", StringComparison.Ordinal))
            .ToArray();

        offending.ShouldBeEmpty($"{service} must never reference {other}");
    }

    [Fact]
    public void Gateway_ReferencesNoService()
    {
        var gateway = Graph.Get(Gateway);

        gateway.ProjectReferences.ShouldBeSubsetOf([ServiceDefaults]);
        Graph.TransitiveProjectReferences(Gateway)
            .Where(r => r.Contains(".WriteService.", StringComparison.Ordinal) || r.Contains(".ReadService.", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public void SharedProjects_NeverReferenceServicesOrGateway()
    {
        foreach (var name in new[] { Domain, BuildingBlocks, ServiceDefaults })
        {
            Graph.TransitiveProjectReferences(name)
                .Where(r => r.Contains(".WriteService.", StringComparison.Ordinal)
                            || r.Contains(".ReadService.", StringComparison.Ordinal)
                            || r == Gateway)
                .ShouldBeEmpty($"{name} is shared and must not depend on a service");
        }
    }

    private static bool IsInfrastructurePackage(string package) =>
        InfrastructurePackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
