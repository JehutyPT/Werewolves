using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class RoleSelectionPageBunitTests
{
	[Fact]
	public void ProductionCatalog_RendersPortugueseAngelAsOrdinarySingleRoleToggle()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var angelLabel = MainRoleType.Angel.GetPublicName();
		var lonerGroupLabel = RoleGroup.Loners.GetDisplayName();

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();

		var lonerGroup = cut.FindAll("section.ww-role-group")
			.Single(group => group.GetAttribute("aria-label") == lonerGroupLabel);
		var angelToggle = lonerGroup.QuerySelectorAll("button")
			.Single(button => button.GetAttribute("aria-label") == angelLabel);
		angelToggle.Closest(".ww-role-row")!
			.QuerySelector(".ww-role-label")!
			.TextContent.Should().Be(angelLabel);
		angelToggle.GetAttribute("aria-pressed").Should().Be("false");

		angelToggle.Click();

		cut.WaitForAssertion(() =>
		{
			lobby.GetRoleCount(MainRoleType.Angel).Should().Be(1);
			FindAngelToggle(cut, angelLabel).GetAttribute("aria-pressed").Should().Be("true");
			cut.FindAll(TestId(ModeratorUiTestIds.InstructionBlock)).Should().BeEmpty();
			cut.FindAll(TestId(ModeratorUiTestIds.DashboardActionZone)).Should().BeEmpty();
		});
	}

	private static AngleSharp.Dom.IElement FindAngelToggle(
		IRenderedComponent<RoleSelectionPage> cut,
		string angelLabel) =>
		cut.FindAll("button")
			.Single(button => button.GetAttribute("aria-label") == angelLabel);

	private static string TestId(string value) => $"[data-testid='{value}']";
}
