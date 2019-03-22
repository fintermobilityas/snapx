using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using snapx.Options;
using Snap;
using Snap.Core;
using Snap.Extensions;
using Snap.Logging;

namespace snapx
{
    internal partial class Program
    {
        static int CommandRcEdit([NotNull] RcEditOptions opts, [NotNull] ICoreRunLib coreRunLib, 
            [NotNull] ISnapFilesystem snapFilesystem, [NotNull] ILog logger)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var exitCode = 1;

            if (!snapFilesystem.FileExists(opts.Filename))
            {
                logger.Error($"Filename does not exist: {opts.Filename}.");
                goto done;
            }
            
            if (opts.ConvertSubSystemToWindowsGui)
            {
                logger.Info($"Attempting to change subsystem to Windows GUI for executable: {snapFilesystem.PathGetFileName(opts.Filename)}.");

                using (var srcStream = snapFilesystem.FileReadWrite(opts.Filename, false))
                {
                    if (!srcStream.ChangeSubsystemToWindowsGui(SnapLogger))
                    {
                        goto done;
                    }

                    logger.Info("Subsystem has been successfully changed to Windows GUI.");
                }

                exitCode = 0;
            }

            if (opts.IconFilename != null)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    logger.Error("Modifying executable icon is not supported on this OS platform.");
                    goto done;
                }
                opts.IconFilename = snapFilesystem.PathGetFullPath(opts.IconFilename);                
                if (!snapFilesystem.FileExists(opts.IconFilename))
                {
                    logger.Error($"Unable to find icon with filename: {opts.IconFilename}");
                    goto done;
                }

                if (!coreRunLib.SetIcon(opts.Filename, opts.IconFilename))
                {
                    logger.Error($"Unknown error setting icon for executable {opts.Filename}. Icon filename: {opts.Filename}.");
                    goto done;
                }

                logger.Info($"Icon has been successfully updated. Filename: {opts.Filename}. Icon filename: {opts.IconFilename}.");                
                exitCode = 0;
            }

            done:
            return exitCode;
        }

    }
}
