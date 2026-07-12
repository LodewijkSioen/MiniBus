using System.Text.RegularExpressions;

namespace Caravelle.Generator.Tests;

[TestFixture]
public class MiddlewareDispatcherCodegenTests
{
    [Test]
    public void AllHandlersMiddleware_GetsDedicatedFieldAndCtorParam_AndIsCalledInsteadOfHandler()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            [Middleware<AllHandlers>]
            public class LoggingMiddleware
            {
                public string BeforeLog() => "logged";
            }

            [Handler]
            public class PlainHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class PlainHandlerDispatcher", StringComparison.Ordinal));

        var fieldMatch = Regex.Match(dispatcher, @"private readonly global::TestApp\.LoggingMiddleware (_loggingMiddleware_[0-9a-f]{8});");
        Assert.That(fieldMatch.Success, Is.True, "expected a dedicated middleware field declaration");
        var fieldName = fieldMatch.Groups[1].Value;

        Assert.That(dispatcher.Contains($"global::TestApp.LoggingMiddleware {fieldName.TrimStart('_')}", StringComparison.Ordinal), Is.True, "expected a constructor parameter for the middleware");
        Assert.That(dispatcher.Contains($"{fieldName} = {fieldName.TrimStart('_')};", StringComparison.Ordinal), Is.True, "expected the field to be assigned in the constructor");
        Assert.That(dispatcher.Contains($"{fieldName}.BeforeLog()", StringComparison.Ordinal), Is.True, "expected the middleware method to be called on its own field, not _handler");
        Assert.That(dispatcher.Contains("_handler.BeforeLog(", StringComparison.Ordinal), Is.False, "middleware method must never be called on _handler");
    }

    [Test]
    public void StaticMiddlewareMethod_IsCalledOnTypeName_WithNoDedicatedField()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            [Middleware<AllHandlers>]
            public class StaticLoggingMiddleware
            {
                public static string BeforeLog() => "logged";
            }

            [Handler]
            public class StaticMiddlewareHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class StaticMiddlewareHandlerDispatcher", StringComparison.Ordinal));

        Assert.That(dispatcher.Contains("global::TestApp.StaticLoggingMiddleware.BeforeLog()", StringComparison.Ordinal), Is.True);
        Assert.That(Regex.IsMatch(dispatcher, @"_\w+_[0-9a-f]{8}"), Is.False, "a static middleware method should never need a DI-resolved field");
    }

    [Test]
    public void MiddlewareBeforePhase_RunsBeforeInheritedBaseClassPhase()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            public abstract class AuditBase
            {
                public InheritedMarker BeforeInherited() => new InheritedMarker();
            }

            public record InheritedMarker;

            [Middleware<AllHandlers>]
            public class OuterMiddleware
            {
                public OuterMarker BeforeOuter() => new OuterMarker();
            }

            public record OuterMarker;

            [Handler]
            public class OnionOrderHandler : AuditBase
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class OnionOrderHandlerDispatcher", StringComparison.Ordinal));

        var outerCallIndex = dispatcher.IndexOf(".BeforeOuter()", StringComparison.Ordinal);
        var inheritedCallIndex = dispatcher.IndexOf(".BeforeInherited()", StringComparison.Ordinal);

        Assert.That(outerCallIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(inheritedCallIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(outerCallIndex, Is.LessThan(inheritedCallIndex), "middleware is the outermost onion layer and should run before inherited base-class phases");
    }

    [Test]
    public void MultipleDistinctMiddlewareTypes_EachGetOwnDedicatedField()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            [Middleware<AllHandlers>]
            public class FirstMiddleware
            {
                public FirstMarker BeforeFirst() => new FirstMarker();
            }

            public record FirstMarker;

            [Middleware<AllHandlers>]
            public class SecondMiddleware
            {
                public SecondMarker BeforeSecond() => new SecondMarker();
            }

            public record SecondMarker;

            [Handler]
            public class MultiMiddlewareHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class MultiMiddlewareHandlerDispatcher", StringComparison.Ordinal));

        var fieldMatches = Regex.Matches(dispatcher, @"private readonly global::TestApp\.(First|Second)Middleware (_(?:first|second)Middleware_[0-9a-f]{8});");
        Assert.That(fieldMatches.Count, Is.EqualTo(2), "each distinct middleware type should get its own dedicated field");

        var fieldNames = fieldMatches.Select(m => m.Groups[2].Value).ToArray();
        Assert.That(fieldNames.Distinct().Count(), Is.EqualTo(2), "field names must be distinct per middleware type");
    }

    [Test]
    public void MiddlewareFinallyPhase_IsCalledOnMiddlewareField()
    {
        const string source = """
            using Caravelle;
            namespace TestApp;

            [Middleware<AllHandlers>]
            public class FinallyMiddleware
            {
                public void Finally() { }
            }

            [Handler]
            public class FinallyMiddlewareHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """;

        var result = GeneratorTestHelper.Run(source);
        var dispatcher = result.GeneratedSources.Single(s => s.Contains("class FinallyMiddlewareHandlerDispatcher", StringComparison.Ordinal));

        var fieldMatch = Regex.Match(dispatcher, @"private readonly global::TestApp\.FinallyMiddleware (_finallyMiddleware_[0-9a-f]{8});");
        Assert.That(fieldMatch.Success, Is.True);
        var fieldName = fieldMatch.Groups[1].Value;

        Assert.That(dispatcher.Contains($"{fieldName}.Finally()", StringComparison.Ordinal), Is.True);
    }
}
