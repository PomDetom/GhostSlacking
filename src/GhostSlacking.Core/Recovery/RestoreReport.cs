
namespace GhostSlacking.Core;

public sealed record RestoreItemResult(nint Hwnd, bool Success, bool Skipped, string Reason, int ErrorCode = 0);

public sealed record RestoreReport(IReadOnlyList<RestoreItemResult> Items)
{
    public bool Success => Items.All(item => item.Success);
    public bool HasFailures => Items.Any(item => !item.Success && !item.Skipped);
}
