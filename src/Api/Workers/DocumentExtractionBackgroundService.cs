using Microsoft.Extensions.Options;
using SnG.Control.Application.Features.Operations;
using SnG.Control.Infrastructure.Persistence.Stores;
namespace SnG.Control.Api.Workers;
public sealed class DocumentExtractionBackgroundService(IServiceScopeFactory scopes,IOptions<DocumentExtractionOptions> options,ILogger<DocumentExtractionBackgroundService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Value.Enabled)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try { await using var scope=scopes.CreateAsyncScope();if(await scope.ServiceProvider.GetRequiredService<EfDocumentExtractionStore>().RunOneAsync(stoppingToken))continue; }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Document extraction iteration failed; lease recovery remains available.");}
            try {await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
