using System.Text.RegularExpressions;

namespace UrlShortener.ArchitectureTests;

/// <summary>
/// The React UI and its container are built only by CI and on the Linux box, so keep the files honest with plain-text
/// checks that need neither Node nor Docker.
/// </summary>
public sealed class WebUiFilesTests
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

    [Fact]
    public void Dockerfile_BuildsWithNodeAndServesWithUnprivilegedNginxOnPort8080()
    {
        var dockerfile = Read("web/Dockerfile");

        dockerfile.ShouldContain("AS build");
        Regex.IsMatch(dockerfile, @"^FROM node:\d+", RegexOptions.Multiline).ShouldBeTrue("build stage uses a pinned node major");
        Regex.IsMatch(dockerfile, @"^FROM nginxinc/nginx-unprivileged:\d", RegexOptions.Multiline).ShouldBeTrue("final stage is non-root nginx with a pinned version");
        dockerfile.ShouldContain("RUN npm ci");
        dockerfile.ShouldContain("RUN npm run build");
        dockerfile.ShouldContain("COPY --from=build /app/dist /usr/share/nginx/html");
        dockerfile.ShouldContain("EXPOSE 8080");
        dockerfile.ShouldNotContain("USER root");
    }

    [Fact]
    public void Dockerfile_InstallsFromTheLockfileOnly_SoTheRestoreLayerIsCachedAndReproducible()
    {
        var dockerfile = Read("web/Dockerfile");

        dockerfile.ShouldContain("COPY package.json package-lock.json ./");
        dockerfile.IndexOf("RUN npm ci", StringComparison.Ordinal)
            .ShouldBeLessThan(dockerfile.IndexOf("RUN npm run build", StringComparison.Ordinal));
    }

    [Fact]
    public void Nginx_ProxiesApiToTheGateway_ServesTheSpaAndNeverCachesIndexHtml()
    {
        var nginx = Read("web/nginx.conf.template");

        nginx.ShouldContain("listen 8080;");
        nginx.ShouldContain("location /api/");
        nginx.ShouldContain("proxy_pass http://${API_UPSTREAM}/;");
        nginx.ShouldContain("try_files $uri /index.html;");
        nginx.ShouldContain("X-Content-Type-Options");
        nginx.ShouldContain("no-cache");
    }

    [Fact]
    public void DockerIgnore_KeepsNodeModulesAndBuildOutputOutOfTheContext()
    {
        var ignore = Read("web/.dockerignore");

        ignore.ShouldContain("node_modules");
        ignore.ShouldContain("dist");
    }

    [Fact]
    public void ComposeFiles_RunTheWebUiBehindTheGateway_WithoutBuildingItInProd()
    {
        var dev = Read("docker-compose.yml");
        var prod = Read("deploy/docker-compose.prod.yml");

        dev.ShouldContain("context: ./web");
        prod.ShouldContain("ghcr.io/papaggeorge/urlshortener-web:${IMAGE_TAG:-latest}");
        foreach (var compose in new[] { dev, prod })
        {
            compose.ShouldContain("API_UPSTREAM: gateway:8080");
            compose.ShouldContain("${WEB_BIND:-127.0.0.1}:${WEB_PORT:-3000}:8080");
        }
    }

    [Fact]
    public void Workflows_TestAndBuildTheUi_AndPublishItsImage()
    {
        var ci = Read(".github/workflows/ci.yml");
        var publish = Read(".github/workflows/publish.yml");

        ci.ShouldContain("working-directory: web");
        ci.ShouldContain("npm ci");
        ci.ShouldContain("npm test");
        ci.ShouldContain("npm run build");
        ci.ShouldContain("dockerfile: web/Dockerfile");
        publish.ShouldContain("image: urlshortener-web");
        publish.ShouldContain("dockerfile: web/Dockerfile");
    }
}
