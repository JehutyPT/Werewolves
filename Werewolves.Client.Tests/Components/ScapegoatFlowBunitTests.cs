using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public sealed class ScapegoatFlowBunitTests
{
	private static string PublicInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionAnnouncement}";

	private static string PrivateInstructionSelector =>
		$".{ClientTestReferences.Css.Classes.InstructionPrivate}";

	private static string HoldButtonSelector =>
		Html.Selectors.ButtonWithClass(
			ClientTestReferences.Css.Classes.HoldButton);

	private static string PlayerOptionSelector =>
		Html.Selectors.ElementWithRole(
			Html.Elements.ListItem,
			Html.Roles.Option);

	[Fact]
	public async Task TiedVote_RendersScapegoatRevealFixedVotersAndDistinctNextDayVoteGuidance()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var manager = context.Services.GetRequiredService<GameClientManager>();
		var playerNames = new[]
		{
			PlayerNames.Ana,
			PlayerNames.Bruno,
			PlayerNames.Catarina,
			PlayerNames.Diana,
			PlayerNames.Eduardo,
			PlayerNames.Eva,
			PlayerNames.Filipe
		};
		var roles = new[]
		{
			MainRoleType.SimpleWerewolf,
			MainRoleType.Scapegoat,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var start = manager.StartGame(playerNames, roles);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var rolesByPlayerId = players
			.Select((player, index) => (player.Id, Role: roles[index]))
			.ToDictionary(item => item.Id, item => item.Role);
		var werewolfVictims = new Queue<Guid>(
			[
				players[6].Id,
				players[2].Id
			]);

		AdvanceUntilSemantic(
			manager,
			ModeratorInstructionSemantic.RecordDayVote,
			rolesByPlayerId,
			werewolfVictims);

		var dayVote = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		dayVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var firstDayLivingRoster = manager.CurrentRoster
			.Where(entry => !entry.IsDead)
			.ToArray();
		var voteResponses = new List<ModeratorResponse>();
		var voteCut = RenderInstruction(
			context,
			dayVote,
			manager.CurrentRoster,
			voteResponses);

		voteCut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.VoteStartsPublicInstruction);
		voteCut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.VoteStartsModeratorInstruction);
		var voteOptions = voteCut.FindAll(PlayerOptionSelector);
		voteOptions.Should().HaveCount(
			dayVote.SelectablePlayerIds.Count + 1);
		foreach (var livingPlayer in firstDayLivingRoster)
		{
			FindPlayerOption(voteCut, livingPlayer.Name).Should().NotBeNull();
		}

		var tieOption = FindPlayerOption(
			voteCut,
			GameStrings.DayVoteNoEliminationOption);
		tieOption.Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			voteCut,
			FindHoldButton(voteCut),
			timing);

		voteResponses.Should().ContainSingle();
		voteResponses.Single().SelectedPlayerIds.Should().BeEmpty();
		manager.ProcessInput(voteResponses.Single()).IsSuccess.Should().BeTrue();

		var holderObservation = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		holderObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie);
		holderObservation.CountConstraint.Should().Be(
			NumberRangeConstraint.SingleOptional);
		var observationResponses = new List<ModeratorResponse>();
		var observationCut = RenderInstruction(
			context,
			holderObservation,
			manager.CurrentRoster,
			observationResponses);

		observationCut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		observationCut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(GameStrings.ScapegoatHolderObservationInstruction);
		FindPlayerOption(
			observationCut,
			GameStrings.ScapegoatNoRevealOption).Should().NotBeNull();
		FindPlayerOption(observationCut, players[1].Name).Click();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			observationCut,
			FindHoldButton(observationCut),
			timing);

		observationResponses.Should().ContainSingle();
		observationResponses.Single().SelectedPlayerIds.Should()
			.BeEquivalentTo([players[1].Id]);
		manager.ProcessInput(observationResponses.Single())
			.IsSuccess.Should().BeTrue();
		var revealedScapegoat = manager.CurrentRoster
			.Single(entry => entry.PlayerId == players[1].Id);
		revealedScapegoat.RoleVisibility.Should().Be(
			DashboardRoleVisibility.Public);
		revealedScapegoat.RoleLabel.Should().Be(
			MainRoleType.Scapegoat.GetPublicName());

		var voterSelection = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		voterSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		voterSelection.CountConstraint.Should().Be(
			NumberRangeConstraint.AtLeast(1));
		var voterResponses = new List<ModeratorResponse>();
		var voterCut = RenderInstruction(
			context,
			voterSelection,
			manager.CurrentRoster,
			voterResponses);

		voterCut.FindAll(PublicInstructionSelector).Should().BeEmpty();
		voterCut.Find(PrivateInstructionSelector).TextContent.Should()
			.Contain(
				GameStrings.ScapegoatPermittedVotersSelectionInstruction);
		FindHoldButton(voterCut)
			.HasAttribute(Html.Attributes.Disabled)
			.Should().BeTrue();
		var selectedVoterIds = new[] { players[2].Id, players[3].Id };
		foreach (var selectedVoterId in selectedVoterIds)
		{
			var selectedVoterName = manager.CurrentRoster
				.Single(entry => entry.PlayerId == selectedVoterId)
				.Name;
			FindPlayerOption(voterCut, selectedVoterName).Click();
		}

		FindHoldButton(voterCut)
			.HasAttribute(Html.Attributes.Disabled)
			.Should().BeFalse();
		await RenderedHoldButtonDriver.CompleteHoldAsync(
			voterCut,
			FindHoldButton(voterCut),
			timing);

		voterResponses.Should().ContainSingle();
		voterResponses.Single().SelectedPlayerIds.Should()
			.BeEquivalentTo(selectedVoterIds);
		manager.ProcessInput(voterResponses.Single()).IsSuccess.Should().BeTrue();

		var announcement = manager.CurrentInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		announcement.AffectedPlayerIds.Should()
			.BeEquivalentTo(selectedVoterIds);
		var announcedNames = announcement.AffectedPlayerIds!
			.Select(playerId => manager.CurrentSession.GetPlayer(playerId).Name)
			.ToArray();
		var expectedAnnouncement =
			GameStrings.ScapegoatPermittedVotersAnnouncement.Format(
				string.Join(Environment.NewLine, announcedNames));
		announcement.PublicAnnouncement.Should().Be(expectedAnnouncement);
		var announcementResponses = new List<ModeratorResponse>();
		var announcementCut = RenderInstruction(
			context,
			announcement,
			manager.CurrentRoster,
			announcementResponses);
		var renderedAnnouncement =
			announcementCut.Find(PublicInstructionSelector).TextContent;

		renderedAnnouncement.Should().Contain(expectedAnnouncement);
		foreach (var selectedName in announcedNames)
		{
			renderedAnnouncement.Should().Contain(selectedName);
		}
		foreach (var unselectedId in
			voterSelection.SelectablePlayerIds.Except(selectedVoterIds))
		{
			renderedAnnouncement.Should().NotContain(
				manager.CurrentSession.GetPlayer(unselectedId).Name);
		}

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			announcementCut,
			FindHoldButton(announcementCut),
			timing);

		announcementResponses.Should().ContainSingle();
		announcementResponses.Single().Type.Should().Be(
			ExpectedInputType.Continue);
		manager.ProcessInput(announcementResponses.Single())
			.IsSuccess.Should().BeTrue();

		AdvanceUntilSemantic(
			manager,
			ModeratorInstructionSemantic.RecordDayVote,
			rolesByPlayerId,
			werewolfVictims);

		var restrictedDayVote = manager.CurrentInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var secondDayLivingRoster = manager.CurrentRoster
			.Where(entry => !entry.IsDead)
			.ToArray();
		var survivingPermittedVoter = players[3];
		var eliminatedPermittedVoter = players[2];
		var expectedEffectiveVoterGuidance =
			GameStrings.ScapegoatEffectiveVotersInstruction.Format(
				survivingPermittedVoter.Name);
		var restrictedVoteCut = RenderInstruction(
			context,
			restrictedDayVote,
			manager.CurrentRoster,
			[]);

		restrictedDayVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		restrictedDayVote.SelectablePlayerIds.Should().BeEquivalentTo(
			secondDayLivingRoster.Select(entry => entry.PlayerId));
		restrictedVoteCut.Find(PublicInstructionSelector).TextContent.Should()
			.Contain(GameStrings.VoteStartsPublicInstruction);
		var restrictedPrivateGuidance =
			restrictedVoteCut.Find(PrivateInstructionSelector).TextContent;
		restrictedPrivateGuidance.Should().Be(
			expectedEffectiveVoterGuidance);
		restrictedPrivateGuidance.Should().NotContain(
			eliminatedPermittedVoter.Name);
		foreach (var livingPlayer in secondDayLivingRoster)
		{
			FindPlayerOption(restrictedVoteCut, livingPlayer.Name)
				.Should().NotBeNull();
		}
		FindPlayerOption(
			restrictedVoteCut,
			GameStrings.DayVoteNoEliminationOption).Should().NotBeNull();
		FindPlayerOption(
			restrictedVoteCut,
			players[4].Name).Should().NotBeNull(
				"Vote targets are all living players, not only effective voters");
	}

	private IRenderedComponent<InstructionRenderer> RenderInstruction(
		ModeratorComponentTestContext context,
		ModeratorInstruction instruction,
		IReadOnlyList<DashboardRosterEntry> roster,
		ICollection<ModeratorResponse> responses) =>
		context.RenderModeratorComponent<InstructionRenderer>(
			parameters => parameters
				.Add(component => component.Instruction, instruction)
				.Add(component => component.Roster, roster)
				.Add(
					component => component.OnResponse,
					EventCallback.Factory.Create<ModeratorResponse>(
						this,
						responses.Add)));

	private static IElement FindPlayerOption(
		IRenderedComponent<InstructionRenderer> cut,
		string text) =>
		cut.FindAll(PlayerOptionSelector)
			.Single(option => option.TextContent.Contains(
				text,
				StringComparison.CurrentCulture));

	private static IElement FindHoldButton(
		IRenderedComponent<InstructionRenderer> cut) =>
		cut.Find(HoldButtonSelector);

	private static void AdvanceUntilSemantic(
		GameClientManager manager,
		ModeratorInstructionSemantic targetSemantic,
		IReadOnlyDictionary<Guid, MainRoleType> rolesByPlayerId,
		Queue<Guid> werewolfVictims)
	{
		for (var step = 0; step < 100; step++)
		{
			var instruction = manager.CurrentInstruction
				?? throw new InvalidOperationException(
					"The game cannot advance without an instruction.");
			if (instruction.Semantic == targetSemantic)
			{
				return;
			}

			var response = instruction switch
			{
				ConfirmationInstruction confirmation =>
					confirmation.CreateResponse(),
				SelectPlayersInstruction
				{
					Semantic:
					ModeratorInstructionSemantic.IdentifyRoleHolders
				} identify =>
					identify.CreateResponse(
						identify.SelectablePlayerIds
							.Where(playerId =>
								rolesByPlayerId[playerId] ==
								MainRoleType.SimpleWerewolf)
							.ToHashSet()),
				SelectPlayersInstruction
				{
					Semantic:
					ModeratorInstructionSemantic.SelectWerewolfVictim
				} victimSelection =>
					victimSelection.CreateResponse(
						[werewolfVictims.Dequeue()]),
				AssignRolesInstruction assignment =>
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => rolesByPlayerId[playerId])),
				_ => throw new InvalidOperationException(
					$"Unexpected instruction while advancing the Scapegoat game: " +
					$"{instruction.GetType().Name} ({instruction.Semantic}).")
			};

			manager.ProcessInput(response).IsSuccess.Should().BeTrue();
		}

		throw new InvalidOperationException(
			$"The Scapegoat game did not reach {targetSemantic}.");
	}
}
