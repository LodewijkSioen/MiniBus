using System.Runtime.CompilerServices;

namespace MiniBus.Generator.Tests;

public static class VerifyInit
{
    [ModuleInitializer]
    public static void Init() =>
        VerifySourceGenerators.Initialize();
}
