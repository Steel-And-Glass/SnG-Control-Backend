using SnG.Control.Application.Features.Notifications;

namespace SnG.Control.Api.Workers;

public sealed class ProjectNotificationBackgroundService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<ProjectNotificationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ProjectNotificationSweepService>().ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError("Notification sweep failed ({FailureType}); the next interval will retry.", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
