#pragma warning disable IDE0130
// Records and 'init' setters rely on System.Runtime.CompilerServices.IsExternalInit,
// which is not present in netstandard2.0.  Declaring it here satisfies the compiler.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }