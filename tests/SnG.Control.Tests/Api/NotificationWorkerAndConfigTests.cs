using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SnG.Control.Api;
using SnG.Control.Api.Authentication;
using SnG.Control.Api.Authorization;
using SnG.Control.Api.Workers;
using SnG.Control.Application.Features.Notifications;
using SnG.Control.Application.Identity;
using SnG.Control.Application.Scheduling;
using SnG.Control.Infrastructure.Files;
using SnG.Control.Tests.Fixtures;

namespace SnG.Control.Tests.Api;

public class NotificationWorkerAndConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" Synthetic-Subject ")]
    public async Task BudgetsConfiguration_IsRequiredAtStartup_AndOpaque(string? subject)
    {
        using var storage = new TemporaryStorage();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.Services.AddApiServices();
        builder.Services.Configure<OperationsHeadOptions>(o => o.OperationsHeadSubject = "synthetic-operations");
        builder.Services.Configure<ManagementOptions>(options => options.ManagementSubject = "management-test-subject"); builder.Services.RemoveAll<IHostedService>();
        builder.Services.Configure<GoogleAuthenticationOptions>(o => o.ClientId = "synthetic-client");
        builder.Services.Configure<AccountingAssigneeOptions>(o => o.AccountingAssigneeSubject = "synthetic-accountant");
        builder.Services.Configure<CommercialHeadOptions>(o => o.CommercialHeadSubject = "synthetic-head");
        builder.Services.Configure<BudgetsAssigneeOptions>(o => o.BudgetsAssigneeSubject = subject!);
        builder.Services.Configure<FileStorageOptions>(o => o.RootPath = storage.Root);
        using var host = builder.Build();
        if (string.IsNullOrWhiteSpace(subject))
        {
            var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
            Assert.Equal("Configure Authentication:BudgetsAssigneeSubject.", error.Message);
        }
        else
        {
            await host.StartAsync(); Assert.Equal(subject, host.Services.GetRequiredService<IBudgetsAssigneeIdentity>().Subject); await host.StopAsync();
        }
    }

    [Fact]
    public async Task Worker_StartsImmediately_RetriesFailureOnTimer_AndStopsWithoutRealMinute()
    {
        var clock = new ManualTimerClock(); var store = new FailingSweep(); var logs = new CaptureLog();
        var services = new ServiceCollection(); services.AddSingleton<IProjectNotificationSweepStore>(store);
        services.AddSingleton(new TaskTiming(clock, new())); services.AddScoped<ProjectNotificationSweepService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new ProjectNotificationBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), clock, logs);
        await worker.StartAsync(default); await store.First.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Period); clock.Tick(); await store.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
        Assert.Equal(2, store.Calls); Assert.Contains("IOException", Assert.Single(logs.Messages));
        Assert.DoesNotContain("private-failure-details", logs.Messages.Single());
    }
    private sealed class FailingSweep : IProjectNotificationSweepStore
    {
        public int Calls;
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SweepAsync(DateTime now, CancellationToken token)
        {
            if (Interlocked.Increment(ref Calls) == 1) { First.SetResult(); throw new IOException("private-failure-details"); }
            Second.SetResult(); return Task.CompletedTask;
        }
    }
    private sealed class ManualTimerClock : TimeProvider
    {
        private TimerCallback? callback; private object? state;
        public TimeSpan Period { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { this.callback = callback; this.state = state; Period = period; return new ManualTimer(); }
        public void Tick() => callback!(state);
        private sealed class ManualTimer : ITimer
        { public bool Change(TimeSpan dueTime, TimeSpan period) => true; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class CaptureLog : ILogger<ProjectNotificationBackgroundService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, error));
    }
}
