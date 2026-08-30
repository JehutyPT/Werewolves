using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class WolfHoundContractTests : DiagnosticTestBase
{
	public WolfHoundContractTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_IdentificationLeavesWerewolfFactionAgencyUnknownUntilAlignmentChoice()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Wolf Hound",
				"Simple Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var session = builder.GetGameState()!;
		var wolfHound = session.GetPlayers().First();
		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().Be(MainRoleType.WolfHound);
		session.GetFactionAgentKnowledge(wolfHound.Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.Unknown);

		var wake = builder.Process(
			identification.CreateResponse([wolfHound.Id]));

		wake.IsSuccess.Should().BeTrue();
		wake.ModeratorInstruction.Should().BeOfType<SelectOptionsInstruction>()
			.Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.ChooseWolfHoundAlignment);
		session.GetFactionAgentKnowledge(wolfHound.Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.Unknown);
		session.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>().Should()
			.NotContain(entry => entry.Source.Identifier ==
				FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownHolder_WakesAndChoosesWithoutRepeatedIdentification()
	{
		var scenario = CreateKnownWolfHoundScenario();
		var session = scenario.Builder.GetGameState()!;
		var identificationCount = session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.WolfHound);

		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			scenario.Builder.ConfirmNightStart());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(scenario.WolfHoundId);
		var alignment =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				scenario.Builder.Process(wake.CreateResponse()));
		alignment.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment);
		alignment.AffectedPlayerIds.Should().Equal(scenario.WolfHoundId);
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.WolfHound)
			.Should().Be(identificationCount);
		identificationCount.Should().Be(1);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownEmptyHolder_OmitsTheWholeWolfHoundCall()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Wolf Hound",
				"Simple Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wolfHoundId = players[0].Id;
		var simpleWerewolfId = players[1].Id;
		builder
			.ArrangeKnownRole(wolfHoundId, MainRoleType.WolfHound)
			.ArrangeEliminatedPlayer(wolfHoundId)
			.ArrangeKnownRole(simpleWerewolfId, MainRoleType.SimpleWerewolf)
			.ArrangeKnownWerewolfFactionAgentGroup(simpleWerewolfId);
		builder.ConfirmGameStart();

		var collectiveWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());

		collectiveWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		collectiveWake.AffectedPlayerIds.Should().Equal(simpleWerewolfId);
		collectiveWake.AffectedPlayerIds.Should().NotContain(wolfHoundId);
		builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Facts.Any(fact =>
					fact.PlayerId == wolfHoundId &&
					fact.Type == FactionFactType.Beneficiary));
		MarkTestCompleted();
	}

	[Fact]
	public void WerewolvesChoice_CommitsOneFactionBatchAndJoinsTheSameNightCollective()
	{
		var scenario = CreateKnownWolfHoundScenario();

		var collectiveWake = CommitAlignmentAndReachCollectiveWake(
			scenario,
			WolfHoundAlignmentOptionIds.Werewolves);

		AssertAlignmentFacts(
			scenario,
			Faction.Werewolf,
			FactionAgentKnowledge.KnownAgent);
		collectiveWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		collectiveWake.AffectedPlayerIds.Should().BeEquivalentTo(
			[scenario.SimpleWerewolfId, scenario.WolfHoundId]);

		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				scenario.Builder.Process(collectiveWake.CreateResponse()));
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		victimSelection.AffectedPlayerIds.Should().BeEquivalentTo(
			[scenario.SimpleWerewolfId, scenario.WolfHoundId]);
		victimSelection.SelectablePlayerIds.Should()
			.NotContain(scenario.SimpleWerewolfId)
			.And.NotContain(scenario.WolfHoundId);
		MarkTestCompleted();
	}

	[Fact]
	public void VillagersChoice_CommitsOneFactionBatchAndRemainsALegalCollectiveTarget()
	{
		var scenario = CreateKnownWolfHoundScenario();

		var collectiveWake = CommitAlignmentAndReachCollectiveWake(
			scenario,
			WolfHoundAlignmentOptionIds.Villagers);

		AssertAlignmentFacts(
			scenario,
			Faction.Villager,
			FactionAgentKnowledge.KnownNonAgent);
		collectiveWake.AffectedPlayerIds.Should().Equal(
			scenario.SimpleWerewolfId);

		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				scenario.Builder.Process(collectiveWake.CreateResponse()));
		victimSelection.AffectedPlayerIds.Should().Equal(
			scenario.SimpleWerewolfId);
		victimSelection.SelectablePlayerIds.Should().Contain(
			scenario.WolfHoundId);

		var collectiveSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(
					victimSelection.CreateResponse([scenario.WolfHoundId])));
		collectiveSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection &&
				entry.TargetIds!.SequenceEqual(
					new[] { scenario.WolfHoundId }));
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(WolfHoundAlignmentOptionIds.Villagers, false)]
	[InlineData(WolfHoundAlignmentOptionIds.Werewolves, true)]
	public void AlignmentChoice_FollowingCollectiveObservationOffersOnlyPossibleAgents(
		string optionId,
		bool expectsWolfHoundCandidate)
	{
		var scenario = CreateKnownWolfHoundScenario(
			arrangeKnownWerewolfAgentGroup: false);
		var alignment = ReachAlignmentChoice(scenario);
		var wolfHoundSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(alignment.CreateResponse(optionId)));

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				scenario.Builder.Process(wolfHoundSleep.CreateResponse()));

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.SelectablePlayerIds.Contains(scenario.WolfHoundId)
			.Should().Be(expectsWolfHoundCandidate);
		observation.SelectablePlayerIds.Should().Contain(
			scenario.SimpleWerewolfId);
		MarkTestCompleted();
	}

	[Fact]
	public void AlignmentChoice_InvalidPayloadsAreSideEffectFree()
	{
		var scenario = CreateKnownWolfHoundScenario();
		var alignment = ReachAlignmentChoice(scenario);
		var invalidCases = new (string Name, ModeratorResponse Response)[]
		{
			("empty", new ModeratorResponse
			{
				InstructionId = alignment.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = []
			}),
			("both", new ModeratorResponse
			{
				InstructionId = alignment.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
				[
					WolfHoundAlignmentOptionIds.Villagers,
					WolfHoundAlignmentOptionIds.Werewolves
				]
			}),
			("duplicate", new ModeratorResponse
			{
				InstructionId = alignment.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
				[
					WolfHoundAlignmentOptionIds.Villagers,
					WolfHoundAlignmentOptionIds.Villagers
				]
			}),
			("unknown", new ModeratorResponse
			{
				InstructionId = alignment.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = ["unknown-wolf-hound-alignment"]
			}),
			("wrong-type", new ModeratorResponse
			{
				InstructionId = alignment.InstructionId,
				Type = ExpectedInputType.Continue
			}),
			("wrong-correlation", new ModeratorResponse
			{
				InstructionId = Guid.Empty,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = [WolfHoundAlignmentOptionIds.Villagers]
			})
		};

		foreach (var invalidCase in invalidCases)
		{
		var before = scenario.Builder.SerializeSession();
			var beforeHistory =
				scenario.Builder.GetGameState()!.GameHistoryLog.ToArray();

			var act = () => scenario.Builder.Process(invalidCase.Response);

			act.Should().Throw<InvalidOperationException>(invalidCase.Name);
			scenario.Builder.GetCurrentInstruction()!.InstructionId.Should().Be(
				alignment.InstructionId,
				invalidCase.Name);
			scenario.Builder.SerializeSession().Should().Be(
				before,
				invalidCase.Name);
			scenario.Builder.GetGameState()!.GameHistoryLog.Should().Equal(
				beforeHistory,
				invalidCase.Name);
		}

		MarkTestCompleted();
	}

	[Fact]
	public void AlignmentChoice_AcceptedResponseReplayIsSideEffectFree()
	{
		var scenario = CreateKnownWolfHoundScenario();
		var alignment = ReachAlignmentChoice(scenario);
		var accepted = alignment.CreateResponse(
			WolfHoundAlignmentOptionIds.Werewolves);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(accepted));
		var beforeReplay = PublicGameSessionSnapshot.Capture(scenario.Builder);

		var replay = () => scenario.Builder.Process(accepted);

		replay.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(scenario.Builder).Should()
			.BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());
		scenario.Builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			sleep.InstructionId);
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Count(entry => entry.Facts.Any(fact =>
				fact.PlayerId == scenario.WolfHoundId &&
				fact.Type == FactionFactType.Beneficiary))
			.Should().Be(1);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(
		WolfHoundAlignmentOptionIds.Villagers,
		Faction.Villager,
		FactionAgentKnowledge.KnownNonAgent)]
	[InlineData(
		WolfHoundAlignmentOptionIds.Werewolves,
		Faction.Werewolf,
		FactionAgentKnowledge.KnownAgent)]
	public void LaterPublicReveal_IdentifiesWolfHoundWithoutChangingOrDisclosingAlignment(
		string alignmentOptionId,
		Faction expectedBeneficiary,
		FactionAgentKnowledge expectedWerewolfFactionAgentKnowledge)
	{
		var scenario = CreateKnownWolfHoundScenario(playerCount: 6);
		var collectiveWake = CommitAlignmentAndReachCollectiveWake(
			scenario,
			alignmentOptionId);
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				scenario.Builder.Process(collectiveWake.CreateResponse()));
		var victimId = scenario.VillagerIds[0];
		var collectiveSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(
					victimSelection.CreateResponse([victimId])));
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(collectiveSleep.CreateResponse()));
		var victimReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				scenario.Builder.Process(finishNight.CreateResponse()));
		var afterVictimReveal = scenario.Builder.Process(
			victimReveal.CreateObservedRoleResponse(new()
			{
				[victimId] = MainRoleType.SimpleVillager
			}));
		var debate =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterVictimReveal);
		var vote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				scenario.Builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(
					vote.CreateResponse([scenario.WolfHoundId])));
		var session = scenario.Builder.GetGameState()!;
		var wolfHoundState =
			session.GetPlayerState(scenario.WolfHoundId);
		var physicalCardBeforeReveal =
			wolfHoundState.PhysicalCharacterCardRole;
		var transitionCountBeforeReveal = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Count(entry => entry.Facts.Any(fact =>
				fact.PlayerId == scenario.WolfHoundId &&
				fact.Type == FactionFactType.Beneficiary));

		(reveal.PublicAnnouncement ?? string.Empty).Should()
			.NotContain(GameStrings.VillagersGroupName)
			.And.NotContain(GameStrings.WerewolvesGroupName);

		scenario.Builder.Process(reveal.CreateResponse());

		wolfHoundState.CurrentRole.Should().Be(MainRoleType.WolfHound);
		wolfHoundState.ModeratorKnownRole.Should().Be(MainRoleType.WolfHound);
		wolfHoundState.PubliclyRevealedRole.Should().Be(
			MainRoleType.WolfHound);
		wolfHoundState.PhysicalCharacterCardRole.Should().Be(
			physicalCardBeforeReveal);
		session.GetFactionBeneficiaryKnowledge(scenario.WolfHoundId)
			.Should().Be(
				FactionBeneficiaryKnowledge.Known(expectedBeneficiary));
		session.GetFactionAgentKnowledge(
				scenario.WolfHoundId,
				Faction.Werewolf)
			.Should().Be(expectedWerewolfFactionAgentKnowledge);
		session.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.Count == 1 &&
				entry.RevealedRoles.GetValueOrDefault(
					scenario.WolfHoundId) == MainRoleType.WolfHound);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Count(entry => entry.Facts.Any(fact =>
				fact.PlayerId == scenario.WolfHoundId &&
				fact.Type == FactionFactType.Beneficiary))
			.Should().Be(transitionCountBeforeReveal);
		MarkTestCompleted();
	}

	[Fact]
	public void RoleIdentification_AfterWerewolvesAlignment_PreservesKnownWerewolfFactionAgent()
	{
		var scenario = CreateKnownWolfHoundScenario(playerCount: 6);
		CommitAlignmentAndReachCollectiveWake(
			scenario,
			WolfHoundAlignmentOptionIds.Werewolves);
		var session = scenario.Builder.GetGameState()!;
		session.GetFactionAgentKnowledge(
			scenario.WolfHoundId,
			Faction.Werewolf).Should().Be(FactionAgentKnowledge.KnownAgent);
		var provenanceBeforeIdentification =
			scenario.Builder.GameService.GetEarliestWerewolfAgencyFact(
				scenario.Builder.GameId,
				scenario.WolfHoundId);

		scenario.Builder.ArrangeKnownRole(
			scenario.WolfHoundId,
			MainRoleType.WolfHound);

		session.GetFactionAgentKnowledge(
			scenario.WolfHoundId,
			Faction.Werewolf).Should().Be(FactionAgentKnowledge.KnownAgent);
		scenario.Builder.GameService.GetEarliestWerewolfAgencyFact(
			scenario.Builder.GameId,
			scenario.WolfHoundId).Should().Be(provenanceBeforeIdentification);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Identifier == FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier &&
				entry.Facts.Any(fact => fact.PlayerId == scenario.WolfHoundId));
		MarkTestCompleted();
	}

	private KnownWolfHoundScenario CreateKnownWolfHoundScenario(
		bool arrangeKnownWerewolfAgentGroup = true,
		int playerCount = 5)
	{
		var builder = CreateBuilder()
			.WithPlayers(playerCount)
			.WithRoles(
				[
					MainRoleType.WolfHound,
					MainRoleType.SimpleWerewolf,
					.. Enumerable.Repeat(
						MainRoleType.SimpleVillager,
						playerCount - 2)
				]);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wolfHoundId = players[0].Id;
		var simpleWerewolfId = players[1].Id;
		builder
			.ArrangeKnownPhysicalRole(wolfHoundId, MainRoleType.WolfHound)
			.ArrangeKnownPhysicalRole(
				simpleWerewolfId,
				MainRoleType.SimpleWerewolf);
		if (arrangeKnownWerewolfAgentGroup)
		{
			builder.ArrangeKnownWerewolfFactionAgentGroup(simpleWerewolfId);
		}
		builder.ConfirmGameStart();
		return new KnownWolfHoundScenario(
			builder,
			wolfHoundId,
			simpleWerewolfId,
			players.Skip(2).Select(player => player.Id).ToArray());
	}

	private static SelectOptionsInstruction ReachAlignmentChoice(
		KnownWolfHoundScenario scenario)
	{
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.ConfirmNightStart());
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		return InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			scenario.Builder.Process(wake.CreateResponse()));
	}

	private static ConfirmationInstruction CommitAlignmentAndReachCollectiveWake(
		KnownWolfHoundScenario scenario,
		string optionId)
	{
		var alignment = ReachAlignmentChoice(scenario);
		var wolfHoundSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(
					alignment.CreateResponse(optionId)));
		wolfHoundSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		return InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			scenario.Builder.Process(wolfHoundSleep.CreateResponse()));
	}

	private static void AssertAlignmentFacts(
		KnownWolfHoundScenario scenario,
		Faction expectedBeneficiary,
		FactionAgentKnowledge expectedAgentKnowledge)
	{
		var session = scenario.Builder.GetGameState()!;
		session.GetFactionBeneficiaryKnowledge(scenario.WolfHoundId)
			.Should().Be(
				FactionBeneficiaryKnowledge.Known(expectedBeneficiary));
		session.GetFactionAgentKnowledge(
				scenario.WolfHoundId,
				Faction.Werewolf)
			.Should().Be(expectedAgentKnowledge);
		var transition = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind == FactionFactSourceKind.ExplicitTransition &&
				entry.Facts.Any(fact =>
					fact.PlayerId == scenario.WolfHoundId &&
					fact.Type == FactionFactType.Beneficiary))
			.Subject;
		transition.Facts.Should().HaveCount(2);
		transition.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == scenario.WolfHoundId &&
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == expectedBeneficiary);
		transition.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == scenario.WolfHoundId &&
			fact.Type == FactionFactType.Agent &&
			fact.Faction == Faction.Werewolf &&
			fact.AgentKnowledge == expectedAgentKnowledge);
		transition.Facts.Select(fact => fact.EffectiveBoundary)
			.Distinct()
			.Should().ContainSingle();
	}

	private sealed record KnownWolfHoundScenario(
		GameTestBuilder Builder,
		Guid WolfHoundId,
		Guid SimpleWerewolfId,
		IReadOnlyList<Guid> VillagerIds);
}
