using UrlShortener.ServiceDefaults;
using UrlShortener.WriteService.Api;
using UrlShortener.WriteService.Api.Endpoints;
using UrlShortener.WriteService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApiDocumentation();
builder.Services.AddWriteApi(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();
app.MapApiDocumentation();
app.MapCreateShortUrl();

// This service owns the schema. Can be switched off (Database:MigrateOnStartup=false) e.g. for tests.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.Services.MigrateWriteDatabaseAsync();
}

await app.RunAsync();

public partial class Program;
