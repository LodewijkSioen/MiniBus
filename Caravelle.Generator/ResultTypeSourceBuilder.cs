using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caravelle.Generator;

internal static class ResultTypeSourceBuilder
{
    public static void Build(StringBuilder sb, HandlerModel model, string indent)
    {
        sb.AppendLine($"{indent}    public sealed class Result : global::Caravelle.IDispatchResult");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        private Result() {{ }}");
        sb.AppendLine();
        foreach (var resultValueType in model.ResultValueTypes)
        {
            sb.AppendLine($"{indent}        public Result({resultValueType.FullType} value)");
            sb.AppendLine($"{indent}        {{");
            if (resultValueType.RequiresNullCheck)
            {
                sb.AppendLine($"{indent}            if (value is null)");
                sb.AppendLine($"{indent}            {{");
                sb.AppendLine($"{indent}                throw new global::System.ArgumentNullException(nameof(value));");
                sb.AppendLine($"{indent}            }}");
                sb.AppendLine();
            }

            sb.AppendLine($"{indent}            Value = value;");
            sb.AppendLine($"{indent}        }}");
            sb.AppendLine();
        }

        sb.AppendLine($"{indent}        public object? Value {{ get; }}");

        var matchBranches = BuildMatchBranches(model);
        sb.AppendLine();
        sb.AppendLine($"{indent}        public T Match<T>(");
        for (var index = 0; index < matchBranches.Count; index++)
        {
            var branch = matchBranches[index];
            var separator = index < matchBranches.Count - 1 ? "," : "";
            sb.AppendLine($"{indent}            global::System.Func<{branch.TypeName}, T> {branch.ParameterName}{separator}");
        }

        sb.AppendLine($"{indent}        )");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            return Value switch");
        sb.AppendLine($"{indent}            {{");
        foreach (var branch in matchBranches)
        {
            sb.AppendLine($"{indent}                {branch.TypeName} value => {branch.ParameterName}(value),");
        }

        sb.AppendLine($"{indent}                _ => throw new global::System.InvalidOperationException(\"Unknown result value type.\")");
        sb.AppendLine($"{indent}            }};");
        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}    }}");
    }

    private static List<MatchBranch> BuildMatchBranches(HandlerModel model)
    {
        var validationBranchTypes = model.ResultValueTypes
            .Where(typeName => !typeName.FullType.Equals(model.FullResponseType, StringComparison.Ordinal)
                && !IsNotFoundType(typeName.FullType))
            .ToArray();
        var hasSingleValidationBranch = validationBranchTypes.Length == 1;

        var branchNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var branches = new List<MatchBranch>();
        foreach (var typeName in model.ResultValueTypes)
        {
            var baseName = BuildMatchParameterBaseName(typeName.FullType, model.FullResponseType, hasSingleValidationBranch);
            var parameterName = MakeUniqueName(baseName, branchNames);
            branches.Add(new MatchBranch(typeName.FullType, parameterName));
        }

        return branches;
    }

    private static string BuildMatchParameterBaseName(string typeName, string responseType, bool hasSingleValidationBranch)
    {
        if (typeName.Equals(responseType, StringComparison.Ordinal))
        {
            return "onSuccess";
        }

        if (IsNotFoundType(typeName))
        {
            return "onNotFound";
        }

        if (hasSingleValidationBranch)
        {
            return "onInvalid";
        }

        return "on" + BuildTypeSuffix(typeName);
    }

    private static bool IsNotFoundType(string typeName)
    {
        return typeName.Equals("global::Caravelle.NotFoundResult", StringComparison.Ordinal);
    }

    private static string BuildTypeSuffix(string typeName)
    {
        var source = typeName.Replace("global::", string.Empty);
        var index = 0;
        var tokens = new List<string>();
        ParseTypeTokens(source, ref index, tokens);

        var suffix = string.Concat(tokens.Select(ToPascalIdentifier));
        if (suffix.Length == 0)
        {
            suffix = "Type";
        }

        if (char.IsDigit(suffix[0]))
        {
            suffix = "Type" + suffix;
        }

        return suffix;
    }

    private static void ParseTypeTokens(string source, ref int index, List<string> tokens)
    {
        SkipWhitespace(source, ref index);

        var pathBuilder = new StringBuilder();
        while (index < source.Length)
        {
            var c = source[index];
            if (c is '<' or ',' or '>' or '?' or '[' or ']')
            {
                break;
            }

            pathBuilder.Append(c);
            index++;
        }

        var path = pathBuilder.ToString().Trim();
        if (path.Length > 0)
        {
            var pathSegments = path.Split('.');
            var tail = pathSegments[pathSegments.Length - 1];
            if (tail.Length > 0)
            {
                tokens.Add(tail);
            }
        }

        if (index < source.Length && source[index] == '<')
        {
            index++;
            while (index < source.Length)
            {
                ParseTypeTokens(source, ref index, tokens);
                SkipWhitespace(source, ref index);

                if (index < source.Length && source[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < source.Length && source[index] == '>')
                {
                    index++;
                    break;
                }

                break;
            }
        }

        while (index < source.Length && source[index] is '?' or '[' or ']')
        {
            index++;
        }
    }

    private static void SkipWhitespace(string source, ref int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }
    }

    private static string ToPascalIdentifier(string value)
    {
        var sb = new StringBuilder();
        var capitalizeNext = true;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
                continue;
            }

            capitalizeNext = true;
        }

        return sb.ToString();
    }

    private static string MakeUniqueName(string baseName, Dictionary<string, int> names)
    {
        if (!names.TryGetValue(baseName, out var count))
        {
            names[baseName] = 1;
            return baseName;
        }

        count++;
        names[baseName] = count;
        return baseName + count;
    }

    private sealed record MatchBranch(string TypeName, string ParameterName);
}
