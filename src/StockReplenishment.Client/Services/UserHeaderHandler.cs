using StockReplenishment.Contracts.Common;

namespace StockReplenishment.Client.Services;

/// <summary>Attaches the acting user to every API call.</summary>
public sealed class UserHeaderHandler(UserSession session) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove(ApiConventions.UserHeader);
        request.Headers.Add(ApiConventions.UserHeader, session.CurrentUser.Name);
        return base.SendAsync(request, cancellationToken);
    }
}
