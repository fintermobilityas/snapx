// Source: https://github.com/jmacato/NSubsys

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Snap.Logging;

// ReSharper disable once CheckNamespace
namespace NSubsys
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class NSubsys
    {
        static readonly ILog Logger = LogProvider.For<NSubsys>();

        public static bool ProcessFile([NotNull] FileStream exeFileStream)
        {
            if (exeFileStream == null) throw new ArgumentNullException(nameof(exeFileStream));

            Logger.Info("NSubsys : Subsystem Changer for Windows PE files.");

			using (var peFile = new PeUtility(exeFileStream))
            {
                var subsysOffset = peFile.MainHeaderOffset;
                var headerType = peFile.Is32BitHeader ? typeof(PeUtility.IMAGE_OPTIONAL_HEADER32) : typeof(PeUtility.IMAGE_OPTIONAL_HEADER64);
                var subsysVal = peFile.Is32BitHeader ? (PeUtility.SubSystemType) peFile.OptionalHeader32.Subsystem : (PeUtility.SubSystemType) peFile.OptionalHeader64.Subsystem;

                subsysOffset += Marshal.OffsetOf(headerType, "Subsystem").ToInt32();

                switch (subsysVal)
                {
                    case PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI:
                        Logger.Info("Executable file is already a Win32 App!");
                        return true;
                    case PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_CUI:
                        Logger.Info("Console app detected...");
                        Logger.Info("Converting...");

                        var subsysSetting = BitConverter.GetBytes((ushort)PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI);

                        if (!BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(subsysSetting);
                        }

                        if (peFile.Stream.CanWrite)
                        {
                            peFile.Stream.Seek(subsysOffset, SeekOrigin.Begin);
                            peFile.Stream.Write(subsysSetting, 0, subsysSetting.Length);
                            Logger.Info("Conversion Complete...");
                        }
                        else
                        {
                            Logger.Info("Can't write changes!");
                            Logger.Info("Conversion Failed...");
                        }

                        return true;
                    default:
                        Logger.Error($"Unsupported subsystem : {Enum.GetName(typeof(PeUtility.SubSystemType), subsysVal)}.");
                        return false;
                }
            }
        }
    }
}
