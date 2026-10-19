using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace StockReplenishment.Contracts.Common;

/// <summary>
/// Rejects collections containing null elements (e.g. <c>"lines": [null]</c>), which model validation
/// does not catch on its own.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NoNullItemsAttribute() : ValidationAttribute("The {0} field must not contain empty items.")
{
    public override bool IsValid(object? value) =>
        value is not IEnumerable items || items.Cast<object?>().All(item => item is not null);
}
