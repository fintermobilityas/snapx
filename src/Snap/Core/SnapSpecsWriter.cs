using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ILRepacking;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Attributes;
using Snap.Core.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Snap.Logging;
using NSubsys;

namespace Snap.Core
{
    internal interface ISnapSpecsWriter
    {
        Task<MemoryStream> ConvertUpdateExeToConsoleApplication(string workingDirectory,
            ISnapFilesystem filesystem, CancellationToken cancellationToken);
        Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsync([NotNull] string workingDirectory, [NotNull] ISnapFilesystem filesystem, CancellationToken cancellationToken);
        AssemblyDefinition BuildSnapAppSpecAssembly(SnapAppSpec snapAppSpec);
        string ToSnapAppSpecYamlString(SnapAppSpec snapAppSpec);
    }

    internal sealed class SnapSpecsWriter : ISnapSpecsWriter
    {
        static readonly ILog Logger = LogProvider.For<SnapSpecsWriter>();

        public const string SnapAppSpecLibraryName = "SnapAppSpec";

        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .Build();

        public async Task<MemoryStream> ConvertUpdateExeToConsoleApplication(string workingDirectory, 
            ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));

            if (!filesystem.DirectoryExists(workingDirectory))
            {
                throw new DirectoryNotFoundException(workingDirectory);
            }

            const string updateDllFilename = "Update.dll";
            var updateDllSrcFilename = Path.Combine(workingDirectory, updateDllFilename);

            if (!filesystem.FileExists(updateDllSrcFilename))
            {
                throw new FileNotFoundException(updateDllSrcFilename);
            }

            using (var tmpDirectory = new DisposableTempDirectory(workingDirectory))
            {
                var updateDllTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, updateDllFilename);

                await filesystem.FileCopyAsync(updateDllSrcFilename, updateDllTmpFilename, cancellationToken);

                using (var fileStream = filesystem.OpenReadWrite(updateDllTmpFilename))
                {
                    if (!NSubsys.NSubsys.ProcessFile(fileStream))
                    {
                        throw new Exception($"Failed to convert {updateDllFilename} to console application. Is this a .NET core dll?");
                    }

                    return await filesystem.FileReadAsync(updateDllTmpFilename, cancellationToken);
                }

            }
        }
        
        public async Task<(MemoryStream updateExeMemoryStream, MemoryStream updateExePdb)> MergeSnapCoreIntoUpdateExeAsync(string workingDirectory, 
            ISnapFilesystem filesystem, CancellationToken cancellationToken)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));

            if (!filesystem.DirectoryExists(workingDirectory))
            {
                throw new DirectoryNotFoundException(workingDirectory);
            }

            var updateExeSrcFilename = Path.Combine(workingDirectory, "Update.exe");
            var snapDllSrcFilename = Path.Combine(workingDirectory, "Snap.dll");

            if (!filesystem.FileExists(updateExeSrcFilename))
            {
                throw new FileNotFoundException(updateExeSrcFilename);
            }

            if (!filesystem.FileExists(snapDllSrcFilename))
            {
                throw new FileNotFoundException(snapDllSrcFilename);
            }

            using (var tmpDirectory = new DisposableTempDirectory(workingDirectory))
            {
                var updateExeTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, "Update.exe");
                var updateExePdbTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, "Update.pdb");
                var snapDllTmpFilename = Path.Combine(tmpDirectory.AbsolutePath, "Snap.dll");

                await filesystem.FileCopyAsync(updateExeSrcFilename, updateExeTmpFilename, cancellationToken);
                await filesystem.FileCopyAsync(snapDllSrcFilename, snapDllTmpFilename, cancellationToken);

                var inputAssemblies = new[] { updateExeTmpFilename, snapDllTmpFilename };

                var ilRepack = new ILRepacking.ILRepack(new RepackOptions
                {
                    InputAssemblies = inputAssemblies,
                    OutputFile = updateExeTmpFilename,
                    DebugInfo = true
                });

                ilRepack.Repack();

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
