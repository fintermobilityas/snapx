using System;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Core.Update
{
    public sealed class SnapReleaseEntry
    {

    }

    public interface IUpdateManager
    {
        Task<SnapReleaseEntry> GetLatestReleaseInformationAsync(CancellationToken cancellationToken);
        Task DownloadLatestReleaseAsync(IProgressSource progressSource, CancellationToken cancellationToken);
        Task<string> InstallReleaseAsync(SnapReleaseEntry releaseEntry);
        Task UpdateToChannelAsync(string channelName, CancellationToken cancellationToken);
        Task RestartAsync();
    }

    public sealed class UpdateManagerManager : IUpdateManager
    {
        public Task<SnapReleaseEntry> GetLatestReleaseInformationAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task DownloadLatestReleaseAsync(IProgressSource progressSource, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<string> InstallReleaseAsync(SnapReleaseEntry releaseEntry)
        {
            throw new NotImplementedException();
        }

        public Task UpdateToChannelAsync(string channelName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task RestartAsync()
        {
            throw new NotImplementedException();
        }
    }
}
