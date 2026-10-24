using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gamex.Messaging.Infrastructure.Persistence;

/// <summary>All timestamps are stored as UTC; MySQL drops the kind, so restore it on read.</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
