﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Reflection;

internal interface ITypeReflector
{
    IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute;
    string FullName { get; }
    string Name { get; }
}

internal class CecilTypeReflector([NotNull] TypeDefinition type) : ITypeReflector
{
    readonly TypeDefinition _type = type ?? throw new ArgumentNullException(nameof(type));

    public string FullName => _type.FullName;
    public string Name => _type.Name;

    public IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute
    {
        if (!_type.HasCustomAttributes)
        {
            return Array.Empty<IAttributeReflector>();
        }

        var expectedTypeName = typeof(T).Name;
        return _type.CustomAttributes
            .Where(a => a.AttributeType.Name == expectedTypeName)
            .Select(a => new CecilAttributeReflector(a))
            .ToList();
    }

}