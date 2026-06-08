using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using System.Linq;

namespace MiniBus.Generator;

public sealed record ReturnElement(
    string LocalName,
    string FullType,
    bool IsNullable)
{
    public string NonNullLocalName => IsNullable ? LocalName + "Value" : LocalName;
    public bool IsValidationResult => FullType.Equals("global::MiniBus.ValidationResult");
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
                var rawName = elem.Name;
                var localName = rawName.Length > 0 && char.IsUpper(rawName[0])
                    ? char.ToLower(rawName[0]) + rawName.Substring(1)
                    : rawName;
                elements.Add(new(string.Concat(localName, MethodName), fullType, isNullable));
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
            Returns = new([
                new(string.Concat("from", MethodName), fullType, isNullable)
            ]);
        }
    }

    private MethodPhase()
    {
        // Dummy constructor for record initialization - not used directly
        Type = PhaseType.Before;
        MethodName = "";
        IsAsync = false;
        IsStatic = false;
        Parameters = EquatableArray<InputParameter>.Empty;
        Returns = EquatableArray<ReturnElement>.Empty;
    }

    public PhaseType Type { get; }
    public string MethodName { get; }
    public bool IsAsync { get; }
    public bool IsStatic { get; }
    public EquatableArray<InputParameter> Parameters { get; init; }
    public EquatableArray<ReturnElement> Returns { get; }

    private static (ITypeSymbol Inner, bool IsAsync) UnwrapTask(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 1 } task)
            return (task.TypeArguments[0], true);
        if (returnType is INamedTypeSymbol { Name: "Task", TypeArguments.Length: 0 })
            return (returnType, true);
        return (returnType, false);
    }
}