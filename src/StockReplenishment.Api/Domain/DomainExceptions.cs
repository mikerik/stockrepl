namespace StockReplenishment.Api.Domain;

/// <summary>The operation is not allowed in the entity's current state (maps to 409 Conflict).</summary>
public sealed class InvalidStateException(string message) : Exception(message);

/// <summary>The input breaks one or more business rules (maps to 400 Bad Request with field errors).</summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public BusinessRuleException(IReadOnlyDictionary<string, string[]> errors)
        : base(string.Join(" ", errors.Values.SelectMany(e => e)))
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class NotFoundException(string entityName, object key)
    : Exception($"{entityName} '{key}' was not found.");
