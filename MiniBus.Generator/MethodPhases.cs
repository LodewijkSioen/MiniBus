using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;

namespace MiniBus.Generator;

public sealed record LoadedElement(
    string LocalName,
    string FullType,
    bool IsNullable,
    string? NotFoundMessage = null)
{
    public string NonNullLocalName => IsNullable ? LocalName + "Value" : LocalName;
}

public interface IHandlerPhaseInfo
{
}

public interface IAsyncPhaseInfo : IHandlerPhaseInfo
{
    bool IsAsync { get; }
}

public interface IPreHandlePhaseInfo : IAsyncPhaseInfo
{
    int Order { get; set; }
    int TieBreak { get; }
}

public interface IInvocablePhaseInfo : IAsyncPhaseInfo
{
    string MethodName { get; }
    string CallArgs { get; }
    ImmutableArray<string> UnsupportedParameters { get; }
}

public interface IMethodPhaseInfo : IHandlerPhaseInfo
{
    ImmutableArray<string> InputTypeFqns { get; }
    ImmutableArray<string> OutputTypeFqns { get; }
}

public sealed class LoadMethodPhase : IMethodPhaseInfo, IPreHandlePhaseInfo, IInvocablePhaseInfo
{
    private readonly string _methodName;

    public LoadMethodPhase(IMethodSymbol methodSymbol, SymbolDisplayFormat format)
    {
        _methodName = methodSymbol.Name;
        MethodSymbol = methodSymbol;
        Parameters = methodSymbol.Parameters;
        InputTypeFqns = methodSymbol.Parameters
            .Select(p => p.Type.ToDisplayString(format))
            .ToImmutableArray();

        var (innerType, isAsync) = UnwrapTask(methodSymbol.ReturnType);
        IsAsync = isAsync;

        if (innerType is INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            IsTuple = true;
            var elements = ImmutableArray.CreateBuilder<LoadedElement>();
            foreach (var elem in tupleType.TupleElements)
            {
                var elemType = elem.Type;
                var isNullable = elemType.NullableAnnotation == NullableAnnotation.Annotated;
                var nonNullType = isNullable
                    ? elemType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    : elemType;
                var fullType = nonNullType.ToDisplayString(format);
                var rawName = elem.Name;
                var localName = rawName.Length > 0 && char.IsUpper(rawName[0])
                    ? char.ToLower(rawName[0]) + rawName.Substring(1)
                    : rawName;
                elements.Add(new LoadedElement(localName, fullType, isNullable));
            }

            Elements = elements.ToImmutable();
        }
        else
        {
            IsTuple = false;
            var isNullable = innerType.NullableAnnotation == NullableAnnotation.Annotated;
            var nonNullable = isNullable
                ? innerType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                : innerType;
            var fullType = nonNullable.ToDisplayString(format);
            Elements = ImmutableArray.Create(new LoadedElement("loaded", fullType, isNullable));
        }

        OutputTypeFqns = Elements
            .Select(static e => e.FullType)
            .ToImmutableArray();
    }

    public LoadMethodPhase(
        bool isAsync,
        bool isTuple,
        int order,
        ImmutableArray<LoadedElement> elements,
        string callArgs = "request",
        ImmutableArray<string> unsupportedParameters = default,
        string methodName = "Load")
    {
        _methodName = methodName;
        IsAsync = isAsync;
        IsTuple = isTuple;
        Order = order;
        Elements = elements;
        CallArgs = callArgs;
        UnsupportedParameters = unsupportedParameters.IsDefault ? ImmutableArray<string>.Empty : unsupportedParameters;
        Parameters = ImmutableArray<IParameterSymbol>.Empty;
        InputTypeFqns = ImmutableArray<string>.Empty;
        OutputTypeFqns = Elements.Select(static e => e.FullType).ToImmutableArray();
    }

    public IMethodSymbol? MethodSymbol { get; }
    public ImmutableArray<IParameterSymbol> Parameters { get; }
    public string MethodName => _methodName;
    public bool IsAsync { get; }
    public bool IsTuple { get; }
    public int Order { get; set; }
    public int TieBreak => 0;
    public string CallArgs { get; set; } = string.Empty;
    public ImmutableArray<string> UnsupportedParameters { get; set; } = ImmutableArray<string>.Empty;
    public ImmutableArray<LoadedElement> Elements { get; set; }
    public ImmutableArray<string> InputTypeFqns { get; }
    public ImmutableArray<string> OutputTypeFqns { get; }

