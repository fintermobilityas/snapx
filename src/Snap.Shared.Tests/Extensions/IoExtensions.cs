using System;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using Snap.Core;

namespace Snap.Shared.Tests.Extensions
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static class IoExtensions
    {
        public static void DeleteFileSafe(this string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            try
            {
                if(File.Exists(filename))
                {
                    File.Delete(filename);
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        public static void DeleteResidueSnapAppSpec(this string workingDirectory)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            foreach (var file in Directory.GetFiles(workingDirectory, "*.dll"))
            {
                if (file.StartsWith(SnapSpecsWriter.SnapAppSpecLibraryName))
                {
                    file.DeleteFileSafe();
                }
            }
        }
    }
}
