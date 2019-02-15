using System;
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

            if (opts.ConvertSubSystemToWindowsGui)
            {
                if (!snapFilesystem.FileExists(opts.Filename))
                {
                    logger.Error($"Unable to convert subsystem for executable, it does not exist: {opts.Filename}.");
                    return -1;
                }

                logger.Info($"Attempting to change subsystem to Windows GUI for executable: {opts.Filename}.");

                using (var srcStream = snapFilesystem.FileReadWrite(opts.Filename, false))
                {
                    if (!srcStream.ChangeSubsystemToWindowsGui(SnapLogger))
                    {
                        return -1;
                    }

                    logger.Info(message: "Subsystem has been successfully changed to Windows GUI.");
                }

                return 0;
            }

            return -1;
        }

    }
}
