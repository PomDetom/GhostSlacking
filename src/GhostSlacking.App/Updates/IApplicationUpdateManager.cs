using GhostSlacking.Core;

namespace GhostSlacking.App;

internal interface IApplicationUpdateManager : IDisposable
{
    ApplicationUpdateSnapshot Snapshot { get; }
    bool CanInstallUpdates { get; }
    UpdateChannel Channel { get; }
    event EventHandler? Changed;
    event EventHandler? InstallHandoffStarted;
    Task<ApplicationUpdateSnapshot> CheckAsync(CancellationToken cancellationToken = default);
    Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default);
    bool BeginAutomaticInstall(string installerPath);
    void SkipCurrentRelease();
    void ResumeCurrentRelease();
    bool OpenSourceRepository();
    bool OpenReleasePage();
    void SetChannel(UpdateChannel channel);
}
