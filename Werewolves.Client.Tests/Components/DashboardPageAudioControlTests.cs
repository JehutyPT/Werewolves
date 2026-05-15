using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class DashboardPageAudioControlTests
{
	[Fact]
	public void ActionTabMarkup_BindsAudioToggleResourceKeys()
	{
		var markup = File.ReadAllText(GetDashboardPagePath());

		markup.Should().Contain("ClientStrings.Dashboard_AudioMute");
		markup.Should().Contain("ClientStrings.Dashboard_AudioUnmute");
		markup.Should().Contain("@onclick=\"ToggleAudioMute\"");
	}

	private static string GetDashboardPagePath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(
				directory.FullName,
				"Werewolves.Client",
				"Components",
				"Pages",
				"DashboardPage.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("DashboardPage.razor could not be found from the test output directory.");
	}
}
