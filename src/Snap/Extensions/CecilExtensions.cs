using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Extensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    internal static class CecilExtensions
    {
        const string ExpressionCannotBeNullMessage = "The expression cannot be null";
        const string InvalidExpressionMessage = "Invalid expression";

        public static byte[] ToByteArray([NotNull] this AssemblyDefinition assemblyDefinition, WriterParameters writerParameters = null)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            using var srcStream = new MemoryStream();
            assemblyDefinition.Write(srcStream, writerParameters ?? new WriterParameters());
            return srcStream.ToArray();
        }

        public static string BuildMemberName<T>(this Expression<Func<T, object>> expression)
        {
            return BuildMemberName(expression.Body);
        }

        public static string BuildPropertyGetterSyntax<T>(this Expression<Func<T, object>> expression)
        {
            var propertyName = expression.BuildMemberName();
            return $"get_{propertyName}";
        }

        public static string BuildPropertySetterSyntax<T>(this Expression<Func<T, object>> expression)
        {
            var propertyName = expression.BuildMemberName();
            return $"set_{propertyName}";
        }

        public static TypeDefinition ResolveTypeDefinition<T>([NotNull] this AssemblyDefinition assemblyDefinition)
        {
            return ResolveTypeDefinitionImpl<T>(assemblyDefinition);
        }

        public static (TypeDefinition typeDefinition, PropertyDefinition propertyDefinition, string getterName, string setterName) ResolveAutoProperty<T>([NotNull] this AssemblyDefinition assemblyDefinition, [NotNull] Expression<Func<T, object>> selector)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var getter = selector.BuildPropertyGetterSyntax();
            var setter = selector.BuildPropertySetterSyntax();

            var typeDefinition = assemblyDefinition.ResolveTypeDefinition<T>();
            var propertyDefinition = typeDefinition?.Properties.SingleOrDefault(m =>
                m.GetMethod?.Name == getter || m.SetMethod?.Name == setter);

            return (typeDefinition, propertyDefinition, getter, setter);
        }

        static string BuildMemberName(Expression expression)
        {
            switch (expression)
            {
                case null:
                    throw new ArgumentException(ExpressionCannotBeNullMessage);
                case MemberExpression memberExpression:
                    // Reference type property or field
                    return memberExpression.Member.Name;
                case MethodCallExpression methodCallExpression:
                    // Reference type method
                    return methodCallExpression.Method.Name;
                case UnaryExpression unaryExpression:
                    // Property, field of method returning value type
                    return BuildMemberName(unaryExpression);
                default:
                    throw new ArgumentException(InvalidExpressionMessage);
            }
        }

        static string BuildMemberName(UnaryExpression unaryExpression)
        {
            if (unaryExpression.Operand is MethodCallExpression methodExpression)
            {
                return methodExpression.Method.Name;
            }

            return ((MemberExpression)unaryExpression.Operand).Member.Name;
        }

        static TypeDefinition ResolveTypeDefinitionImpl<T>([NotNull] AssemblyDefinition assemblyDefinition)
        {
            var tSourceFullName = typeof(T).FullName;
            var tSource = assemblyDefinition.MainModule.Types.SingleOrDefault(x => x.FullName == tSourceFullName);
            return tSource;
        }
    }
}
