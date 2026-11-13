using Microsoft.Extensions.Options;
using SnG.Control.Application.Identity;
namespace SnG.Control.Api.Authorization;
public sealed class OperationsHeadOptions { public string OperationsHeadSubject { get; set; } = ""; }
public sealed class ConfiguredOperationsHeadIdentity(IOptions<OperationsHeadOptions> options) : IOperationsHeadIdentity
{ public string Subject => options.Value.OperationsHeadSubject; }
public static class OperationsHeadRegistration
{
    public static IServiceCollection AddOperationsHead(this IServiceCollection services)
    {
        services.AddOptions<OperationsHeadOptions>().BindConfiguration("Authentication")
            .Validate(o => !string.IsNullOrWhiteSpace(o.OperationsHeadSubject) && o.OperationsHeadSubject.Length <= 200 &&
                o.OperationsHeadSubject == o.OperationsHeadSubject.Trim() && !o.OperationsHeadSubject.Any(char.IsControl),
                "Configure Authentication:OperationsHeadSubject.").ValidateOnStart();
        services.AddSingleton<IOperationsHeadIdentity, ConfiguredOperationsHeadIdentity>();
        services.AddScoped<SnG.Control.Application.Features.Operations.ProductionPreparationService>();
        services.AddScoped<SnG.Control.Application.Features.Operations.IProductionPreparationStore, SnG.Control.Infrastructure.Persistence.Stores.EfProductionPreparationStore>();
        services.AddOptions<SnG.Control.Application.Features.Operations.DocumentExtractionOptions>().BindConfiguration("DocumentExtraction")
            .Validate(o=>o.MaxDocumentBytes is >0 and <=26214400 && o.MaxPages is >0 and <=100 && o.MemoryMb is >=256 and <=1024 && o.TimeoutSeconds is >=1 and <=90,
                "Invalid DocumentExtraction resource limits.")
            .Validate(o=>!o.Enabled || Path.IsPathFullyQualified(o.PythonExecutable) && File.Exists(o.PythonExecutable),"DocumentExtraction:PythonExecutable must be an existing absolute executable path.").ValidateOnStart();
        services.AddScoped<SnG.Control.Infrastructure.Persistence.Stores.EfDocumentExtractionStore>();
        services.AddScoped<SnG.Control.Application.Features.Operations.IDocumentExtractionStore>(sp=>sp.GetRequiredService<SnG.Control.Infrastructure.Persistence.Stores.EfDocumentExtractionStore>());
        services.AddSingleton<SnG.Control.Application.Features.Operations.IDocumentComponentProcessor,SnG.Control.Infrastructure.Extraction.PythonDocumentComponentProcessor>();
        services.AddHostedService<SnG.Control.Api.Workers.DocumentExtractionBackgroundService>();
        services.AddScoped<SnG.Control.Application.Features.Operations.ProductionReviewAccess>();
        services.AddScoped<SnG.Control.Application.Features.Operations.IProductionReviewStore,SnG.Control.Infrastructure.Persistence.Stores.EfProductionReviewStore>();
        return services;
    }
}
