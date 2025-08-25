// Copyright © 2011-2015 Damian Hickey.  All rights reserved.
// LibLog-inspired reflection helpers for portability

using System;
using System.Reflection;

namespace Snap.Logging.LogProviders;

internal static class ReflectionExtensions
{
    public static MethodInfo GetMethod(this Type type, string name, params Type[] parameterTypes)
    {
        parameterTypes ??= Type.EmptyTypes;
        return type.GetMethod(name, parameterTypes);
    }

    public static ConstructorInfo GetConstructorPortable(this Type type, params Type[] parameterTypes)
    {
        parameterTypes ??= Type.EmptyTypes;
        return type.GetConstructor(parameterTypes);
    }
}
