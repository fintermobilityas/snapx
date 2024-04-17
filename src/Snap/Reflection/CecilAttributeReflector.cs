﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Reflection;

internal interface IAttributeReflector
{
    IDictionary<string, string> Values { get; }
}

internal class CecilAttributeReflector([NotNull] CustomAttribute attribute) : IAttributeReflector
{
    readonly CustomAttribute _attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
    IDictionary<string, string> _values;

    public IDictionary<string, string> Values
    {
        get
        {
            if (_values != null)
            {
                return _values;
            }

            _values = new Dictionary<string, string>();
            var constructorArguments = _attribute.Constructor.Resolve().Parameters.Select(p => p.Name).ToList();
            var constructorParameters = _attribute.ConstructorArguments.Select(a => a.Value.ToString()).ToList();
            for (var i = 0; i < constructorArguments.Count; i++)
            {
                _values.Add(constructorArguments[i], constructorParameters[i]);
            }

            foreach (var prop in _attribute.Properties)
            {
                _values.Add(prop.Name, prop.Argument.Value.ToString());
            }

            return _values;
        }
    }
}