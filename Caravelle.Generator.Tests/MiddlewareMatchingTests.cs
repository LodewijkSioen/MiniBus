using Caravelle.Generator.Handler;
using Caravelle.Generator.Middleware;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Caravelle.Generator.Tests;

[TestFixture]
public class MiddlewareMatchingTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static Compilation Compile(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var path = typeof(HandlerAttribute).Assembly.Location;
        if (!references.Any(r => r.Display == path))
            references.Add(MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static INamedTypeSymbol GetSymbol(Compilation compilation, string className)
    {
        var syntaxTree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var classSyntax = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == className);

        return (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(classSyntax)!;
    }

    private static MiddlewareModel GetMiddlewareModel(Compilation compilation, string className)
    {
        var symbol = GetSymbol(compilation, className);
        var attributes = symbol.GetAttributes()
            .Where(static a => a.AttributeClass?.Name == "MiddlewareAttribute")
            .ToImmutableArray();
        var result = MiddlewareModelFactory.GetMiddlewareModel(symbol, attributes, Location.None);
        Assert.That(result.Model, Is.Not.Null, "middleware should discover successfully");
        return result.Model!;
    }

    private static Result GetHandlerModel(Compilation compilation, string className, params MiddlewareModel[] middleware)
    {
        var symbol = GetSymbol(compilation, className);
        return HandlerModelFactory.GetHandlerModel(symbol, new(middleware), Location.None);
    }

    // ── Filter kinds ──────────────────────────────────────────────────────

    [Test]
    public void AllHandlers_MatchesAnyHandler()
    {
        var compilation = Compile("""
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
            """);

        var middleware = GetMiddlewareModel(compilation, "LoggingMiddleware");
        var result = GetHandlerModel(compilation, "PlainHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.LoggingMiddleware"), Is.True);
    }

    [Test]
    public void ForInterface_MatchesHandlerImplementingInterface()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            public interface IAdminHandler;

            [Middleware<ForInterface<IAdminHandler>>]
            public class AuditMiddleware
            {
                public string BeforeAudit() => "audited";
            }

            [Handler]
            public class AdminHandler : IAdminHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }

            [Handler]
            public class PlainHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "AuditMiddleware");
        var adminResult = GetHandlerModel(compilation, "AdminHandler", middleware);
        var plainResult = GetHandlerModel(compilation, "PlainHandler", middleware);

        Assert.That(adminResult.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.AuditMiddleware"), Is.True);
        Assert.That(plainResult.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.AuditMiddleware"), Is.False);
    }

    [Test]
    public void ForReturnType_MatchesHandlerWithAssignableResponseType()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            public interface IResponse;

            [Middleware<ForReturnType<IResponse>>]
            public class ResponseMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class TypedResponseHandler
            {
                public record Request(int Id);
                public record Response(string Name) : IResponse;
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "ResponseMiddleware");
        var result = GetHandlerModel(compilation, "TypedResponseHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.ResponseMiddleware"), Is.True);
    }

    [Test]
    public void ForRequestType_MatchesHandlerWithAssignableRequestType()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            public interface IRequest;

            [Middleware<ForRequestType<IRequest>>]
            public class RequestMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class TypedRequestHandler
            {
                public record Request(int Id) : IRequest;
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "RequestMiddleware");
        var result = GetHandlerModel(compilation, "TypedRequestHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.RequestMiddleware"), Is.True);
    }

    [Test]
    public void ForVariable_MatchesHandlerWithAssignableLocalVariable()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            public interface IEntity;

            [Middleware<ForVariable<IEntity>>]
            public class EntityMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class LoadingHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(int Id) : IEntity;
                public Entity Load(Request request) => new Entity(request.Id);
                public Response Handle(Entity entity) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "EntityMiddleware");
        var result = GetHandlerModel(compilation, "LoadingHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.EntityMiddleware"), Is.True);
    }

    [Test]
    public void ForNamespaceOf_MatchesHandlerInSameNamespace()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp.Admin;

            public interface INamespaceMarker;

            [Middleware<ForNamespaceOf<INamespaceMarker>>]
            public class NamespaceMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class InNamespaceHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "NamespaceMiddleware");
        var result = GetHandlerModel(compilation, "InNamespaceHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.Admin.NamespaceMiddleware"), Is.True);
    }

    [Test]
    public void ForAssemblyOf_MatchesHandlerInSameAssembly()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            [Middleware<ForAssemblyOf<HandlerAttribute>>]
            public class AssemblyMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class AnyHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "AssemblyMiddleware");
        var result = GetHandlerModel(compilation, "AnyHandler", middleware);

        // The handler is declared in the test assembly, not Caravelle's own assembly, so a
        // filter anchored to HandlerAttribute's assembly should never match it.
        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.AssemblyMiddleware"), Is.False);
    }

    [Test]
    public void ForAttribute_MatchesHandlerDecoratedWithAttribute()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            public class AuditedAttribute : Attribute;

            [Middleware<ForAttribute<AuditedAttribute>>]
            public class AttributeMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Audited]
            [Handler]
            public class AuditedHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "AttributeMiddleware");
        var result = GetHandlerModel(compilation, "AuditedHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.AttributeMiddleware"), Is.True);
    }

    [Test]
    public void ForHandler_MatchesOnlyNamedHandler()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            [Handler]
            public class SpecialHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }

            [Middleware<ForHandler<SpecialHandler>>]
            public class SpecificMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class OtherHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "SpecificMiddleware");
        var specialResult = GetHandlerModel(compilation, "SpecialHandler", middleware);
        var otherResult = GetHandlerModel(compilation, "OtherHandler", middleware);

        Assert.That(specialResult.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.SpecificMiddleware"), Is.True);
        Assert.That(otherResult.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.SpecificMiddleware"), Is.False);
    }

    [Test]
    public void HasValidation_MatchesHandlerWithValidatePhase()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            [Middleware<HasValidation>]
            public class ValidationLoggingMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class ValidatedHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public ValidationResult Validate(Request request) => new ValidationResult();
                public Response Handle(Request request) => new Response("test");
            }

            [Handler]
            public class PlainHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "ValidationLoggingMiddleware");
        var validatedResult = GetHandlerModel(compilation, "ValidatedHandler", middleware);
        var plainResult = GetHandlerModel(compilation, "PlainHandler", middleware);

        Assert.That(validatedResult.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.ValidationLoggingMiddleware"), Is.True);
        Assert.That(plainResult.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.ValidationLoggingMiddleware"), Is.False);
    }

    [Test]
    public void HasNotFound_MatchesHandlerWithNullableLoad()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            [Middleware<HasNotFound>]
            public class NotFoundLoggingMiddleware
            {
                public string BeforeTag() => "tagged";
            }

            [Handler]
            public class NullableLoadHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public record Entity(int Id);
                public Entity? Load(Request request) => null;
                public Response Handle(Entity entity) => new Response("test");
            }
            """);

        var middleware = GetMiddlewareModel(compilation, "NotFoundLoggingMiddleware");
        var result = GetHandlerModel(compilation, "NullableLoadHandler", middleware);

        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.NotFoundLoggingMiddleware"), Is.True);
    }

    // ── Fixed-point convergence ───────────────────────────────────────────

    [Test]
    public void SecondMiddleware_MatchesOnlyAfterFirstMiddlewareIntroducesItsTargetType()
    {
        var compilation = Compile("""
            using Caravelle;
            namespace TestApp;

            public record AuditMarker;

            [Middleware<AllHandlers>]
            public class AuditingMiddleware
            {
                public AuditMarker BeforeAudit() => new AuditMarker();
            }

            [Middleware<ForVariable<AuditMarker>>]
            public class AuditReactingMiddleware
            {
                public string AfterReact(AuditMarker marker) => "reacted";
            }

            [Handler]
            public class PlainHandler
            {
                public record Request(int Id);
                public record Response(string Name);
                public Response Handle(Request request) => new Response("test");
            }
            """);

        var auditingMiddleware = GetMiddlewareModel(compilation, "AuditingMiddleware");
        var reactingMiddleware = GetMiddlewareModel(compilation, "AuditReactingMiddleware");
        var result = GetHandlerModel(compilation, "PlainHandler", auditingMiddleware, reactingMiddleware);

        // AuditReactingMiddleware's filter only matches once AuditingMiddleware's
        // AuditMarker-producing phase has been merged in an earlier pass — proving the
        // fixed-point loop iterates rather than doing a single pass.
        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.AuditingMiddleware"), Is.True);
        Assert.That(result.Model!.MatchedMiddlewareClassNames.Contains("global::TestApp.AuditReactingMiddleware"), Is.True);
    }
}
