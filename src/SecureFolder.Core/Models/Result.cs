namespace SecureFolder.Core.Models;

/// <summary>
/// Result wrapper for operation outcomes.
/// </summary>
public readonly record struct Result<T>(T? Value, string? Error, bool IsSuccess)
{
    public static Result<T> Ok(T value) => new(value, null, true);
    public static Result<T> Fail(string error) => new(default, error, false);
}

/// <summary>
/// Result wrapper for void operations.
/// </summary>
public readonly record struct Result(string? Error, bool IsSuccess)
{
    public static Result Ok() => new(null, true);
    public static Result Fail(string error) => new(error, false);
}
