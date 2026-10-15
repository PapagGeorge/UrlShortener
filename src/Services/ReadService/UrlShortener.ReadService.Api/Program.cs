using UrlShortener.ReadService.Api;
using UrlShortener.ReadService.Api.Endpoints;
using UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApiDocumentation();
builder.Services.AddReadApi(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapApiDocumentation();   // literal routes such as /health win over the /{shortCode} catch-all
app.MapRedirect();

await app.RunAsync();

public partial class Program;
