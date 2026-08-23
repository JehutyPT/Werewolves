using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class LobbyRosterPageTests
{
	[Fact]
	public void AddControl_AddsPlayerWithIconOnlyLabelAndKeepsLocalizedRosterActions()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyRosterPage>();
		var addButton = cut.Find(".ww-add-row button[type='submit']");

		addButton.TextContent.Trim().Should().Be("+");
		addButton.GetAttribute("aria-label").Should().Be(ClientStrings.LobbyRoster_AddButton);
		addButton.GetAttribute("title").Should().Be(ClientStrings.LobbyRoster_AddButton);

		const string playerName = "Alexandra da Silva com um nome muito comprido";
		cut.Find("#player-name").Input(playerName);
		addButton.Click();

		cut.Find(".ww-player-name").TextContent.Should().Be(playerName);
		cut.Find($"button[aria-label='{Format(ClientStrings.LobbyRoster_MoveUpAriaFormat, playerName)}']")
			.HasAttribute("disabled").Should().BeTrue();
		cut.Find($"button[aria-label='{Format(ClientStrings.LobbyRoster_MoveDownAriaFormat, playerName)}']")
			.HasAttribute("disabled").Should().BeTrue();
		cut.Find($"button[aria-label='{Format(ClientStrings.LobbyRoster_RemoveAriaFormat, playerName)}']")
			.Should().NotBeNull();
	}

	[Fact]
	public void Submit_ClearsAndRefocusesAfterSuccessButRetainsInvalidDraft()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<LobbyRosterPage>();
		var form = cut.Find(".ww-roster-form");
		var input = cut.Find("#player-name");

		input.Input("Alice");
		form.Submit();

		cut.Find("#player-name").GetAttribute("value").Should().BeEmpty();
		context.JSInterop.VerifyFocusAsyncInvoke();

		cut.Find("#player-name").Input("Alice");
		form.Submit();

		cut.Find("#player-name").GetAttribute("value").Should().Be("Alice");
		cut.Find("[role='alert']").TextContent.Should().Be(ClientStrings.Validation_DuplicatePlayerName);
		context.JSInterop.VerifyFocusAsyncInvoke(calledTimes: 1);
	}

	[Fact]
	public void PlayerLimit_DisablesAddAndDescribesWhy()
	{
		using var context = new ModeratorComponentTestContext();
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		for (var number = 1; number <= 30; number++)
		{
			lobby.AddPlayer($"Player {number}").Should().Be(AddPlayerResult.Success);
		}
		var cut = context.RenderModeratorComponent<LobbyRosterPage>();

		var addButton = cut.Find(".ww-add-row button[type='submit']");
		addButton.HasAttribute("disabled").Should().BeTrue();
		var descriptionId = addButton.GetAttribute("aria-describedby");
		descriptionId.Should().NotBeNullOrWhiteSpace();
		cut.Find($"#{descriptionId}").TextContent
			.Should().Be(ClientStrings.Validation_PlayerLimitReached);
	}

	private static string Format(string format, params object[] args) =>
		string.Format(CultureInfo.CurrentCulture, format, args);
}
