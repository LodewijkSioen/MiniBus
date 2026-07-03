namespace Caravelle;

/// <summary>
/// Marks a class as reusable middleware whose pre-handle, post-handle, and finally
/// methods are merged into the pipeline of handlers matching <typeparamref name="TFilter"/>.
/// Multiple <see cref="MiddlewareAttribute{TFilter}"/> instances on the same class are
/// independent applicability rules (a handler matching any one of them is enough) —
/// use <see cref="AllHandlers"/> to apply to every handler in the compilation.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MiddlewareAttribute<TFilter> : Attribute
    where TFilter : IMiddlewareFilter;

/// <summary>
/// Marker interface for types that describe which handlers a <see cref="MiddlewareAttribute{TFilter}"/>
/// applies to. Implementations are compile-time-only markers inspected by the
/// Caravelle source generator — they are never instantiated and carry no behavior.
/// Only the filter shapes shipped in this file are recognized by the generator;
/// custom implementations are ignored with a diagnostic.
/// </summary>
public interface IMiddlewareFilter;

/// <summary>Matches every handler in the compilation.</summary>
public sealed class AllHandlers : IMiddlewareFilter;

/// <summary>Matches handlers whose class implements <typeparamref name="T"/>.</summary>
public sealed class ForInterface<T> : IMiddlewareFilter;

/// <summary>
/// Matches handlers whose result can be <typeparamref name="T"/> (or a type assignable
/// to it), including the success response type and any validation/not-found payload types.
/// </summary>
public sealed class ForReturnType<T> : IMiddlewareFilter;

/// <summary>
/// Matches handlers whose inferred request type is <typeparamref name="T"/>, or a type
/// assignable to it.
/// </summary>
public sealed class ForRequestType<T> : IMiddlewareFilter;

/// <summary>
/// Matches handlers whose pipeline has a local variable of type <typeparamref name="T"/>,
/// or a type assignable to it, flowing between pre-handle/handle/post-handle methods.
/// </summary>
public sealed class ForVariable<T> : IMiddlewareFilter;

/// <summary>Matches handlers declared in the same namespace as <typeparamref name="T"/>.</summary>
public sealed class ForNamespaceOf<T> : IMiddlewareFilter;

/// <summary>Matches handlers declared in the same assembly as <typeparamref name="T"/>.</summary>
public sealed class ForAssemblyOf<T> : IMiddlewareFilter;

/// <summary>Matches handlers decorated with <typeparamref name="TAttribute"/>.</summary>
public sealed class ForAttribute<TAttribute> : IMiddlewareFilter
    where TAttribute : Attribute;

/// <summary>Matches only the specific handler <typeparamref name="THandler"/>.</summary>
public sealed class ForHandler<THandler> : IMiddlewareFilter;

/// <summary>Matches handlers whose pipeline can produce an <see cref="IValidationResult"/>.</summary>
public sealed class HasValidation : IMiddlewareFilter;

/// <summary>Matches handlers whose pipeline can short-circuit with a <see cref="NotFoundResult"/>.</summary>
public sealed class HasNotFound : IMiddlewareFilter;
