namespace StockReplenishment.Client.Services;

/// <summary>
/// Who the UI is acting as. Real authentication is out of scope; switching user makes it possible to
/// demonstrate that a request must be reviewed by someone other than its requester.
/// </summary>
public sealed class UserSession
{
    public static readonly IReadOnlyList<DemoUser> Users =
    [
        new("Anna Lindqvist", "Line worker"),
        new("Johan Berg", "Line worker"),
        new("Erik Johansson", "Reviewer"),
        new("Sara Holm", "Reviewer"),
        new("Warehouse", "Warehouse"),
    ];

    public DemoUser CurrentUser { get; private set; } = Users[0];

    public event Action? Changed;

    public void SwitchTo(DemoUser user)
    {
        if (user == CurrentUser)
        {
            return;
        }

        CurrentUser = user;
        Changed?.Invoke();
    }
}

public sealed record DemoUser(string Name, string Role);
