using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Client.Services;

/// <summary>
/// Single SignalR connection shared by all pages. The server pushes a notification whenever a request
/// changes, most importantly when a background stock check finishes.
/// </summary>
public sealed class RequestUpdatesListener(NavigationManager navigation, ILogger<RequestUpdatesListener> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private HubConnection? _connection;

    public event Action<RequestUpdatedNotification>? RequestUpdated;

    /// <summary>Raised after a lost connection is re-established; listeners should reload since updates may have been missed.</summary>
    public event Action? Reconnected;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task EnsureStartedAsync()
    {
        await _startLock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(navigation.ToAbsoluteUri(ApiConventions.RequestsHubPath))
                    .WithAutomaticReconnect()
                    .Build();

                _connection.On<RequestUpdatedNotification>(ApiConventions.RequestUpdatedEvent, n => RequestUpdated?.Invoke(n));
                _connection.Reconnected += _ =>
                {
                    Reconnected?.Invoke();
                    return Task.CompletedTask;
                };
            }

            if (_connection.State == HubConnectionState.Disconnected)
            {
                await _connection.StartAsync();
            }
        }
        catch (Exception ex)
        {
            // Live updates are a convenience; pages still work (with manual refresh) without them.
            logger.LogWarning(ex, "Could not connect to the live update hub");
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _startLock.Dispose();
    }
}
