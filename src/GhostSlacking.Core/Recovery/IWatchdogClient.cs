
namespace GhostSlacking.Core;

public interface IWatchdogClient : IDisposable
{
    Guid SessionId { get; }
    bool IsConnected { get; }
    void Start();
    void PublishManifest(RecoveryManifest manifest);
    void CompleteShutdown();
}
