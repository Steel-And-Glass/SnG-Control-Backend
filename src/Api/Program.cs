using Microsoft.EntityFrameworkCore;
using SnG.Control.Api;
using SnG.Control.Api.Authentication;
using SnG.Control.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SnGControl");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:SnGControl using User Secrets or ConnectionStrings__SnGControl before starting the API.");
}

builder.Services.AddDbContext<SnGControlDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddApiServices();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityDocumentTransformer>());

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
