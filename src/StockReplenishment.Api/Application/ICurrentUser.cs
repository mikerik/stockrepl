namespace StockReplenishment.Api.Application;

/// <summary>The user performing the current operation.</summary>
public interface ICurrentUser
{
    string UserName { get; }
}
