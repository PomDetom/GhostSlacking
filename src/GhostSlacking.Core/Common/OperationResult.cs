
namespace GhostSlacking.Core;

public sealed record OperationResult<T>(bool Success, T? Value, string? Error = null, int ErrorCode = 0)
{
    public static OperationResult<T> Ok(T value) => new(true, value);
    public static OperationResult<T> Failed(string error, int errorCode = 0) => new(false, default, error, errorCode);
}
