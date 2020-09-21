using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Snap.Core.Resources
{
    internal interface IEmbedResources : IDisposable
    {
        IEnumerable<EmbeddedResource> Resources { get; }
        void AddFromTypeRoot(Type typeRoot, Func<string, bool> filterFn = null);
        EmbeddedResource Find(Type typeRoot, string filename);
    }

    internal interface IEmbeddedResource : IDisposable
    {
        Type TypeRoot { get; }
        Assembly Assembly { get; }
        MemoryStream Stream { get; }
        string Filename { get; set; }
    }

    internal sealed class EmbeddedResource : IEmbeddedResource
    {
        public Type TypeRoot { get; }
        public Assembly Assembly => TypeRoot.Assembly;
        public MemoryStream Stream { get; }
        public string Filename { get; set; }

        public EmbeddedResource(Type typeRoot, MemoryStream stream, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(filename));
            TypeRoot = typeRoot ?? throw new ArgumentNullException(nameof(typeRoot));
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            Filename = filename;
        }

        public void Dispose()
        {
            Stream?.Dispose();
        }
    }

    internal abstract class EmbeddedResources : IEmbedResources
    {
        readonly List<EmbeddedResource> _resources;

        public IEnumerable<EmbeddedResource> Resources => _resources.AsReadOnly();

        protected internal EmbeddedResources()
        {
            _resources = new List<EmbeddedResource>();
        }

        public EmbeddedResource Find(Type typeRoot, string filename)
        {
            if (typeRoot == null) throw new ArgumentNullException(nameof(typeRoot));
            return _resources.SingleOrDefault(x => x.TypeRoot == typeRoot && string.Equals(filename, x.Filename));
        }

        public void AddFromTypeRoot(Type typeRoot, Func<string, bool> filterFn = null)
        {
            var typeRootNamespace = typeRoot?.FullName?.Substring(0, typeRoot.FullName.Length - 1 - typeRoot.Name.Length);
            if (string.IsNullOrWhiteSpace(typeRootNamespace))
            {
                return;
            }

            foreach (var resource in typeRoot.Assembly.GetManifestResourceNames().Where(resource =>
            {
                if (!resource.StartsWith(typeRootNamespace))
                {
                    return false;
                }

                return filterFn == null || filterFn(resource);
            }))
            {
                using var manifestResourceStream = typeRoot.Assembly.GetManifestResourceStream(resource);
                if (manifestResourceStream == null)
                {
                    continue;
                }

                using var binaryReader = new BinaryReader(manifestResourceStream);
                var embededResourceStream = new MemoryStream();

                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = binaryReader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    embededResourceStream.Write(buffer, 0, bytesRead);
                }

                embededResourceStream.Seek(0, SeekOrigin.Begin);

                _resources.Add(new EmbeddedResource(typeRoot, embededResourceStream, resource[(typeRootNamespace.Length + 1)..]));
            }
        }

        public void Dispose()
        {
            foreach (var embeddedResource in _resources)
            {
                embeddedResource.Dispose();
            }

            _resources.Clear();
        }
    }
}
