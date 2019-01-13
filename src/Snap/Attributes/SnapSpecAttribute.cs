using System;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal sealed class SnapSpecAttribute : Attribute
    {
        public string YamlString { get; set; }
    }
}
