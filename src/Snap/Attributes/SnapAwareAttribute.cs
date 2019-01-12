using System;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapAwareAttribute : Attribute
    {

    }


    [AttributeUsage(AttributeTargets.Assembly)]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapAttribute : Attribute
    {
        public SnapApp App { get; set; }
    }
}
