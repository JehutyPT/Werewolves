using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Base class for tests that automatically dumps diagnostic state changes on failure.
/// </summary>
public abstract class DiagnosticTestBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected GameTestBuilder? Builder;
    private bool _testCompleted;

    protected DiagnosticTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    /// <summary>
    /// Creates a new GameTestBuilder with diagnostic output enabled.
    /// </summary>
    protected GameTestBuilder CreateBuilder()
    {
        Builder = GameTestBuilder.Create(Output);
        return Builder;
    }

    /// <summary>
    /// Call at the end of a successful test to suppress diagnostic dump.
    /// </summary>
    protected void MarkTestCompleted() => _testCompleted = true;

    public void Dispose()
    {
        if (!_testCompleted && Builder != null)
        {
            Output.WriteLine("\n⚠️ TEST DID NOT COMPLETE - DUMPING DIAGNOSTICS:");
            Builder.DumpDiagnostics();
        }
        GC.SuppressFinalize(this);
    }
}
