namespace StockReplenishment.Contracts.Common;

public static class ApiConventions
{
    /// <summary>
    /// Identifies the acting user. Stand-in for real authentication, which is out of scope for this exercise.
    /// </summary>
    public const string UserHeader = "X-User-Name";

    public const string RequestsHubPath = "/hubs/requests";

    public const string RequestUpdatedEvent = "RequestUpdated";
}
