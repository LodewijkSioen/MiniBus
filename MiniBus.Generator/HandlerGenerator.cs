using Microsoft.CodeAnalysis;

namespace MiniBus.Generator;

[Generator]
public class HandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Phase 1: skeleton — no generation yet
    }
}
