using System.ComponentModel.DataAnnotations;
using StockReplenishment.Contracts.Common;

namespace StockReplenishment.Contracts.Requests;

/// <summary>Payload for creating a draft request or replacing the content of an existing draft.</summary>
public sealed class SaveRequestInput
{
    [Range(1, int.MaxValue, ErrorMessage = "A stock location must be selected.")]
    public int LocationId { get; set; }

    [EnumDataType(typeof(RequestPriority))]
    public RequestPriority Priority { get; set; } = RequestPriority.Normal;

    [MaxLength(ValidationLimits.NotesMaxLength)]
    public string? Notes { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "A request must contain at least one line item.")]
    [MaxLength(ValidationLimits.MaxLinesPerRequest)]
    [NoNullItems]
    public List<RequestLineInput> Lines { get; set; } = [];
}

public sealed class RequestLineInput
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(ValidationLimits.ArticleNumberMaxLength)]
    public string ArticleNumber { get; set; } = string.Empty;

    // Decimal limits are strings, so they must be parsed culture-independently (e.g. "0.001" fails under sv-SE).
    [Range(typeof(decimal), "0.001", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Requested quantity must be between 0.001 and 100000.")]
    public decimal RequestedQuantity { get; set; }
}

public sealed class ApproveRequestInput
{
    [MaxLength(ValidationLimits.CommentMaxLength)]
    public string? Comment { get; set; }
}

public sealed class RejectRequestInput
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "A rejection reason is required.")]
    [MaxLength(ValidationLimits.CommentMaxLength)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class FulfillRequestInput
{
    [Required]
    [MinLength(1)]
    [NoNullItems]
    public List<FulfilledLineInput> Lines { get; set; } = [];
}

public sealed class FulfilledLineInput
{
    [Range(1, int.MaxValue)]
    public int LineId { get; set; }

    [Range(typeof(decimal), "0", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Fulfilled quantity must be between 0 and 100000.")]
    public decimal FulfilledQuantity { get; set; }
}
