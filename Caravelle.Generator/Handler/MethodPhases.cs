using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using System.Linq;

namespace Caravelle.Generator.Handler;

public sealed record ReturnElement(
    string LocalName,
    string FullType,
    bool IsNullable,
    bool IsResultType,
    bool RequiresNullCheck)
{
    public string NonNullLocalName => IsNullable ? LocalName + "Value" : LocalName;
}

public sealed record InputParameter(
    string LocalName,
    string FullType,
    bool IsNullable,
    string NotNullMessage,
    bool IsFromServices = false);

public enum PhaseType
{
    Before,
    Handle,
    After,
    Finally
}


public sealed record MethodPhase
{
    public MethodPhase(PhaseType type, string methodName, bool isAsync,
        bool isStatic,
        EquatableArray<InputParameter> parameters,
        EquatableArray<ReturnElement> returns)
    {
        Type = type;
        MethodName = methodName;
        IsAsync = isAsync;
        IsStatic = isStatic;
        Parameters = parameters;
        Returns = returns;
    }

    public MethodPhase(PhaseType type, IMethodSymbol methodSymbol, SymbolDisplayFormat format)
    {
        Type = type;
        MethodName = methodSymbol.Name;
        IsStatic = methodSymbol.IsStatic;

        //Determine the Method Parameters
        var parameters = new List<InputParameter>();
        foreach (var parameter in methodSymbol.Parameters)
        {
            var isNullable = parameter.NullableAnnotation == NullableAnnotation.Annotated;
            var nonNullable = isNullable
                ? parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                : parameter.Type;
            var fullType = nonNullable.ToDisplayString(format);

            var req = parameter.GetAttributes().FirstOrDefault(static a =>
                a.AttributeClass?.Name == "RequiredAttribute"
                && a.AttributeClass.ContainingNamespace?.ToDisplayString()
                == "System.ComponentModel.DataAnnotations");

            var notNullMessage = $"{parameter.Name} cannot be null";
            if (req is not null)
            {
                var msgArg = req.NamedArguments
                    .FirstOrDefault(static kv => kv.Key == "ErrorMessage");
                notNullMessage = msgArg.Value.Value as string ?? notNullMessage;
                isNullable = false;
            }

            parameters.Add(new(parameter.Name, fullType, isNullable, notNullMessage));
        }

        Parameters = new(parameters.ToArray());


        // Determine the Method Return values
        var (innerType, isAsync) = UnwrapTask(methodSymbol.ReturnType);
        IsAsync = isAsync;

        // Handle void and non-generic Task returns (used by Finally phase) - they have no return values
        if (innerType.SpecialType == SpecialType.System_Void ||
            (innerType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 }))
        {
            Returns = EquatableArray<ReturnElement>.Empty;
        }
        else if (innerType is INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            var elements = new List<ReturnElement>();
            foreach (var elem in tupleType.TupleElements)
            {
                var elemType = elem.Type;
                var isNullable = elemType.NullableAnnotation == NullableAnnotation.Annotated;
                var nonNullType = isNullable
                    ? elemType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    : elemType;
                var fullType = nonNullType.ToDisplayString(format);
                var isResultType = ImplementsResultInterface(nonNullType);
                var requiresNullCheck = isNullable || !nonNullType.IsValueType;
                var rawName = elem.Name;
                var localName = rawName.Length > 0 && char.IsUpper(rawName[0])
                    ? char.ToLower(rawName[0]) + rawName.Substring(1)
                    : rawName;
                elements.Add(new(string.Concat(localName, MethodName), fullType, isNullable, isResultType, requiresNullCheck));
            }

            Returns = new(elements);
        }
        else
        {
            var isNullable = innerType.NullableAnnotation == NullableAnnotation.Annotated;
            var nonNullable = isNullable
                ? innerType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                : innerType;
            var fullType = nonNullable.ToDisplayString(format);
            var isResultType = ImplementsResultInterface(nonNullable);
            var requiresNullCheck = isNullable || !nonNullable.IsValueType;
            Returns = new([
                new(string.Concat("from", MethodName), fullType, isNullable, isResultType, requiresNullCheck)
            ]);
        }
    }

    public PhaseType Type { get; }
    public string MethodName { get; }
    public bool IsAsync { get; }
    public bool IsStatic { get; }
    public EquatableArray<InputParameter> Parameters { get; init; }
    public EquatableArray<ReturnElement> Returns { get; }

    /// <summary>
    /// The fully-qualified type name of the middleware class that contributed this phase,
    /// or <see langword="null"/> if it belongs to the handler itself (own class or an
    /// inherited base class — both are invoked on the same handler instance). Set via a
    /// <c>with</c> expression when merging matched middleware phases into a handler's
    /// pipeline; never populated by either constructor directly.
    /// </summary>
    public string? OwnerTypeFullName { get; init; }

    private static (ITypeSymbol Inner, bool IsAsync) UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } task)
            return (task.TypeArguments[0], true);
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
            return (returnType, true);
        return (returnType, false);
    }

    private static ITypeSymbol StripNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;

    /// <summary>
    /// Returns the unwrapped (Task-stripped, nullable-stripped, tuple-deconstructed) return
    /// type symbols of <paramref name="methodSymbol"/> — the same set of types that would
    /// become <see cref="ReturnElement.FullType"/> entries, but as live symbols instead of
    /// display strings. Used to build assignability closures for middleware filter matching;
    /// never persisted into a cached/equatable model.
    /// </summary>
    internal static IEnumerable<ITypeSymbol> GetUnwrappedReturnTypeSymbols(IMethodSymbol methodSymbol)
    {
        var (innerType, _) = UnwrapTask(methodSymbol.ReturnType);

        if (innerType.SpecialType == SpecialType.System_Void
            || innerType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
        {
            yield break;
        }

        if (innerType is INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            foreach (var elem in tupleType.TupleElements)
                yield return StripNullable(elem.Type);
        }
        else
        {
            yield return StripNullable(innerType);
        }
    }

    /// <summary>
    /// Returns the nullable-stripped parameter type symbols of <paramref name="methodSymbol"/>.
    /// Used alongside <see cref="GetUnwrappedReturnTypeSymbols"/> to build assignability
    /// closures for middleware filter matching.
    /// </summary>
    internal static IEnumerable<ITypeSymbol> GetParameterTypeSymbols(IMethodSymbol methodSymbol) =>
        methodSymbol.Parameters.Select(static p => StripNullable(p.Type));

    /// <summary>
    /// Computes the set of fully-qualified type names that <paramref name="type"/> is
    /// assignable to: itself, every interface it implements (transitively), and every
    /// base class up its inheritance chain (excluding <see cref="object"/>).
    /// </summary>
    internal static HashSet<string> GetAssignableClosure(ITypeSymbol type, SymbolDisplayFormat format)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal) { type.ToDisplayString(format) };

        foreach (var iface in type.AllInterfaces)
            closure.Add(iface.ToDisplayString(format));

        var baseType = type.BaseType;
        while (baseType is not null && baseType.SpecialType != SpecialType.System_Object)
        {
            closure.Add(baseType.ToDisplayString(format));
            baseType = baseType.BaseType;
        }

        return closure;
    }

    /// <summary>
    /// Builds a lookup from a type's fully-qualified display string (matching the
    /// convention used by <see cref="ReturnElement.FullType"/>/<see cref="InputParameter.FullType"/>)
    /// to its assignability closure, covering every return and parameter type symbol
    /// reachable from <paramref name="methods"/>. Ephemeral — built fresh per matching
    /// pass from live symbols, never cached.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> BuildTypeClosureIndex(
        IEnumerable<IMethodSymbol> methods, SymbolDisplayFormat format)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var method in methods)
        {
            foreach (var type in GetUnwrappedReturnTypeSymbols(method).Concat(GetParameterTypeSymbols(method)))
            {
                var key = type.ToDisplayString(format);
                if (!index.ContainsKey(key))
                    index[key] = GetAssignableClosure(type, format);
            }
        }

        return index;
    }

    private static bool ImplementsResultInterface(ITypeSymbol type)
    {
        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Equals("global::Caravelle.IValidationResult", StringComparison.Ordinal))
        {
            return true;
        }

        return type.AllInterfaces.Any(interfaceType =>
            interfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Equals("global::Caravelle.IValidationResult", StringComparison.Ordinal));
    }

}