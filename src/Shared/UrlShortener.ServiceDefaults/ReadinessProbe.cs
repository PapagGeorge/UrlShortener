using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UrlShortener.ServiceDefaults;

public static class ReadinessProbeExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers a readiness probe: an async action that throws when the dependency is not usable.
    /// It runs only on <c>/health/ready</c> (never on liveness), inside a scope (so a DbContext can be resolved),
    /// and is cut off after <paramref name="timeout"/> (default 3 s) so a hung dependency answers 503 quickly.
    /// </summary>
    public static IHealthChecksBuilder AddReadinessProbe(
        this IHealthChecksBuilder builder, string name, Func<IServiceProvider, CancellationToken, Task> probe, TimeSpan? timeout = null) =>
        builder.Add(new HealthCheckRegistration(
            name,
            sp => new ProbeCheck(sp, probe, timeout ?? TimeSpan.FromSeconds(3)),
            failureStatus: HealthStatus.Unhealthy,
            tags: [ReadyTag]));

    private sealed class ProbeCheck(IServiceProvider services, Func<IServiceProvider, CancellationToken, Task> probe, TimeSpan timeout) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                // `services` is the scope the health check service created for this run. WaitAsync enforces the timeout even if the probe ignores its token.
                await probe(services, cts.Token).WaitAsync(timeout, cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy($"{context.Registration.Name} did not answer within {timeout.TotalSeconds:0.#} s.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy($"{context.Registration.Name} is not reachable.", ex);
            }
        }
    }
}
