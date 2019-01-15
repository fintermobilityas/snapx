using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ILRepacking;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.AnyOS.Windows;
using Snap.Attributes;
using Snap.Core.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Snap.Logging;
using Snap.Extensions;

namespace Snap.Core
{
    internal interface ISnapSpecsWriter
    {
        Task<string> ConvertUpdateExeToConsoleApplicationUnitTest(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken);
        Task<string> ConvertUpdateExeToConsoleApplication(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken);
        Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsyncUnitTest(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken);
        Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsync(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken);
        AssemblyDefinition BuildSnapAppSpecAssembly(SnapAppSpec snapAppSpec);
        string ToSnapAppSpecYamlString(SnapAppSpec snapAppSpec);
    }

    internal sealed class SnapSpecsWriter : ISnapSpecsWriter
    {
        static readonly ILog Logger = LogProvider.For<SnapSpecsWriter>();

        public const string SnapAppSpecLibraryName = "SnapAppSpec";
        public const string SnapAppSpecDllFilename = SnapAppSpecLibraryName + ".dll";
        public const string SnapUpdateDllFilename = "Snap.Update.dll";
        public const string SnapUpdateDllUnitTestFilename = "Snap.Update.UnitTest.dll";
        public const string SnapUpdateExeFilename = "Update.exe";
        public const string SnapDllFilename = "Snap.dll";
        public const string SnapDllILRepackedUnitTestFilename = "Snap.ILRepacked.UnitTest.dll";

        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .Build();

        
        public Task<string> ConvertUpdateExeToConsoleApplicationUnitTest(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            return ConvertUpdateExeToConsoleApplication(workingDirectory, SnapUpdateDllUnitTestFilename, SnapUpdateExeFilename, filesystem, cancellationToken);
        }
           
        public Task<string> ConvertUpdateExeToConsoleApplication(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            return ConvertUpdateExeToConsoleApplication(workingDirectory, SnapUpdateDllFilename, SnapUpdateExeFilename, filesystem, cancellationToken);
        }

        public async Task<string> ConvertUpdateExeToConsoleApplication(string workingDirectory, string updateDllFilename, string updateExeFilename,
            ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));

            if (!filesystem.DirectoryExists(workingDirectory))
            {
                throw new DirectoryNotFoundException(workingDirectory);
            }

            var updateDllSrcFilename = Path.Combine(workingDirectory, updateDllFilename);
            var updateDllDstFilename = Path.Combine(workingDirectory, updateExeFilename);

            if (!filesystem.FileExists(updateDllSrcFilename))
            {
                throw new FileNotFoundException(updateDllSrcFilename);
            }

