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
        static int CommandSha512([NotNull] Sha512Options sha512Options, [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ILog logger)
        {
            if (sha512Options == null) throw new ArgumentNullException(nameof(sha512Options));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            if (sha512Options.Filename == null || !snapFilesystem.FileExists(sha512Options.Filename))
            {
                logger.Error($"File not found: {sha512Options.Filename}");
                return -1;
            }

            try
            {
                using (var fileStream = new FileStream(sha512Options.Filename, FileMode.Open, FileAccess.Read))
                {
                    logger.Info(snapCryptoProvider.Sha512(fileStream));
                }
                return 0;
            }
            catch (Exception e)
            {
                logger.ErrorException($"Error computing SHA512-checksum for filename: {sha512Options.Filename}", e);
                return -1;
            }
        }
    }
}
