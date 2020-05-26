using System;
using System.IO;
using JetBrains.Annotations;
using snapx.Options;
using Snap.Core;
using Snap.Logging;

namespace snapx
{
    internal partial class Program
    {        
        static int CommandSha256([NotNull] Sha256Options sha256Options, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ILog logger)
        {
            if (sha256Options == null) throw new ArgumentNullException(nameof(sha256Options));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (sha256Options.Filename == null || !snapFilesystem.FileExists(sha256Options.Filename))
            {
                logger.Error($"File not found: {sha256Options.Filename}");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha256Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    logger.Info(snapCryptoProvider.Sha256(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                logger.ErrorException($"Error computing SHA256-checksum for filename: {sha256Options.Filename}", e);
                return -1;
            }
        }
    }
}
