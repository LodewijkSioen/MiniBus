using System.Runtime.CompilerServices;

namespace Caravelle.Generator.Tests;

public static class VerifyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        //VerifierSettings.AutoVerify();
        VerifySourceGenerators.Initialize();
    }
}