    private static (ITypeSymbol Inner, bool IsAsync) UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task" } task && task.TypeArguments.Length == 1)
            return (task.TypeArguments[0], true);
        return (returnType, false);
    }
}

public sealed class ValidateMethodPhase : IMethodPhaseInfo, IPreHandlePhaseInfo, IInvocablePhaseInfo
{
    private readonly string _methodName;

    public ValidateMethodPhase(IMethodSymbol methodSymbol, SymbolDisplayFormat format)
    {
        _methodName = methodSymbol.Name;
        MethodSymbol = methodSymbol;
        Parameters = methodSymbol.Parameters;
        InputTypeFqns = methodSymbol.Parameters
            .Select(p => p.Type.ToDisplayString(format))
            .ToImmutableArray();

        var (innerType, isAsync) = UnwrapTask(methodSymbol.ReturnType);
        IsAsync = isAsync;
        ReturnsValidationResult = innerType.ToDisplayString(format) == "global::MiniBus.ValidationResult";
    }

    public ValidateMethodPhase(
        bool isAsync,
        int order,
        string callArgs,
        ImmutableArray<string> unsupportedParameters,
        string methodName = "Validate")
    {
        _methodName = methodName;
        IsAsync = isAsync;
        Order = order;
        CallArgs = callArgs;
        UnsupportedParameters = unsupportedParameters;
        ReturnsValidationResult = true;
        Parameters = ImmutableArray<IParameterSymbol>.Empty;
        InputTypeFqns = ImmutableArray<string>.Empty;
    }

    public IMethodSymbol? MethodSymbol { get; }
    public ImmutableArray<IParameterSymbol> Parameters { get; }
    public string MethodName => _methodName;
    public bool IsAsync { get; }
    public int Order { get; set; }
    public int TieBreak => 1;
    public string CallArgs { get; set; } = string.Empty;
    public ImmutableArray<string> UnsupportedParameters { get; set; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> InputTypeFqns { get; }
    public ImmutableArray<string> OutputTypeFqns => ImmutableArray<string>.Empty;
    public bool ReturnsValidationResult { get; }

    private static (ITypeSymbol Inner, bool IsAsync) UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task" } task && task.TypeArguments.Length == 1)
            return (task.TypeArguments[0], true);
        return (returnType, false);
    }
}

public sealed class HandleMethodPhase : IMethodPhaseInfo, IInvocablePhaseInfo
{
    private readonly string _methodName;

    public HandleMethodPhase(IMethodSymbol methodSymbol, SymbolDisplayFormat format)
    {
        _methodName = methodSymbol.Name;
        MethodSymbol = methodSymbol;
        Parameters = methodSymbol.Parameters;
        InputTypeFqns = methodSymbol.Parameters
            .Select(p => p.Type.ToDisplayString(format))
            .ToImmutableArray();

        var (innerType, isAsync) = UnwrapTask(methodSymbol.ReturnType);
        IsAsync = isAsync;
        FullResponseType = innerType.ToDisplayString(format);
        OutputTypeFqns = ImmutableArray.Create(FullResponseType);
    }

    public HandleMethodPhase(
        bool isAsync,
        string fullResponseType,
        string callArgs,
        ImmutableArray<string> unsupportedParameters,
        string methodName = "Handle")
    {
        _methodName = methodName;
        IsAsync = isAsync;
        FullResponseType = fullResponseType;
        CallArgs = callArgs;
        UnsupportedParameters = unsupportedParameters;
        Parameters = ImmutableArray<IParameterSymbol>.Empty;
        InputTypeFqns = ImmutableArray<string>.Empty;
        OutputTypeFqns = ImmutableArray.Create(fullResponseType);
    }

    public IMethodSymbol? MethodSymbol { get; }
    public ImmutableArray<IParameterSymbol> Parameters { get; }
    public string MethodName => _methodName;
    public bool IsAsync { get; }
    public string FullResponseType { get; }
    public string CallArgs { get; set; } = string.Empty;
    public ImmutableArray<string> UnsupportedParameters { get; set; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> InputTypeFqns { get; }
    public ImmutableArray<string> OutputTypeFqns { get; }

    private static (ITypeSymbol Inner, bool IsAsync) UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task" } task && task.TypeArguments.Length == 1)
            return (task.TypeArguments[0], true);
        return (returnType, false);
    }
}
