
namespace GhostSlacking.Core;

public interface IWindowEnvironmentApi
{
    OperationResult<WindowEnvironment> ReadEnvironment(WindowSnapshot original);
}
