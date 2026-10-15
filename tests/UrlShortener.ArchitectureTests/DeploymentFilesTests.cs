using System.Text.RegularExpressions;

namespace UrlShortener.ArchitectureTests;

/// <summary>
/// The production compose file and the publish workflow are only exercised on the Linux box, so keep them honest
/// with plain-text checks that need neither Docker nor a YAML parser.
/// </summary>
public sealed partial class DeploymentFilesTests
{
    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UrlShortener.slnx")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }

    [GeneratedRegex(@"image:\s*(ghcr\.io/[^\s:$]+)")]
    private static partial Regex GhcrImage();

    [Fact]
    public void ProdCompose_NeverBuilds_ItOnlyPullsPublishedImages()
    {
        var compose = Read("deploy/docker-compose.prod.yml");

        Regex.IsMatch(compose, @"^\s*build:", RegexOptions.Multiline).ShouldBeFalse("production pulls images, it never builds");
    }

    [Fact]
    public void ProdCompose_UsesTheThreeImagesThatTheWorkflowPublishes_WithARollbackTag()
    {
        var compose = Read("deploy/docker-compose.prod.yml");
        var publish = Read(".github/workflows/publish.yml");

        var used = GhcrImage().Matches(compose).Select(m => m.Groups[1].Value).Order().ToArray();

        used.ShouldBe(
        [
            "ghcr.io/papaggeorge/urlshortener-gateway",
            "ghcr.io/papaggeorge/urlshortener-read-service",
            "ghcr.io/papaggeorge/urlshortener-write-service",
        ]);
        foreach (var image in used)
        {
            publish.ShouldContain(image["ghcr.io/papaggeorge/".Length..], customMessage: $"publish.yml must publish {image}");
        }

        compose.ShouldContain("${IMAGE_TAG:-latest}");
    }

    [Fact]
    public void ProdCompose_PublishesOnlyTheGatewayAndTheObservabilityUis_BoundToConfigurableAddresses()
    {
        var compose = Read("deploy/docker-compose.prod.yml");

        var ports = Regex.Matches(compose, @"^\s*-\s*""([^""]+:[^""]+:\d+)""", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();

        ports.Length.ShouldBe(3);
        ports.ShouldAllBe(p => p.StartsWith("${GATEWAY_BIND:-127.0.0.1}") || p.StartsWith("${OBSERVABILITY_BIND:-127.0.0.1}"));
    }

    [Fact]
    public void ProdCompose_HasTheSameServicesAsTheDevCompose()
    {
        static string[] Services(string compose) =>
            Regex.Matches(compose, @"^  ([a-z-]+):\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Order().ToArray();

        Services(Read("deploy/docker-compose.prod.yml")).ShouldBe(Services(Read("docker-compose.yml")));
    }

    [Fact]
    public void PublishWorkflow_RunsOnlyAfterASuccessfulCiRunOnMain_NeverFromAForkOrAPullRequest()
    {
        var publish = Read(".github/workflows/publish.yml");

        publish.ShouldContain("workflow_run:");
        publish.ShouldContain("branches: [main]");
        publish.ShouldContain("github.event.workflow_run.conclusion == 'success'");
        publish.ShouldContain("github.event.workflow_run.event == 'push'");
        publish.ShouldNotContain("pull_request");
        publish.ShouldNotContain("self-hosted");
        publish.ShouldContain("packages: write");
        publish.ShouldContain("platforms: linux/amd64");
    }

    [Fact]
    public void UpdateScript_NeverTouchesVolumesAndFailsOnErrors()
    {
        var script = Read("deploy/update.sh");

        script.ShouldContain("set -euo pipefail");
        Regex.IsMatch(script, @"docker compose .*\bpull\b").ShouldBeTrue();
        script.ShouldContain("--remove-orphans");
        script.ShouldNotContain("down");
        script.ShouldNotContain("--volumes");
        script.ShouldNotContain("prune -a");
    }

    [Fact]
    public void SystemdTimer_RunsTheUpdateServiceRepeatedly()
    {
        var timer = Read("deploy/urlshortener-update.timer");
        var service = Read("deploy/urlshortener-update.service");

        timer.ShouldContain("OnUnitActiveSec=");
        timer.ShouldContain("Unit=urlshortener-update.service");
        service.ShouldContain("Type=oneshot");
        service.ShouldContain("ExecStart=/opt/urlshortener/deploy/update.sh");
    }
}
