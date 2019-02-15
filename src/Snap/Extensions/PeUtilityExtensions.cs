using System;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Snap.AnyOS.Windows;
using Snap.Logging;

namespace Snap.Extensions
{
    internal static class PeUtilityExtensions
    {
        [UsedImplicitly]
        public static (PeUtility.SubSystemType subSystemType, bool is32Bit, bool is64Bit) GetPeDetails([NotNull] this Stream srcStream)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            using (var peFile = new PeUtility(srcStream))
            {
                var subsysVal = peFile.Is32BitHeader ? (PeUtility.SubSystemType)peFile.OptionalHeader32.Subsystem : (PeUtility.SubSystemType)peFile.OptionalHeader64.Subsystem;

                return (subsysVal, peFile.Is32BitHeader, !peFile.Is32BitHeader);
            }
        }

        public static bool ChangeSubsystemToWindowsGui([NotNull] this Stream srcStream, ILog logger = null)
        {
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            using (var peFile = new PeUtility(srcStream))
            {
                var subsysOffset = peFile.MainHeaderOffset;
                var headerType = peFile.Is32BitHeader ? 
                    typeof(PeUtility.IMAGE_OPTIONAL_HEADER32) : 
                    typeof(PeUtility.IMAGE_OPTIONAL_HEADER64);

                var subsysVal = peFile.Is32BitHeader ?
                    (PeUtility.SubSystemType)peFile.OptionalHeader32.Subsystem :
                    (PeUtility.SubSystemType)peFile.OptionalHeader64.Subsystem;

                subsysOffset += Marshal.OffsetOf(headerType, "Subsystem").ToInt32();

                switch (subsysVal)
                {
                    case PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI:
                        logger?.Info("Executable file is already a Win32 App!");
                        return true;
                    case PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_CUI:

                        var subsysSetting = BitConverter.GetBytes((ushort)PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI);

                        if (!BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(subsysSetting);
                        }

                        if (peFile.Stream.CanWrite)
                        {
                            peFile.Stream.Seek(subsysOffset, SeekOrigin.Begin);
                            peFile.Stream.Write(subsysSetting, 0, subsysSetting.Length);
                        }
                        else
                        {
                            logger.Error("Can't write changes! Conversion failed...");
                        }

                        return true;
                    default:
                        logger.Error($"Unsupported subsystem : {Enum.GetName(typeof(PeUtility.SubSystemType), subsysVal)}.");
                        return false;
                }
            }
        }
    }
}
