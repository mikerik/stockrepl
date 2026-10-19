namespace StockReplenishment.Contracts.Requests;

public static class ValidationLimits
{
    public const int NotesMaxLength = 1000;
    public const int CommentMaxLength = 500;
    public const int ArticleNumberMaxLength = 32;
    public const int UserNameMaxLength = 100;
    public const int MaxLinesPerRequest = 50;
}
