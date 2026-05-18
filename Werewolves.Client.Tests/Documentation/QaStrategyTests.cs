using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Documentation;

public class QaStrategyTests
{
	[Fact]
	public void QaStrategy_DefinesClaimFirstEvidenceContractAndSourceTestAudit()
	{
		var strategy = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "qa-strategy.md"));

		strategy.Should().Contain("claim-first");
		strategy.Should().Contain("cheapest reliable evidence");
		strategy.Should().Contain("QA Evidence Matrix");
		strategy.Should().Contain("Manual Device Boundary");
		strategy.Should().Contain("Local Browser QA Host");
		strategy.Should().Contain("CI and Local Split");
		strategy.Should().Contain("Source-Test Allowlist");
		strategy.Should().Contain("Permanent policy");
		strategy.Should().Contain("Deprecated temporary scaffold");
		strategy.Should().NotContain("TBD");
		strategy.Should().NotContain("placeholder");
	}

	private static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);

			while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Werewolves.sln")))
			{
				directory = directory.Parent;
			}

			return directory?.FullName
				?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
		}
	}
}
