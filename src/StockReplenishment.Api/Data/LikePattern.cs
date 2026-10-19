namespace StockReplenishment.Api.Data;

internal static class LikePattern
{
    public const string EscapeCharacter = "\\";

    /// <summary>Builds a "contains" LIKE pattern, escaping wildcards in user input.</summary>
    public static string Contains(string value)
    {
        var escaped = value.Trim()
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter)
            .Replace("%", EscapeCharacter + "%")
            .Replace("_", EscapeCharacter + "_");
        return $"%{escaped}%";
    }
}
