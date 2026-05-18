using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Documentation;

public class QaStrategyTests
{
	[Fact]
	public void QaStrategy_DefinesClaimFirstEvidenceGuideAndSourceTestAllowlist()
	{
		var strategy = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "qa-strategy.md"));

		strategy.Should().Contain("claim-first");
		strategy.Should().Contain("cheapest reliable evidence");
		strategy.Should().Contain("QA Evidence Matrix");
		strategy.Should().Contain("Choose Evidence");
		strategy.Should().Contain("Source-Test Rules");
		strategy.Should().Contain("Allowed Source Tests");
		strategy.Should().Contain("Manual Device Checks");
		strategy.Should().Contain("manual device boundary");
		strategy.Should().Contain("Browser QA Host");
		strategy.Should().Contain("CI vs Local Evidence");
		strategy.Should().Contain("Source-Test Allowlist");
		strategy.Should().Contain("Permanent policy");
		strategy.Should().Contain("Deprecated temporary scaffold");
		strategy.Should().NotContain("TBD");
		strategy.Should().NotContain("placeholder");
		strategy.Should().NotContain("PRD #");
		strategy.Should().NotContain("this slice");
		strategy.Should().NotContain("migration");
		strategy.Should().NotContain("Existing Source-Test Audit");
	}

	[Fact]
	public void NativeDeviceChecklist_DefinesManualOnlyClaimAndEvidenceChecks()
	{
		var checklistPath = Path.Combine(RepositoryRoot, "Werewolves.Client", "docs", "native-device-qa-checklist.md");
		var strategy = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "qa-strategy.md"));

		File.Exists(checklistPath).Should().BeTrue("native behavior needs a repeatable client release checklist");

		var checklist = File.ReadAllText(checklistPath);

		checklist.Should().Contain("Claim");
		checklist.Should().Contain("Manual scenario");
		checklist.Should().Contain("Expected evidence");
		checklist.Should().Contain("real audio output");
		checklist.Should().Contain("haptic feel");
		checklist.Should().Contain("wake lock behavior");
		checklist.Should().Contain("resume/background behavior");
		checklist.Should().Contain("platform storage behavior");
		checklist.Should().Contain("packaging/install behavior");
		checklist.Should().Contain("Native WebView rendering quirks");
		checklist.Should().Contain("touch feel");
		checklist.Should().Contain("These checks remain manual");
		checklist.Should().Contain("one or two tiny device smoke tests");
		checklist.Should().Contain("Do not use this checklist to replay Core game rules");
		checklist.Should().Contain("Do not use this checklist to repeat Browser QA Host checks");

		strategy.Should().Contain("Werewolves.Client/docs/native-device-qa-checklist.md");
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
