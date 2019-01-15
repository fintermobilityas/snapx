using System.IO;
using Snap.AnyOS.Windows;

namespace Snap.Extensions
{
    internal static class PeUtilityExtensions
    {
        public static (PeUtility.SubSystemType subSystemType, bool is32Bit, bool is64Bit) GetPeDetails(this Stream stream)
        {
            using (var peFile = new PeUtility(stream))
            {
                var subsysVal = peFile.Is32BitHeader ? (PeUtility.SubSystemType) peFile.OptionalHeader32.Subsystem : (PeUtility.SubSystemType) peFile.OptionalHeader64.Subsystem;

                return (subsysVal, peFile.Is32BitHeader, !peFile.Is32BitHeader);
            }
        }
    }
}
