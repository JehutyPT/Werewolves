using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Documentation;

public class QaStrategyTests
{
	[Fact]
	public void QaStrategy_DefinesClaimFirstEvidenceGuideAndSourceTestAllowlist()
	{
		var strategy = File.ReadAllText(QaStrategyPath);

		foreach (var requiredContent in QaStrategyContract.RequiredStrategyContent)
		{
			strategy.Should().Contain(requiredContent);
		}

		foreach (var forbiddenContent in QaStrategyContract.ForbiddenStrategyContent)
		{
			strategy.Should().NotContain(forbiddenContent);
		}
	}

	[Fact]
	public void NativeDeviceChecklist_DefinesManualOnlyClaimAndEvidenceChecks()
	{
		var checklistPath = Path.Combine(RepositoryRoot, "Werewolves.Client", "docs", "native-device-qa-checklist.md");
		var strategy = File.ReadAllText(QaStrategyPath);

		File.Exists(checklistPath).Should().BeTrue(ClientTestReferences.AssertionReasons.NativeChecklistExists);

		var checklist = File.ReadAllText(checklistPath);

		foreach (var requiredContent in QaStrategyContract.RequiredNativeChecklistContent)
		{
			checklist.Should().Contain(requiredContent);
		}

		strategy.Should().Contain(QaStrategyContract.NativeChecklistRelativePath);
	}

	[Fact]
	public void QaStrategy_SourceTestAllowlistTracksActiveRetainedSourceTests()
	{
		var strategy = File.ReadAllText(QaStrategyPath);
		var allowlist = SourceTestAllowlistSection(strategy);

		foreach (var requiredEntry in QaStrategyContract.RequiredSourceTestAllowlistEntries)
		{
			allowlist.Should().Contain(requiredEntry);
		}

		foreach (var retiredEntry in QaStrategyContract.RetiredSourceTestAllowlistEntries)
		{
			allowlist.Should().NotContain(retiredEntry);
		}
	}

	private static string RepositoryRoot => ClientTestReferences.Paths.RepositoryRoot;

	private static string QaStrategyPath => Path.Combine(RepositoryRoot, "docs", "agents", "qa-strategy.md");

	private static string SourceTestAllowlistSection(string strategy)
	{
		const string header = "## Source-Test Allowlist";
		var start = strategy.IndexOf(header, StringComparison.Ordinal);

		start.Should().BeGreaterThanOrEqualTo(0, "the QA strategy must define the source-test allowlist section");

		return strategy[start..];
	}
}
