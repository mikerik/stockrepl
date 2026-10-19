using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace StockReplenishment.Api.Data;

/// <summary>All timestamps are stored as UTC; SQLite loses DateTimeKind, so it is restored on read.</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
