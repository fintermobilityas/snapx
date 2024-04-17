﻿using System.Collections.Generic;
using System.Linq;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Installer.Assets;

namespace Snap.Installer.Core;

internal interface ISnapInstallerEmbeddedResources : IEmbedResources
{
    List<byte[]> GifAnimation { get; }
}

internal sealed class SnapInstallerEmbeddedResources : EmbeddedResources, ISnapInstallerEmbeddedResources
{
    public List<byte[]> GifAnimation { get; }

    public SnapInstallerEmbeddedResources()
    {
        AddFromTypeRoot(typeof(AssetsTypeRoot), x => x.StartsWith("Snap.Installer.Assets"));

        GifAnimation = [];

        const string animatedGifNs = "AnimatedGif.";
        foreach (var image in Resources.Where(x => x.Filename.StartsWith(animatedGifNs)).OrderBy(x => x.Filename[animatedGifNs.Length..].ToIntSafe()))
        {
            GifAnimation.Add(image.Stream.ToArray());
        }
    }

}