using System;
using System.IO;
using Mono.Cecil;

namespace Snap.Extensions
{
    internal static class AssemblyDefinitionExtensions
    {
        public static void WriteAssemblyAndSymbols(this AssemblyDefinition assemblyDefinition, Stream assemblyStream, Stream assemblySymbolStream)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (assemblyStream == null) throw new ArgumentNullException(nameof(assemblyStream));
            if (assemblySymbolStream == null) throw new ArgumentNullException(nameof(assemblySymbolStream));

            assemblyDefinition.Write(assemblyStream, new WriterParameters
            {
                SymbolStream = assemblySymbolStream,
                WriteSymbols = assemblyDefinition.MainModule.SymbolReader != null,
                SymbolWriterProvider = assemblyDefinition.MainModule.SymbolReader?.GetWriterProvider()
            });

            assemblyStream.Seek(0, SeekOrigin.Begin);
            assemblySymbolStream.Seek(0, SeekOrigin.Begin);
        }
    }
}
