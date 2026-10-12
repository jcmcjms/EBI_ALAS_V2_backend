using Ebi.Alas.Api.Composition;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Honor reverse-proxy headers so Connection.RemoteIpAddress (rate-limit
    // partitions, logs) reflects the real client, not the proxy hop.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddApiComposition(builder.Configuration, builder.Environment);

var app = builder.Build();

// Must run before rate limiting/auth so RemoteIpAddress is the forwarded client.
app.UseForwardedHeaders();

app.UseApiPipeline();

app.Run();

public partial class Program;
