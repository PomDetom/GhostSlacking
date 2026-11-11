
namespace GhostSlacking.Core;

public sealed record GhostWindowProfile(WindowSnapshot Original, RevealSettings Reveal)
{
    public nint Hwnd => Original.Hwnd;
}
