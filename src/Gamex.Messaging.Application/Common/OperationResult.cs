namespace Gamex.Messaging.Application.Common;

public enum OperationStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
    Unauthorized,
    Forbidden,
}

/// <summary>Outcome of a command or query; the API maps <see cref="Status"/> to an HTTP status.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Factory methods read as OperationResult<T>.Ok(value).")]
public sealed record OperationResult<T>(
    OperationStatus Status,
    T? Value = default,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public bool IsOk => Status == OperationStatus.Ok;

    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value);

    public static OperationResult<T> NotFound(string detail) => new(OperationStatus.NotFound, Detail: detail);

    public static OperationResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(OperationStatus.Invalid, Errors: errors);

    public static OperationResult<T> Invalid(string field, string message) =>
        new(OperationStatus.Invalid, Errors: new Dictionary<string, string[]> { [field] = [message] });

    public static OperationResult<T> Conflict(string detail, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new(OperationStatus.Conflict, Detail: detail, Extensions: extensions);

    public static OperationResult<T> Unauthorized(string detail) => new(OperationStatus.Unauthorized, Detail: detail);

    public static OperationResult<T> Forbidden(string detail) => new(OperationStatus.Forbidden, Detail: detail);

    /// <summary>Same failure, different value type.</summary>
    public OperationResult<TOther> As<TOther>() => new(Status, default, Detail, Errors, Extensions);
}

/// <summary>Collects field validation errors.</summary>
public sealed class Validation
{
    private readonly Dictionary<string, string[]> _errors = [];

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyDictionary<string, string[]> Errors => _errors;

    public Validation Require(bool condition, string field, string message)
    {
        if (!condition && !_errors.ContainsKey(field))
        {
            _errors[field] = [message];
        }

        return this;
    }
}

/// <summary>Opaque keyset cursor: base64url of "ticks:id".</summary>
public static class Cursor
{
    public static string Encode(DateTime at, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{at.Ticks}:{id}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out DateTime at, out Guid id)
    {
        at = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var ticks)
                && Guid.TryParse(parts[1], out id))
            {
                at = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
