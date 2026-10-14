using UrlShortener.Gateway;
using UrlShortener.Gateway.RateLimiting;
using UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddGatewayForwardedHeaders(builder.Configuration);
builder.Services.AddGatewayRateLimiting(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseForwardedHeaders();   // first: everything after it (rate limiting) sees the real client address
app.UseServiceDefaults();
app.UseRateLimiter();
app.MapDefaultEndpoints();   // /health: a literal route with the default order, so it wins over the /{code} proxy route
app.MapReverseProxy();

await app.RunAsync();

public partial class Program;
