using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Serialization;

namespace Snap.Core.Specs
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal sealed class SnapApps : IEnumerable<SnapApp>
    {
        public List<SnapFeed> Feeds { get; set; }
        public List<SnapChannel> Channels { get; set; }
        public List<SnapSignature> Signatures { get; set; }
        [YamlMember(Alias = "snaps")]
        public List<SnapApp> Apps { get; set; }

        public SnapApps()
        {
            Feeds = new List<SnapFeed>();
            Channels = new List<SnapChannel>();
            Signatures = new List<SnapSignature>();
            Apps = new List<SnapApp>();
        }

        public IEnumerator<SnapApp> GetEnumerator()
        {
            return Apps.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
