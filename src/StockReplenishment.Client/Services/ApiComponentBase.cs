using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace StockReplenishment.Client.Services;

/// <summary>Shared plumbing for pages that call the API: busy state and user-friendly error reporting.</summary>
public abstract class ApiComponentBase : ComponentBase
{
    [Inject]
    protected ReplenishmentApiClient Api { get; set; } = null!;

    [Inject]
    protected ISnackbar Snackbar { get; set; } = null!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = null!;

    protected bool IsBusy { get; private set; }

    /// <summary>Runs an API operation, showing failures as a snackbar. Returns false if it failed.</summary>
    protected async Task<bool> TryRunAsync(Func<Task> operation, string? successMessage = null)
    {
        IsBusy = true;
        try
        {
            await operation();
            if (successMessage is not null)
            {
                Snackbar.Add(successMessage, Severity.Success);
            }

            return true;
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.DisplayMessage, Severity.Error);
            return false;
        }
        catch (HttpRequestException)
        {
            Snackbar.Add("Could not reach the server. Check your connection and try again.", Severity.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
