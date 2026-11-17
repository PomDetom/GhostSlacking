
namespace GhostSlacking.Core;

public sealed record NativeResult(bool Success, string Operation, int ErrorCode = 0, string? ErrorMessage = null)
{
    public static NativeResult Ok(string operation) => new(true, operation);
    public static NativeResult Failed(string operation, int errorCode, string? message = null) => new(false, operation, errorCode, message);
}
