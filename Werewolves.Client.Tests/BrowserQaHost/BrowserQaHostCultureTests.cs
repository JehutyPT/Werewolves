using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.Resources;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Client.Tests.BrowserQaHost;

public sealed class BrowserQaHostCultureTests
{
	[Fact]
	public void Use_EnglishCulture_ResolvesClientAndGameCopyFromEnglishResources()
	{
		var original = CultureState.Capture();

		try
		{
			var clientValues = LoadResourceValues(ClientTestReferences.Paths.RepositoryPath(
				"Werewolves.Client.Shared", "Resources", "ClientStrings.resx"));
			var gameValues = LoadResourceValues(ClientTestReferences.Paths.RepositoryPath(
				"Werewolves.Core", "Werewolves.Core.StateModels", "Resources", "GameStrings.en-US.resx"));

			BrowserQaHostCulture.Use("en-US");

			CultureInfo.DefaultThreadCurrentCulture.Should().Be(BrowserQaHostCulture.EnglishCulture);
			CultureInfo.DefaultThreadCurrentUICulture.Should().Be(BrowserQaHostCulture.EnglishCulture);
				ClientStrings.LobbyRoster_Title.Should().Be(clientValues[nameof(ClientStrings.LobbyRoster_Title)]);
				GameStrings.NightStartsPrompt.Should().Be(gameValues[nameof(GameStrings.NightStartsPrompt)]);
				GameStrings.VictoryConditionWhiteWerewolfSoleSurvivor.Should().Be(
					gameValues[nameof(GameStrings.VictoryConditionWhiteWerewolfSoleSurvivor)]);
				MainRoleType.Seer.GetPublicName().Should().Be(gameValues["SeerRoleName"]);
			RoleGroup.Villagers.GetDisplayName().Should().Be(gameValues["VillagersGroupName"]);
		}
		finally
		{
			original.Restore();
		}
	}

	[Fact]
	public void Use_WithoutCultureName_KeepsPortugueseAsTheQaDefault()
	{
		var original = CultureState.Capture();

		try
		{
			BrowserQaHostCulture.Use(null);

			CultureInfo.DefaultThreadCurrentCulture.Should().Be(BrowserQaHostCulture.PortugueseCulture);
			CultureInfo.DefaultThreadCurrentUICulture.Should().Be(BrowserQaHostCulture.PortugueseCulture);
			ClientStrings.Culture.Should().Be(BrowserQaHostCulture.PortugueseCulture);
			GameStrings.Culture.Should().Be(BrowserQaHostCulture.PortugueseCulture);
		}
		finally
		{
			original.Restore();
		}
	}

	private static IReadOnlyDictionary<string, string> LoadResourceValues(string path) =>
		XDocument.Load(path)
			.Root!
			.Elements("data")
			.ToDictionary(
				data => (string)data.Attribute("name")!,
				data => data.Element("value")!.Value,
				StringComparer.Ordinal);

	private sealed record CultureState(
		CultureInfo? DefaultCulture,
		CultureInfo? DefaultUiCulture,
		CultureInfo? ClientResourceCulture,
		CultureInfo? GameResourceCulture)
	{
		public static CultureState Capture() => new(
			CultureInfo.DefaultThreadCurrentCulture,
			CultureInfo.DefaultThreadCurrentUICulture,
			ClientStrings.Culture,
			GameStrings.Culture);

		public void Restore()
		{
			CultureInfo.DefaultThreadCurrentCulture = DefaultCulture;
			CultureInfo.DefaultThreadCurrentUICulture = DefaultUiCulture;
			ClientStrings.Culture = ClientResourceCulture;
			GameStrings.Culture = GameResourceCulture;
		}
	}
}
