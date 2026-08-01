using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public sealed class DevotedServantFlowBunitTests
{
	private const string GroupRole = "group";

	[Fact]
	public async Task PublicVoteWindow_NoActorSelected_RendersOneWayContinueAndSubmitsCorrelatedResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var targetId = Guid.NewGuid();
		var firstActorId = Guid.NewGuid();
		var secondActorId = Guid.NewGuid();
		var responses = new List<ModeratorResponse>();
		var instruction = CreateVoteWindowInstruction(
			targetId,
			[firstActorId, secondActorId]);
		var roster = new[]
		{
			CreateRosterEntry(targetId, 1, PlayerNames.Ana),
			CreateRosterEntry(secondActorId, 2, PlayerNames.Bruno),
			CreateRosterEntry(firstActorId, 3, PlayerNames.Carla)
		};

		var cut = RenderInstruction(context, instruction, roster, responses);

		var actorOptions = cut.FindAll(
			$"{Html.Elements.ListItem}[role='{Html.Roles.Option}']");
		actorOptions.Should().HaveCount(2);
		actorOptions[0].TextContent.Should().Contain(PlayerNames.Bruno);
		actorOptions[1].TextContent.Should().Contain(PlayerNames.Carla);
		var holdButton = FindHoldButton(cut);
		holdButton.TextContent.Should().Contain(ClientStrings.Dashboard_ContinueButton);

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		var response = responses.Should().ContainSingle().Subject;
		response.InstructionId.Should().Be(instruction.InstructionId);
		response.Type.Should().Be(ExpectedInputType.Continue);
		response.SelectedPlayerIds.Should().BeNull();
		response.AssignedPlayerRoles.Should().BeNull();
		response.SelectedOptionIds.Should().BeNull();
	}

	[Fact]
	public async Task PublicVoteWindow_SelectedActor_SubmitsOnlyThatPublicSelfReveal()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var targetId = Guid.NewGuid();
		var actorId = Guid.NewGuid();
		var responses = new List<ModeratorResponse>();
		var instruction = CreateVoteWindowInstruction(targetId, [actorId]);
		var roster = new[]
		{
			CreateRosterEntry(targetId, 1, PlayerNames.Ana),
			CreateRosterEntry(actorId, 2, PlayerNames.Bruno)
		};
		var cut = RenderInstruction(context, instruction, roster, responses);
		var actorOption = cut.Find(
			$"{Html.Elements.ListItem}[role='{Html.Roles.Option}']");

		actorOption.Click();

		actorOption.GetAttribute(Html.Attributes.AriaSelected)
			.Should().Be(Html.AriaValues.True);
		var holdButton = FindHoldButton(cut);
		holdButton.TextContent.Should().Contain(ClientStrings.SelectPlayers_SubmitButton);

		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		var response = responses.Should().ContainSingle().Subject;
		response.InstructionId.Should().Be(instruction.InstructionId);
		response.Type.Should().Be(ExpectedInputType.PlayerSelection);
		response.SelectedPlayerIds.Should().BeEquivalentTo([actorId]);
		response.AssignedPlayerRoles.Should().BeNull();
		response.SelectedOptionIds.Should().BeNull();
	}

	[Fact]
	public async Task AcceptedSelfReveal_RecoversPrivateFixedTargetRoleRecordingThroughGenericRenderer()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var scenario = AdvanceToVoteWindow(manager);

		manager.ProcessInput(
			scenario.Window.CreatePublicSelfRevealResponse(scenario.ActorId))
			.IsSuccess.Should().BeTrue();

		var resumed = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var acquiredCard = resumed.CurrentInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		acquiredCard.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard);
		acquiredCard.InstructionId.Should().Be(
			manager.CurrentInstruction!.InstructionId);
		acquiredCard.PlayersForAssignment.Should().Equal(scenario.TargetId);
		acquiredCard.AffectedPlayerIds.Should().Equal(
			scenario.ActorId,
			scenario.TargetId);
		var actor = resumed.CurrentRoster.Single(
			entry => entry.PlayerId == scenario.ActorId);
		actor.RoleLabel.Should().Be(MainRoleType.DevotedServant.GetPublicName());
		actor.RoleVisibility.Should().Be(DashboardRoleVisibility.Public);
		var target = resumed.CurrentRoster.Single(
			entry => entry.PlayerId == scenario.TargetId);
		target.RoleVisibility.Should().Be(DashboardRoleVisibility.Unknown);

		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var cut = RenderInstruction(
			context,
			acquiredCard,
			resumed.CurrentRoster,
			responses);
		var assignmentGroup = cut.FindAll($"[{Html.Attributes.Role}='{GroupRole}']")
			.Single(group =>
				group.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.AssignRoles_Title);
		assignmentGroup.TextContent.Should().Contain(target.Name);
		assignmentGroup.TextContent.Should().NotContain(actor.Name);
		var printedRole = MainRoleType.SimpleVillager;
		cut.FindAll(Html.Selectors.Button)
			.Single(button => button.TextContent.Contains(
				printedRole.GetPublicName(),
				StringComparison.CurrentCulture))
			.Click();

		await RenderedHoldButtonDriver.CompleteHoldAsync(
			cut,
			FindHoldButton(cut),
			timing);

		var response = responses.Should().ContainSingle().Subject;
		response.InstructionId.Should().Be(acquiredCard.InstructionId);
		response.Type.Should().Be(ExpectedInputType.AssignPlayerRoles);
		response.AssignedPlayerRoles.Should().ContainSingle();
		response.AssignedPlayerRoles![scenario.TargetId].Should().Be(printedRole);
		response.SelectedPlayerIds.Should().BeNull();
		response.SelectedOptionIds.Should().BeNull();
	}

	[Fact]
	public void AcceptedAcquiredCard_RecoversModeratorPrivateRoleWithoutPublicProjectionLeak()
	{
		using var saveDirectory = TemporaryDirectory.Create();
		var manager = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		var scenario = AdvanceToVoteWindow(manager);
		manager.ProcessInput(
			scenario.Window.CreatePublicSelfRevealResponse(scenario.ActorId))
			.IsSuccess.Should().BeTrue();
		var acquiredCard = manager.CurrentInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var acquiredPrintedRole = MainRoleType.SimpleVillager;

		manager.ProcessInput(acquiredCard.CreateResponse(new Dictionary<Guid, MainRoleType>
		{
			[scenario.TargetId] = acquiredPrintedRole
		})).IsSuccess.Should().BeTrue();

		var expectedRecoveredInstruction = manager.CurrentInstruction!;
		var resumed = new GameClientManager(
			new GameService(),
			saveStore: new FileGameSessionSaveStore(saveDirectory.Path));
		resumed.CurrentInstruction.Should().NotBeOfType<AssignRolesInstruction>();
		resumed.CurrentInstruction!.GetType().Should().Be(
			expectedRecoveredInstruction.GetType());
		resumed.CurrentInstruction.InstructionId.Should().Be(
			expectedRecoveredInstruction.InstructionId);
		var actor = resumed.CurrentRoster.Single(
			entry => entry.PlayerId == scenario.ActorId);
		actor.RoleLabel.Should().Be(acquiredPrintedRole.GetPublicName());
		actor.RoleVisibility.Should().Be(DashboardRoleVisibility.ModeratorPrivate);
		var actorState = resumed.CurrentSession!
			.GetPlayerState(scenario.ActorId);
		actorState.ModeratorKnownRole.Should().Be(acquiredPrintedRole);
		actorState.PubliclyRevealedRole.Should().Be(MainRoleType.DevotedServant);
		var target = resumed.CurrentRoster.Single(
			entry => entry.PlayerId == scenario.TargetId);
		target.IsDead.Should().BeTrue();

		var publicStats = DashboardStatsSnapshot.FromSession(resumed.CurrentSession);
		var publicRoles = publicStats.RoleGroups
			.SelectMany(group => group.Roles)
			.ToArray();
		publicRoles.Should().ContainSingle(role =>
			role.Role == MainRoleType.DevotedServant &&
			role.RemainingCount == 1);
		publicRoles.Should().NotContain(role => role.Role == acquiredPrintedRole);
	}

	private static IRenderedComponent<InstructionRenderer> RenderInstruction(
		ModeratorComponentTestContext context,
		ModeratorInstruction instruction,
		IReadOnlyList<DashboardRosterEntry> roster,
		ICollection<ModeratorResponse> responses) =>
		context.RenderModeratorComponent<InstructionRenderer>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.Roster, roster)
			.Add(
				component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(new object(), responses.Add)));

	private static AngleSharp.Dom.IElement FindHoldButton(
		IRenderedComponent<InstructionRenderer> cut) =>
		cut.FindAll(Html.Selectors.Button)
			.Single(button =>
				button.GetAttribute(Html.Attributes.AriaLabel) ==
				ClientStrings.Common_HoldToConfirm);

	private static DevotedServantVoteWindowInstruction CreateVoteWindowInstruction(
		Guid targetId,
		HashSet<Guid> selectablePlayerIds) =>
		(DevotedServantVoteWindowInstruction)VoteWindowConstructor.Invoke(
			[targetId, selectablePlayerIds, GameStrings.DevotedServantVoteWindowAnnouncement]);

	private static DashboardRosterEntry CreateRosterEntry(
		Guid playerId,
		int seatNumber,
		string name) =>
		new(
			playerId,
			seatNumber,
			name,
			DashboardRoster.UnknownRoleLabel,
			IsRoleKnown: false,
			DashboardRoster.HealthLabel(PlayerHealth.Alive),
			IsDead: false,
			StatusEffects: [],
			DashboardRoster.NoStatusEffectsLabel);

	private static VoteWindowScenario AdvanceToVoteWindow(
		GameClientManager manager)
	{
		var start = manager.StartGame(
			[
				PlayerNames.Ana,
				PlayerNames.Bruno,
				PlayerNames.Catarina,
				PlayerNames.Diana,
				PlayerNames.Eduardo,
				PlayerNames.Filipe
			],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		manager.ProcessInput(start.CreateResponse()).IsSuccess.Should().BeTrue();
		var players = manager.CurrentSession!.GetPlayers().ToArray();
		var werewolfId = players[0].Id;
		var dawnVictimId = players[1].Id;

		for (var step = 0; manager.CurrentPhase == GamePhase.Night && step < 20; step++)
		{
			switch (manager.CurrentInstruction)
			{
				case ConfirmationInstruction confirmation:
					manager.ProcessInput(confirmation.CreateResponse())
						.IsSuccess.Should().BeTrue();
					break;
				case SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup
				} observation:
					manager.ProcessInput(observation.CreateResponse([werewolfId]))
						.IsSuccess.Should().BeTrue();
					break;
				case SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
				} victimSelection:
					manager.ProcessInput(victimSelection.CreateResponse([dawnVictimId]))
						.IsSuccess.Should().BeTrue();
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected Night instruction {manager.CurrentInstruction?.GetType().Name} " +
						$"({manager.CurrentInstruction?.Semantic}).");
			}
		}

		for (var step = 0; manager.CurrentPhase == GamePhase.Dawn && step < 20; step++)
		{
			switch (manager.CurrentInstruction)
			{
				case ConfirmationInstruction confirmation:
					manager.ProcessInput(confirmation.CreateResponse())
						.IsSuccess.Should().BeTrue();
					break;
				case AssignRolesInstruction assignRoles:
					manager.ProcessInput(assignRoles.CreateResponse(
						assignRoles.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager)))
						.IsSuccess.Should().BeTrue();
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected Dawn instruction {manager.CurrentInstruction?.GetType().Name} " +
						$"({manager.CurrentInstruction?.Semantic}).");
			}
		}

		manager.CurrentPhase.Should().Be(GamePhase.Day);
		var debate = manager.CurrentInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var vote = manager.ProcessInput(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var targetId = players[2].Id;
		var actorId = players[3].Id;
		var window = manager.ProcessInput(vote.CreateResponse([targetId]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;
		return new VoteWindowScenario(window, actorId, targetId);
	}

	private sealed record VoteWindowScenario(
		DevotedServantVoteWindowInstruction Window,
		Guid ActorId,
		Guid TargetId);

	private sealed class TemporaryDirectory : IDisposable
	{
		private TemporaryDirectory(string path)
		{
			Path = path;
		}

		public string Path { get; }

		public static TemporaryDirectory Create() =>
			new(Directory.CreateTempSubdirectory("werewolves-devoted-servant-client-").FullName);

		public void Dispose()
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
	}

	private static readonly ConstructorInfo VoteWindowConstructor =
		typeof(DevotedServantVoteWindowInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(constructor => constructor.GetParameters().Length == 3);
}
