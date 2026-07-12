using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Caravelle.Generator;

/// <summary>
/// Method-name conventions and symbol-tree discovery logic
/// </summary>
internal static class Helpers
{
    internal static bool IsSupportedFinallyReturnType(IMethodSymbol method)
    {
        var returnType = method.ReturnType;

        // Finally supports void or non-generic Task only
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
        {
            return true;
        }

        return false;
    }

    internal static List<INamedTypeSymbol> GetInheritanceChain(INamedTypeSymbol classSymbol)
    {
        var chain = new List<INamedTypeSymbol> { classSymbol };
        var current = classSymbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            chain.Add(current);
            current = current.BaseType;
        }

        return chain;
    }

    internal static List<(IMethodSymbol Method, int Depth)> CollectPhaseMethods(
        IReadOnlyList<INamedTypeSymbol> inheritanceChain,
        Func<string, bool> nameMatches)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(IMethodSymbol Method, int Depth)>();

        for (var depth = 0; depth < inheritanceChain.Count; depth++)
        {
            var methods = inheritanceChain[depth].GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Where(m => nameMatches(m.Name))
                .OrderBy(m => m.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan.Start ?? int.MaxValue)
                .ThenBy(m => m.Name, StringComparer.Ordinal);

            // Dedupe across the chain by method name: the shallowest (most-derived)
            // declaration wins, matching real C# override/hiding semantics since
            // `_handler.MethodName(...)` can only ever bind to one implementation.
            foreach (var method in methods)
            {
                if (seenNames.Add(method.Name))
                {
                    result.Add((method, depth));
                }
            }
        }

        return result;
    }

    internal static List<(IMethodSymbol Method, int Depth)> CollectFinallyMethods(
        IReadOnlyList<INamedTypeSymbol> inheritanceChain)
    {
        var result = new List<(IMethodSymbol Method, int Depth)>();

        for (var depth = 0; depth < inheritanceChain.Count; depth++)
        {
            // A single class can only contribute one Finally method (it matches either
            // "Finally" or "FinallyAsync", not both), so pick at most one per depth.
            var method = inheritanceChain[depth].GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Where(m => IsFinallyMethodName(m.Name))
                .OrderBy(m => m.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan.Start ?? int.MaxValue)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .FirstOrDefault();

            if (method is not null)
            {
                result.Add((method, depth));
            }
        }

        return result;
    }

    internal static bool IsPreHandleMethodName(string methodName)
    {
        return methodName == "Load"
            || methodName == "LoadAsync"
            || methodName == "Validate"
            || methodName == "ValidateAsync"
            || methodName.StartsWith("Before", StringComparison.Ordinal)
            || methodName.EndsWith("Before", StringComparison.Ordinal)
            || methodName.EndsWith("BeforeAsync", StringComparison.Ordinal);
    }

    internal static bool IsPostHandleMethodName(string methodName)
    {
        return methodName.StartsWith("After", StringComparison.Ordinal)
            || methodName.StartsWith("Post", StringComparison.Ordinal);
    }

    internal static bool IsFinallyMethodName(string methodName)
    {
        return methodName == "Finally"
            || methodName == "FinallyAsync";
    }

    internal static bool HasGenericContainingType(INamedTypeSymbol? typeSymbol)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.Arity > 0) return true;
            current = current.ContainingType;
        }

        return false;
    }

    internal static ITypeSymbol StripNullable(ITypeSymbol type, out bool isNullable)
    {
        isNullable = type.NullableAnnotation == NullableAnnotation.Annotated;
        return isNullable ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;
    }

    internal static string GetHashForTypeName(string typeName)
    {
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(typeName));
        var chars = new char[8];
        for (var i = 0; i < 4; i++)
        {
            var b = hashBytes[i];
            chars[i * 2] = ToHexChar((b >> 4) & 0xF);
            chars[i * 2 + 1] = ToHexChar(b & 0xF);
        }

        return new (chars);
    }
    private static char ToHexChar(int value) =>
        (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}
