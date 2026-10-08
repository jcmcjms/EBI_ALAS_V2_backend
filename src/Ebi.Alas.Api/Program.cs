using Ebi.Alas.Api.Composition;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApiComposition(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseApiPipeline();

app.Run();

public partial class Program;
