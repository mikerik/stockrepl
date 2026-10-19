using StockReplenishment.Api.Application;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Infrastructure;

/// <summary>
/// Resolves the acting user from the <see cref="ApiConventions.UserHeader"/> header. This is a placeholder
/// for real authentication (e.g. Entra ID), which would populate <see cref="HttpContext.User"/> instead.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string Anonymous = "anonymous";

    public string UserName
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers[ApiConventions.UserHeader].ToString().Trim();
            if (string.IsNullOrEmpty(value))
            {
                return Anonymous;
            }

            return value.Length > ValidationLimits.UserNameMaxLength ? value[..ValidationLimits.UserNameMaxLength] : value;
        }
    }
}
