using UrlShortener.ReadService.Api;
using UrlShortener.ReadService.Api.Endpoints;
using UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddReadApi(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();   // literal routes such as /health win over the /{shortCode} catch-all
app.MapRedirect();

await app.RunAsync();

public partial class Program;
