using System.Collections.Generic;
using System.Linq;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Installer.Assets;

namespace Snap.Installer.Core
{
    internal interface IInstallerEmbeddedResources : IEmbedResources
    {
        List<byte[]> GifAnimation { get; }
    }

    internal sealed class InstallerEmbeddedResources : EmbeddedResources, IInstallerEmbeddedResources
    {
        public List<byte[]> GifAnimation { get; }

        public InstallerEmbeddedResources()
        {
            AddFromTypeRoot(typeof(AssetsTypeRoot), x => x.StartsWith("Snap.Installer.Assets"));

            GifAnimation = new List<byte[]>();

            const string animatedGifNs = "AnimatedGif.";
            foreach (var image in Resources.Where(x => x.Filename.StartsWith(animatedGifNs)).OrderBy(x => x.Filename.Substring(animatedGifNs.Length).ToIntSafe()))
            {
                GifAnimation.Add(image.Stream.ToArray());
            }
        }

    }
}
