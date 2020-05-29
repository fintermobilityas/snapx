using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Core;
using Snap.Logging;
using snapx.Core;
using snapx.Options;

namespace snapx
{
    internal partial class Program
    {
        static async Task<int> CommandLock([NotNull] LockOptions lockOptions, [NotNull] IDistributedMutexClient distributedMutexClient,
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapAppReader appReader,
            [NotNull] ILog logger, [NotNull] string workingDirectory, CancellationToken cancellationToken)
        {
            if (lockOptions == null) throw new ArgumentNullException(nameof(lockOptions));
            if (distributedMutexClient == null) throw new ArgumentNullException(nameof(distributedMutexClient));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));

            var snapApps = BuildSnapAppsFromDirectory(filesystem, appReader, workingDirectory);

            var snapApp = snapApps.Apps.FirstOrDefault(x => string.Equals(x.Id, lockOptions.Id, StringComparison.OrdinalIgnoreCase));
            if (snapApp == null)
            {
                return -1;
            }

            if (string.IsNullOrWhiteSpace(snapApps.Generic.Token))
            {
                logger.Error("Please specify a token in your snapx.yml file. A random UUID is sufficient.");
                return -1;
            }

            await using var distributedMutex = new DistributedMutex(distributedMutexClient, 
                logger, snapApps.BuildLockKey(snapApp), cancellationToken, false);

            bool success;
            if (!lockOptions.Release)
            {
                success = await distributedMutex.TryAquireAsync();
                return success ? 0 : -1;
            }

            success = await DistributedMutex.TryForceReleaseAsync(distributedMutex.Name, distributedMutexClient, logger);
            return success ? 0 : -1;
        }
    }
}
