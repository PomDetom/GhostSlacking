
namespace GhostSlacking.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