            var updateDllMemoryStream = await filesystem.FileReadAsync(updateDllSrcFilename, cancellationToken);
            using (var peFile = new PeUtility(updateDllMemoryStream)) // Closes stream
            {
                var subsysOffset = peFile.MainHeaderOffset;
                var headerType = peFile.Is32BitHeader ? typeof(PeUtility.IMAGE_OPTIONAL_HEADER32) : typeof(PeUtility.IMAGE_OPTIONAL_HEADER64);
                var subsysVal = peFile.Is32BitHeader ? (PeUtility.SubSystemType) peFile.OptionalHeader32.Subsystem : (PeUtility.SubSystemType) peFile.OptionalHeader64.Subsystem;

                subsysOffset += Marshal.OffsetOf(headerType, "Subsystem").ToInt32();

                switch (subsysVal)
                {
                    case PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_CUI:
                        var subsysSetting = BitConverter.GetBytes((ushort) PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI);

                        if (!BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(subsysSetting);
                        }

                        peFile.Stream.Seek(subsysOffset, SeekOrigin.Begin);
                        peFile.Stream.Write(subsysSetting, 0, subsysSetting.Length);

                        break;

                    case PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI:
                        throw new Exception("Executable file is already a Win32 App!");
                    default:
                        throw new Exception($"Unsupported subsystem : {Enum.GetName(typeof(PeUtility.SubSystemType), subsysVal)}.");
                }

                peFile.Stream.Seek(0, SeekOrigin.Begin);

                await filesystem.FileWriteAsync(peFile.Stream, updateDllDstFilename, cancellationToken);

                return updateDllDstFilename;
            }
        }
        
              
        public Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsyncUnitTest(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            return MergeSnapCoreIntoUpdateExeAsync(workingDirectory, SnapUpdateExeFilename, SnapDllILRepackedUnitTestFilename, SnapUpdateDllUnitTestFilename, filesystem, cancellationToken);
        }

        public Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsync(string workingDirectory, ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            return MergeSnapCoreIntoUpdateExeAsync(workingDirectory, SnapUpdateExeFilename, SnapDllFilename, SnapUpdateDllFilename, filesystem, cancellationToken);
        }

        public async Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsync(string workingDirectory, string updateExeFilename, string snapDllFilename, string snapUpdateDllFilename,
            ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));

            if (!filesystem.DirectoryExists(workingDirectory))
            {
                throw new DirectoryNotFoundException(workingDirectory);
            }

            var updateExeSrcFilename = Path.Combine(workingDirectory, updateExeFilename);
            var snapDllSrcFilename = Path.Combine(workingDirectory, snapDllFilename);
            var snapUpdateDllSrcFilename = Path.Combine(workingDirectory, snapUpdateDllFilename);

            if (!filesystem.FileExists(updateExeSrcFilename))
            {
                throw new FileNotFoundException(updateExeSrcFilename);
            }

            if (!filesystem.FileExists(snapDllSrcFilename))
            {
                throw new FileNotFoundException(snapDllSrcFilename);
            }
            
            if (!filesystem.FileExists(snapUpdateDllSrcFilename))
            {
                throw new FileNotFoundException(snapUpdateDllSrcFilename);
            }

            using (var tmpDirectory = new DisposableTempDirectory(workingDirectory, filesystem))
            {
                var updateExeTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, updateExeFilename);
                var updateExePdbTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, $"{Path.GetFileNameWithoutExtension(updateExeFilename)}.exe.pdb");
                var snapDllTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, snapDllFilename);
                var snapUpdateDllTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, snapUpdateDllFilename);

                await filesystem.FileCopyAsync(snapDllSrcFilename, snapDllTmpFilename, cancellationToken);
                await filesystem.FileCopyAsync(snapUpdateDllSrcFilename, snapUpdateDllTmpFilename, cancellationToken);
               
                var inputAssemblies = new[] { snapUpdateDllSrcFilename, snapDllTmpFilename };

                var repackOptions = new RepackOptions
                {
                    InputAssemblies = inputAssemblies,
                    OutputFile = updateExeTmpFilename,
                    SearchDirectories = new List<string>(),      
                    DebugInfo = true
                };

                var ilRepack = new ILRepacking.ILRepack(repackOptions);

                ilRepack.Repack();

                if (!filesystem.FileExists(updateExeTmpFilename))
                {
                    throw new FileNotFoundException("Unknown error during il-repacking, missing output exe.", updateExeTmpFilename);
                }

                if (!filesystem.FileExists(updateExePdbTmpFilename))
                {
                    throw new FileNotFoundException("Unknown error during il-repacking, missing output exe pdb.", updateExePdbTmpFilename);
                }

                var updateExeMemoryStream = await filesystem.FileReadAsync(updateExeTmpFilename, cancellationToken);
                var updateExePdb = await filesystem.FileReadAsync(updateExePdbTmpFilename, cancellationToken);

                return (updateExeMemoryStream, updateExePdb);
            }
        }

        public AssemblyDefinition BuildSnapAppSpecAssembly([NotNull] SnapAppSpec snapAppSpec)
        {
            if (snapAppSpec == null) throw new ArgumentNullException(nameof(snapAppSpec));

            var yamlSnapAppSpecStr = ToSnapAppSpecYamlString(snapAppSpec);

            var currentVersion = snapAppSpec.Version;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(SnapAppSpecLibraryName, new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Patch)), SnapAppSpecLibraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var attributeConstructor = mainModule.ImportReference(
                typeof(SnapSpecAttribute).GetConstructor(Type.EmptyTypes));

            assembly.CustomAttributes.Add(new CustomAttribute(attributeConstructor));

            var snapAppSpecEmbeddedResource = new EmbeddedResource("SnapAppSpec", ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(yamlSnapAppSpecStr));
            mainModule.Resources.Add(snapAppSpecEmbeddedResource);

            return assembly;
        }

        public string ToSnapAppSpecYamlString([NotNull] SnapAppSpec snapAppSpec)
        {
            if (snapAppSpec == null) throw new ArgumentNullException(nameof(snapAppSpec));
            return YamlSerializer.Serialize(snapAppSpec);
        }
    }
}
