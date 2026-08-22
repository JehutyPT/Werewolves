using System.Text.Json.Nodes;
using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class RoleKnowledgeContractTests : DiagnosticTestBase
{
    public RoleKnowledgeContractTests(ITestOutputHelper output) : base(output) { }

	public static TheoryData<MainRoleType, FactionAgentKnowledge?>
		RoleIdentificationWerewolfFactionAgencyEntailmentCases => new()
		{
			{ MainRoleType.SimpleWerewolf, FactionAgentKnowledge.KnownAgent },
			{ MainRoleType.BigBadWolf, FactionAgentKnowledge.KnownAgent },
			{ MainRoleType.AccursedWolfFather, FactionAgentKnowledge.KnownAgent },
			{ MainRoleType.WhiteWerewolf, FactionAgentKnowledge.KnownAgent },
			{ MainRoleType.SimpleVillager, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.VillagerVillager, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Seer, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Cupid, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Witch, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Hunter, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.LittleGirl, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Defender, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Elder, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Scapegoat, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.VillageIdiot, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.TwoSisters, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.ThreeBrothers, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Fox, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.BearTamer, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.StutteringJudge, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.KnightWithRustySword, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Actor, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Piper, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Angel, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.PrejudicedManipulator, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.Gypsy, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.WildChild, FactionAgentKnowledge.KnownNonAgent },
			{ MainRoleType.WolfHound, null },
			{ MainRoleType.Thief, null },
			{ MainRoleType.DevotedServant, null }
		};

    [Fact]
    public void LiveGameSession_FromRoleComposition_LeavesEveryPlayerRoleFactUnknown()
    {
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();

        var session = builder.GetGameState()!;
        session.RoleInPlayCount(MainRoleType.SimpleWerewolf).Should().Be(1);
        session.RoleInPlayCount(MainRoleType.Seer).Should().Be(1);
        session.RoleInPlayCount(MainRoleType.SimpleVillager).Should().Be(3);

        foreach (var player in session.GetPlayers())
        {
            player.State.CurrentRole.Should().BeNull();
            player.State.PhysicalCharacterCardRole.Should().BeNull();
            player.State.ModeratorKnownRole.Should().BeNull();
            player.State.PubliclyRevealedRole.Should().BeNull();
        }

        MarkTestCompleted();
    }

    [Fact]
    public void RoleIdentification_RecordsPrivateCurrentRoleWithoutAssigningOrRevealingCard()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToArray();
        var werewolfAgent = players[0];
        var holder = players[1];
        var werewolfVictim = players[4];
        builder.CompleteWerewolfNightAction(
            [werewolfAgent.Id],
            werewolfVictim.Id);

        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        identification.Semantic.Should()
            .Be(ModeratorInstructionSemantic.IdentifyRoleHolders);
        identification.RoleIdentification.Should().Be(MainRoleType.Seer);

        var result = builder.Process(identification.CreateResponse([holder.Id]));

        result.IsSuccess.Should().BeTrue();
        holder.State.CurrentRole.Should().Be(MainRoleType.Seer);
        holder.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
        holder.State.PhysicalCharacterCardRole.Should().BeNull();
        holder.State.PubliclyRevealedRole.Should().BeNull();
        session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
            .Should().ContainSingle(entry =>
                entry.Role == MainRoleType.Seer &&
                entry.PlayerIds.SetEquals(new[] { holder.Id }));
        session.GameHistoryLog.OfType<AssignRoleLogEntry>().Should().BeEmpty();

        MarkTestCompleted();
    }

	[Theory]
	[MemberData(nameof(RoleIdentificationWerewolfFactionAgencyEntailmentCases))]
	public void RoleIdentification_CompleteRuleTable_ProjectsExpectedWerewolfFactionAgency(
		MainRoleType role,
		FactionAgentKnowledge? entailedKnowledge)
	{
		var builder = CreateBuilder()
			.WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var holder = session.GetPlayers().ElementAt(2);
		session.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should().Be(
			FactionAgentKnowledge.Unknown);

		builder.ArrangeKnownRole(holder.Id, role);

		session.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should().Be(
			entailedKnowledge ?? FactionAgentKnowledge.Unknown);
		MarkTestCompleted();
	}

	[Fact]
	public void RoleIdentification_WerewolfRole_UsesNonInitialAgencyProvenanceWithoutNarrowing()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();

		builder.ArrangeKnownRole(players[1].Id, MainRoleType.BigBadWolf);

		session.GetFactionAgentKnowledge(players[1].Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		var provenance = builder.GameService.GetEarliestWerewolfAgencyFact(
			builder.GameId,
			players[1].Id);
		provenance.Should().NotBeNull();
		provenance!.Source.Kind.Should().Be(
			FactionFactSourceKind.ScheduledObservation);
		provenance.Source.Identifier.Should().Be(
			"role-identification-werewolf-faction-agency-entailment");
		provenance.Source.Identifier.Should().NotBe(
			FactionFactSource.WerewolfFactionAgentGroupObservationIdentifier);
		builder.GameService.GetPossibleRoles(builder.GameId, players[1].Id).Should()
			.Contain(MainRoleType.SimpleWerewolf)
			.And.Contain(MainRoleType.SimpleVillager);
		MarkTestCompleted();
	}

	[Fact]
	public void RoleIdentifications_CompleteWerewolfAgentPartition_ClosureUsesIdentificationBoundaryWithoutObservation()
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.TwoSisters),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.TwoSisters),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.BearTamer),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Defender),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.BigBadWolf)
		};
		var roleLockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Thief", "Sister A", "Sister B", "Bear Tamer", "Defender"],
			roleLockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var players = session.GetPlayers().ToArray();
		var roleHolders = new Dictionary<MainRoleType, HashSet<Guid>>
		{
			[MainRoleType.Thief] = [players[0].Id],
			[MainRoleType.TwoSisters] = [players[1].Id, players[2].Id],
			[MainRoleType.BearTamer] = [players[3].Id],
			[MainRoleType.Defender] = [players[4].Id]
		};
		var seenSemantics = new List<ModeratorInstructionSemantic>();

		var nightStart = service.ProcessInstruction(
			start.GameGuid,
			start.CreateResponse()).ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		service.ProcessInstruction(start.GameGuid, nightStart.CreateResponse());
		ConfirmationInstruction? collectiveWake = null;
		for (var step = 0; step < 40 && collectiveWake == null; step++)
		{
			var instruction = service.GetCurrentInstruction(start.GameGuid)
				?? throw new InvalidOperationException(
					"The Night 1 workflow requires a Pending Instruction.");
			seenSemantics.Add(instruction.Semantic);
			if (instruction is ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.WakeRole,
					AffectedPlayerIds: [var affectedPlayerId]
				} wake && affectedPlayerId == players[0].Id &&
				session.GetPlayers().All(player =>
					session.GetFactionAgentKnowledge(
						player.Id,
						Faction.Werewolf) != FactionAgentKnowledge.Unknown))
			{
				collectiveWake = wake;
				break;
			}

			switch (instruction)
			{
				case SelectPlayersInstruction
				{
					RoleIdentification: { } role
				} identification:
					service.ProcessInstruction(
						start.GameGuid,
						identification.CreateResponse(roleHolders[role]));
					break;
				case SelectOptionsInstruction
				{
					Semantic: ModeratorInstructionSemantic
							.ChooseThiefOffer
				} offer:
					service.ProcessInstruction(
						start.GameGuid,
						offer.CreateResponse(ThiefOfferOptionIds.Offer1));
					break;
				case SelectPlayersInstruction selection:
					var selectedPlayerIds = selection.CountConstraint.IsOptional
						? []
						: selection.SelectablePlayerIds
							.Take(selection.CountConstraint.Minimum)
							.ToHashSet();
					service.ProcessInstruction(
						start.GameGuid,
						selection.CreateResponse(selectedPlayerIds));
					break;
				case ConfirmationInstruction confirmation:
					service.ProcessInstruction(
						start.GameGuid,
						confirmation.CreateResponse());
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected Night 1 instruction '{instruction.Semantic}'.");
			}
		}

		collectiveWake.Should().NotBeNull();
		seenSemantics.Should().NotContain(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var afterWake = service.ProcessInstruction(
			start.GameGuid,
			collectiveWake!.CreateResponse());
		afterWake.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>()
			.Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWerewolfVictim);

		var finalWerewolfAgentIdentificationBoundary = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry => entry.Source.Identifier == FactionFactSource
				.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier)
			.SelectMany(entry => entry.Facts)
			.Single(fact => fact.PlayerId == players[4].Id)
			.EffectiveBoundary;
		var closure = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry => entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		closure.Facts.Should().NotBeEmpty();
		closure.Facts.Should().OnlyContain(fact =>
			fact.EffectiveBoundary == finalWerewolfAgentIdentificationBoundary);
		session.RequireKnownFactionBeneficiary(players[0].Id).Should().Be(
			Faction.Werewolf);
		players.Skip(1).Should().OnlyContain(player =>
			session.RequireKnownFactionBeneficiary(player.Id) == Faction.Villager);

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(session.Serialize());
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var recoveredResidualBeneficiaries = players
			.Skip(1)
			.Select(player => recovered.GetFactionBeneficiaryKnowledge(player.Id))
			.ToArray();
		recovered.GetFactionBeneficiaryKnowledge(players[0].Id).Should().Be(
			session.GetFactionBeneficiaryKnowledge(players[0].Id));
		var recoveredPreClosure = recoveredResidualBeneficiaries.All(
			beneficiary => !beneficiary.IsKnown);
		var recoveredPostClosure = recoveredResidualBeneficiaries.All(
			beneficiary => beneficiary.IsKnown);
		(recoveredPreClosure || recoveredPostClosure).Should().BeTrue();
		if (recoveredPreClosure)
		{
			var recoveredWake = recoveredService.GetCurrentInstruction(recoveredId)
				.Should().BeOfType<ConfirmationInstruction>().Subject;
			recoveredWake.Semantic.Should().Be(
				ModeratorInstructionSemantic.WakeRole);
			recoveredService.ProcessInstruction(
				recoveredId,
				recoveredWake.CreateResponse()).IsSuccess.Should().BeTrue();
		}
		foreach (var player in players)
		{
			recovered.GetFactionBeneficiaryKnowledge(player.Id).Should().Be(
				session.GetFactionBeneficiaryKnowledge(player.Id));
		}
		recovered.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry => entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		MarkTestCompleted();
	}

    [Fact]
    public void RoleIdentification_HardAlignedVillagerRole_CommitsKnownWerewolfFactionNonAgent()
    {
        var builder = CreateBuilder()
            .WithPlayers(7)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Cupid,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var session = builder.GetGameState()!;
        var holder = session.GetPlayers().ElementAt(1);
        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        identification.RoleIdentification.Should().Be(MainRoleType.Cupid);
        session.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should().Be(
            FactionAgentKnowledge.Unknown);

        var result = builder.Process(identification.CreateResponse([holder.Id]));

        result.IsSuccess.Should().BeTrue();
        session.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should().Be(
            FactionAgentKnowledge.KnownNonAgent);
		var history = session.GameHistoryLog.ToList();
		var identificationEntry = history.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Cupid &&
				entry.PlayerIds.SetEquals(new[] { holder.Id }))
			.Subject;
		var entailedBatch = history.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry => entry.Source.Identifier ==
				FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier)
			.Subject;
		entailedBatch.Source.Kind.Should().Be(
			FactionFactSourceKind.ScheduledObservation);
		var entailedFact = entailedBatch.Facts.Should().ContainSingle().Subject;
		entailedFact.Should().Match<FactionFact>(fact =>
			fact.PlayerId == holder.Id &&
			fact.Type == FactionFactType.Agent &&
			fact.Faction == Faction.Werewolf &&
			fact.AgentKnowledge == FactionAgentKnowledge.KnownNonAgent);
		entailedFact.EffectiveBoundary.TurnNumber.Should().Be(
			identificationEntry.TurnNumber);
		entailedFact.EffectiveBoundary.Phase.Should().Be(
			identificationEntry.CurrentPhase);
		entailedFact.EffectiveBoundary.Order.Should().BeGreaterThan(
			history.IndexOf(identificationEntry));
		builder.GameService.GetPossibleRoles(builder.GameId, holder.Id).Should()
			.NotContain(role => role.EstablishesInitialWerewolfAgency());

		var serialized = session.Serialize();
		var payload = JsonNode.Parse(serialized)!.AsObject();
		payload[nameof(GameSessionDto.RoleFactSchemaVersion)]!.GetValue<int>()
			.Should().Be(RoleFactSchema.CurrentVersion);
		payload[nameof(GameSessionDto.FactionFactSchemaVersion)]!.GetValue<int>()
			.Should().Be(FactionFactSchema.CurrentVersion);
		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(serialized);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		recovered.GetPlayerState(holder.Id).ModeratorKnownRole.Should().Be(
			MainRoleType.Cupid);
		recovered.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.KnownNonAgent);
        MarkTestCompleted();
    }

    [Fact]
    public void RoleIdentification_AcceptedObservation_RehydratesAtExactNextInstruction()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolfAgent = players[0];
        var holder = players[1];
        var werewolfVictim = players[4];
        var target = players[2];
        builder.CompleteWerewolfNightAction(
            [werewolfAgent.Id],
            werewolfVictim.Id);

        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        identification.Semantic.Should()
            .Be(ModeratorInstructionSemantic.IdentifyRoleHolders);
        identification.RoleIdentification.Should().Be(MainRoleType.Seer);
        var acceptedIdentification = identification.CreateResponse([holder.Id]);
        var afterIdentification = builder.Process(acceptedIdentification);
        var expectedNext = afterIdentification.ModeratorInstruction
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)
            .Should().BeOfType<SelectPlayersInstruction>().Subject;

        recovered.GetPlayerState(holder.Id).CurrentRole.Should().Be(MainRoleType.Seer);
        recovered.GetPlayerState(holder.Id).ModeratorKnownRole.Should().Be(MainRoleType.Seer);
        recovered.GetPlayerState(holder.Id).PubliclyRevealedRole.Should().BeNull();
        recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
            .ContainSingle(entry =>
                entry.Role == MainRoleType.Seer &&
                entry.PlayerIds.SetEquals(new[] { holder.Id }));
        recoveredNext.InstructionId.Should().Be(expectedNext.InstructionId);
        recoveredNext.SelectablePlayerIds.Should().BeEquivalentTo(expectedNext.SelectablePlayerIds);
        recoveredNext.AffectedPlayerIds.Should().Equal(expectedNext.AffectedPlayerIds);

        Action replayAcceptedIdentification = () =>
            recoveredService.ProcessInstruction(recoveredId, acceptedIdentification);
        replayAcceptedIdentification.Should().Throw<InvalidOperationException>();
        recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should().ContainSingle();

        var continued = recoveredService.ProcessInstruction(
            recoveredId,
            recoveredNext.CreateResponse([target.Id]));

        continued.IsSuccess.Should().BeTrue();
        continued.ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>();
        recovered.GameHistoryLog.OfType<NightActionLogEntry>().Should()
            .ContainSingle(entry =>
                entry.ActionType == NightActionType.SeerCheck &&
                entry.TargetIds!.SequenceEqual(new[] { target.Id }));

        MarkTestCompleted();
    }

    [Fact]
    public void VillagerVillager_AfterPhysicalDeal_RecordsOnePublicHolderBeforeNightPlay()
    {
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.VillagerVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        var afterStart = builder.ConfirmGameStart();

        var observation = InstructionAssert
            .ExpectSuccessWithType<SelectPlayersInstruction>(afterStart);
        observation.Semantic.Should()
            .Be(ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal);
        observation.CountConstraint.Should().Be(NumberRangeConstraint.Single);
        observation.SelectablePlayerIds.Should()
            .BeEquivalentTo(builder.GetGameState()!.GetPlayers().Select(player => player.Id));
		observation.AffectedPlayerIds.Should()
			.BeEquivalentTo(observation.SelectablePlayerIds);

        var holder = builder.GetGameState()!.GetPlayers().ElementAt(2);
        var accepted = builder.Process(observation.CreateResponse([holder.Id]));

        accepted.IsSuccess.Should().BeTrue();
        accepted.ModeratorInstruction!.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
        holder.State.CurrentRole.Should().Be(MainRoleType.VillagerVillager);
        holder.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.VillagerVillager);
        holder.State.ModeratorKnownRole.Should().Be(MainRoleType.VillagerVillager);
        holder.State.PubliclyRevealedRole.Should().Be(MainRoleType.VillagerVillager);
        builder.GetGameState()!.GameHistoryLog
            .OfType<VillagerVillagerPublicFromDealLogEntry>()
            .Should().ContainSingle(entry => entry.PlayerId == holder.Id);

        MarkTestCompleted();
    }

	[Fact]
	public void VillagerVillager_AfterThiefSwapAndElimination_OffersOnlyLivingPossibleHolder()
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.VillagerVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			cards,
			cards.Take(5).Select(card => card.Id),
			cards[5].Id,
			cards[6].Id);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Thief", "Werewolf", "Seer", "Villager", "Victim"],
			lockIn));
		var builder = GameTestBuilder.ForExistingGame(service, start.GameGuid);
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var thief = players[0];
		var werewolf = players[1];
		var seer = players[2];
		var victim = players[4];

		var nightStart = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(start.CreateResponse()));
		var identifyThief = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(nightStart.CreateResponse()));
		var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			builder.Process(identifyThief.CreateResponse([thief.Id])));
		var sleepThief = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(choice.CreateResponse(ThiefOfferOptionIds.Offer1)));
		builder.Process(sleepThief.CreateResponse());
		builder.CompleteWerewolfNightAction([werewolf.Id], victim.Id);
		var afterSeer = builder.CompleteSeerNightAction(seer.Id, werewolf.Id);
		var finishNight = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterSeer);
		builder.Process(finishNight.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[victim.Id] = MainRoleType.SimpleVillager
		});
		var afterDay = builder.CompleteDayPhaseWithTie();

		var observation = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			afterDay);

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal);
		observation.SelectablePlayerIds.Should().Equal(thief.Id);
		observation.AffectedPlayerIds.Should().Equal(thief.Id);
		victim.State.Health.Should().Be(PlayerHealth.Dead);
		seer.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		var historyCountBeforeRejectedSubmission =
			builder.GetGameState()!.GameHistoryLog.Count();
		Action submitExcludedPlayer = () => builder.Process(new ModeratorResponse
		{
			InstructionId = observation.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = new HashSet<Guid> { seer.Id }
		});

		submitExcludedPlayer.Should().Throw<InvalidOperationException>()
			.WithMessage("*payload is not valid*");
		builder.GetGameState()!.GameHistoryLog.Should()
			.HaveCount(historyCountBeforeRejectedSubmission);
		var stillPending = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		stillPending.InstructionId.Should().Be(observation.InstructionId);
		stillPending.SelectablePlayerIds.Should().Equal(thief.Id);

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredPending = recoveredService.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredPending.InstructionId.Should().Be(observation.InstructionId);
		recoveredPending.SelectablePlayerIds.Should().Equal(thief.Id);
		recoveredPending.AffectedPlayerIds.Should().Equal(thief.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void VillagerVillager_WithNoLivingPossibleHolder_FailsAsAnInvariant()
	{
		var builder = CreateBuilder()
			.WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		foreach (var player in builder.GetGameState()!.GetPlayers())
		{
			builder.ArrangeEliminatedPlayer(player.Id);
		}

		Action confirmPhysicalDeal = () => builder.ConfirmGameStart();

		confirmPhysicalDeal.Should().Throw<ArgumentException>()
			.WithMessage("*SelectablePlayerIds cannot be empty*");
		MarkTestCompleted();
	}

    [Fact]
    public void VillagerVillager_AcceptedPublicFromDealObservation_RehydratesExactlyOnceAtNextInstruction()
    {
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Seer,
                MainRoleType.VillagerVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);

        builder.StartGame();
        var observation = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            builder.ConfirmGameStart());
        var holder = builder.GetGameState()!.GetPlayers().ElementAt(2);
        var afterObservation = builder.Process(observation.CreateResponse([holder.Id]));
        var expectedNextInstruction = afterObservation.ModeratorInstruction!;

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredHolder = recovered.GetPlayer(holder.Id);

        recoveredHolder.State.CurrentRole.Should().Be(MainRoleType.VillagerVillager);
		recoveredHolder.State.PhysicalCharacterCardId.Should().NotBeNull();
        recoveredHolder.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.VillagerVillager);
        recoveredHolder.State.ModeratorKnownRole.Should().Be(MainRoleType.VillagerVillager);
        recoveredHolder.State.PubliclyRevealedRole.Should().Be(MainRoleType.VillagerVillager);
		var recoveredPhysicalCard = recovered.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id ==
				recoveredHolder.State.PhysicalCharacterCardId);
		recoveredPhysicalCard.Zone.Should().Be(
			PhysicalCharacterCardZone.PlayerOwned);
		recoveredPhysicalCard.OwnerPlayerId.Should().Be(holder.Id);
        recovered.GameHistoryLog.OfType<VillagerVillagerPublicFromDealLogEntry>()
            .Should().ContainSingle(entry => entry.PlayerId == holder.Id);
        var recoveredInstruction = recoveredService.GetCurrentInstruction(recoveredId)!;
        recoveredInstruction.GetType().Should().Be(expectedNextInstruction.GetType());
        recoveredInstruction.InstructionId.Should().Be(expectedNextInstruction.InstructionId);
        recoveredInstruction.PublicAnnouncement.Should().Be(expectedNextInstruction.PublicAnnouncement);
        recoveredInstruction.PrivateInstruction.Should().Be(expectedNextInstruction.PrivateInstruction);

        MarkTestCompleted();
    }

	[Fact]
	public void GenericRoleReveal_TwoUnknownVictimsExposeTheirOwnConstrainedRoleMultisets()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialAgent = players[0];
		var whiteWerewolf = players[1];
		var firstNightVictim = players[4];
		var ordinaryUnknown = players[5];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [initialAgent.Id, whiteWerewolf.Id],
			WerewolfVictimId = firstNightVictim.Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new()
		{
			[firstNightVictim.Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();

		var whiteWake = builder.CompleteWerewolfNightAction(
			[initialAgent.Id, whiteWerewolf.Id],
			ordinaryUnknown.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var whiteTarget = builder.Process(whiteWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var whiteSleep = builder.Process(
			whiteTarget.CreateResponse([initialAgent.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(whiteSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		var reveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.SelectableRolesForPlayers.Keys.Should().BeEquivalentTo(
			[initialAgent.Id, ordinaryUnknown.Id]);
		reveal.SelectableRolesForPlayers[initialAgent.Id].Should()
			.Equal(MainRoleType.SimpleWerewolf);
		reveal.SelectableRolesForPlayers[ordinaryUnknown.Id].Should()
			.Contain(MainRoleType.Hunter)
			.And.Contain(MainRoleType.SimpleVillager)
			.And.NotContain(role => role.EstablishesInitialWerewolfAgency());
		reveal.PlayersForAssignment.Should().Equal(ordinaryUnknown.Id);

		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void GenericRoleReveal_SingletonReservationsPropagateAndPreserveExtraCopies(
		bool hasExtraWerewolfCopy)
	{
		var roles = hasExtraWerewolfCopy
			? new[]
			{
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.BigBadWolf,
				MainRoleType.Witch
			}
			: new[]
			{
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.BigBadWolf,
				MainRoleType.Witch
			};
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(roles);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialAgent = players[0];
		var collectiveVictim = players[1];
		var additionalVictim = players[2];
		var bigBadWolf = players[3];
		var witch = players[4];
		builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
		builder.ArrangeKnownRole(witch.Id, MainRoleType.Witch);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[initialAgent.Id, bigBadWolf.Id],
			collectiveVictim.Id);
		var witchWake = builder.CompleteBigBadWolfNightAction(
				bigBadWolf.Id,
				additionalVictim.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var healing = builder.Process(witchWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(healing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var witchSleep = builder.Process(
				poison.CreateResponse([initialAgent.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			initialAgent.Id,
			collectiveVictim.Id,
			additionalVictim.Id,
			bigBadWolf.Id);
		var finishNight = builder.Process(witchSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		var reveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.SelectableRolesForPlayers.Keys.Should().BeEquivalentTo([
			initialAgent.Id,
			collectiveVictim.Id,
			additionalVictim.Id
		]);
		reveal.SelectableRolesForPlayers[initialAgent.Id].Should()
			.OnlyContain(role => role == MainRoleType.SimpleWerewolf);
		if (hasExtraWerewolfCopy)
		{
			reveal.SelectableRolesForPlayers[collectiveVictim.Id].Should()
				.BeEquivalentTo([
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager
				]);
			reveal.SelectableRolesForPlayers[additionalVictim.Id].Should()
				.BeEquivalentTo([
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager
				]);
			reveal.PlayersForAssignment.Should().BeEquivalentTo([
				collectiveVictim.Id,
				additionalVictim.Id
			]);
		}
		else
		{
			reveal.SelectableRolesForPlayers[collectiveVictim.Id].Should()
				.OnlyContain(role => role == MainRoleType.SimpleVillager);
			reveal.SelectableRolesForPlayers[additionalVictim.Id].Should()
				.OnlyContain(role => role == MainRoleType.SimpleVillager);
			new[]
			{
				reveal.SelectableRolesForPlayers[collectiveVictim.Id].Count,
				reveal.SelectableRolesForPlayers[additionalVictim.Id].Count
			}.Should().BeEquivalentTo([1, 2]);
			reveal.PlayersForAssignment.Should().BeEmpty();
		}

		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_JointlyAmbiguousMappingsRejectInvalidBatchesAndAllowLegalRetry()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.Hunter,
				MainRoleType.VillageIdiot,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var firstVictim = players[2];
		var secondVictim = players[3];
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.SimpleWerewolf);
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.BigBadWolf);
		foreach (var player in players.Skip(4))
		{
			builder.ArrangeKnownRole(player.Id, MainRoleType.SimpleVillager);
		}
		builder.ConfirmGameStart();

		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, players[1].Id],
			WerewolfVictimId = firstVictim.Id,
			BigBadWolfId = players[1].Id,
			BigBadWolfTargetId = secondVictim.Id
		});
		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		var expectedOptions = new[]
		{
			MainRoleType.Hunter,
			MainRoleType.VillageIdiot
		};
		reveal.SelectableRolesForPlayers[firstVictim.Id].Should()
			.BeEquivalentTo(expectedOptions);
		reveal.SelectableRolesForPlayers[secondVictim.Id].Should()
			.BeEquivalentTo(expectedOptions);
		reveal.PlayersForAssignment.Should().BeEquivalentTo([
			firstVictim.Id,
			secondVictim.Id
		]);
		var beforeInvalidResponses = PublicGameSessionSnapshot.Capture(builder);
		var incompleteResponse = new ModeratorResponse
		{
			InstructionId = reveal.InstructionId,
			Type = ExpectedInputType.AssignPlayerRoles,
			AssignedPlayerRoles = new Dictionary<Guid, MainRoleType>
			{
				[firstVictim.Id] = MainRoleType.Hunter
			}
		};

		Action submitIncomplete = () => builder.Process(incompleteResponse);

		submitIncomplete.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(builder).Should().BeEquivalentTo(
			beforeInvalidResponses,
			options => options.WithStrictOrdering());
		var overallocatedResponse = reveal.CreateResponse(new()
		{
			[firstVictim.Id] = MainRoleType.Hunter,
			[secondVictim.Id] = MainRoleType.Hunter
		});

		Action submitOverallocated = () => builder.Process(overallocatedResponse);

		submitOverallocated.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(builder).Should().BeEquivalentTo(
			beforeInvalidResponses,
			options => options.WithStrictOrdering());

		var accepted = builder.Process(reveal.CreateResponse(new()
		{
			[firstVictim.Id] = MainRoleType.Hunter,
			[secondVictim.Id] = MainRoleType.VillageIdiot
		}));

		accepted.IsSuccess.Should().BeTrue();
		firstVictim.State.PubliclyRevealedRole.Should().Be(MainRoleType.Hunter);
		secondVictim.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.VillageIdiot);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_WhenRoleIsPrivatelyKnown_AcknowledgesAndCommitsPublicReveal()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var seer = players[1];
        var otherVillager = players[2];

        builder.CompleteNightPhase(new NightActionInputs
        {
            WerewolfIds = [werewolf.Id],
            WerewolfVictimId = seer.Id,
            SeerId = seer.Id,
            SeerTargetId = otherVillager.Id
        });

		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		reveal.AffectedPlayerIds.Should().Equal(seer.Id);
		reveal.PrivateInstruction.Should().Contain(
			MainRoleType.Seer.GetPublicName());
		seer.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		seer.State.PubliclyRevealedRole.Should().BeNull();
		var acceptedReveal = reveal.CreateResponse();

		var afterReveal = builder.Process(acceptedReveal);

        afterReveal.IsSuccess.Should().BeTrue();
        seer.State.CurrentRole.Should().Be(MainRoleType.Seer);
        seer.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
        seer.State.PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
		seer.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.Seer);
		seer.State.PhysicalCharacterCardId.Should().NotBeNull();
		builder.GetGameState()!.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == seer.State.PhysicalCharacterCardId)
			.Should().Match<PhysicalCharacterCardState>(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == seer.Id);
		var revealEntry = builder.GetGameState()!.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle().Subject;
		revealEntry.RevealedRoles.Should()
			.Contain(new KeyValuePair<Guid, MainRoleType>(seer.Id, MainRoleType.Seer));
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Seer &&
				entry.PlayerIds.SetEquals(new[] { seer.Id }));
		builder.GetGameState()!.GameHistoryLog
			.OfType<AssignRoleLogEntry>()
			.Should().NotContain(entry => entry.PlayerIds.Contains(seer.Id));

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)!;

        recovered.GetPlayerState(seer.Id).PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();
        recoveredNext.GetType().Should().Be(afterReveal.ModeratorInstruction!.GetType());
        recoveredNext.InstructionId.Should().Be(afterReveal.ModeratorInstruction.InstructionId);
		Action replayAcceptedReveal = () =>
			recoveredService.ProcessInstruction(recoveredId, acceptedReveal);
        replayAcceptedReveal.Should().Throw<InvalidOperationException>();
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();

        MarkTestCompleted();
    }

	[Fact]
	public void GenericRoleReveal_VoteVictimIdentifiedEarlier_AcknowledgesAndBindsCard()
	{
		var builder = CreateBuilder()
			.WithSimpleGame(playerCount: 6, werewolfCount: 1, includeSeer: true);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var seer = players[1];
		var dawnVictim = players[2];
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [werewolf.Id],
			WerewolfVictimId = dawnVictim.Id,
			SeerId = seer.Id,
			SeerTargetId = players[3].Id
		});
		builder.CompleteDawnPhase(new()
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var reveal = builder.Process(vote.CreateResponse([seer.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		reveal.AffectedPlayerIds.Should().Equal(seer.Id);
		reveal.PrivateInstruction.Should().Contain(
			MainRoleType.Seer.GetPublicName());
		seer.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		seer.State.PhysicalCharacterCardId.Should().BeNull();
		seer.State.PubliclyRevealedRole.Should().BeNull();

		var afterReveal = builder.Process(reveal.CreateResponse());

		afterReveal.IsSuccess.Should().BeTrue();
		seer.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.Seer);
		seer.State.PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.ContainsKey(seer.Id) &&
				entry.RevealedRoles[seer.Id] == MainRoleType.Seer);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_WithOneOfTwoWerewolvesEstablished_OffersOneUnclaimedCopy()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.SimpleWerewolf);
		builder.ConfirmGameStart();

		builder.CompleteNightPhase(
			[players[0].Id, players[1].Id],
			players[2].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[2].Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var reveal = builder.Process(vote.CreateResponse([players[1].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		reveal.PlayersForAssignment.Should().BeEmpty();
		reveal.SelectableRolesForPlayers[players[1].Id].Should()
			.ContainSingle(role => role == MainRoleType.SimpleWerewolf);

		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_WithBothWerewolfCopiesEstablished_OffersNoWerewolfCopy()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.SimpleWerewolf);
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.SimpleWerewolf);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(
			[players[0].Id, players[1].Id],
			players[2].Id);

		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		reveal.SelectableRolesForPlayers[players[2].Id].Should()
			.NotContain(MainRoleType.SimpleWerewolf);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_RepeatedCopiesOfOneRoleRequireConfirmationAndCommit()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var victim = players[2];
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.SimpleWerewolf);
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.SimpleWerewolf);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(
			[players[0].Id, players[1].Id],
			victim.Id);

		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		reveal.SelectableRolesForPlayers[victim.Id].Should().OnlyContain(
			role => role == MainRoleType.SimpleVillager);
		reveal.SelectableRolesForPlayers[victim.Id].Should().HaveCount(4);
		reveal.PlayersForAssignment.Should().BeEmpty();
		reveal.PrivateInstruction.Should().ContainAll(
			victim.Name,
			MainRoleType.SimpleVillager.GetPublicName());
		var confirmation = reveal.CreateResponse([]);

		confirmation.Type.Should().Be(ExpectedInputType.Continue);
		confirmation.AssignedPlayerRoles.Should().BeNull();
		var accepted = builder.Process(confirmation);

		accepted.IsSuccess.Should().BeTrue();
		victim.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		victim.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_StaleOverAllocationIsRejectedBeforeLegalMappingCommits()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var unestablishedAgent = players[0];
		var accursedWolfFather = players[1];
		var victim = players[2];
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [unestablishedAgent.Id, accursedWolfFather.Id],
			WerewolfVictimId = victim.Id,
			AccursedWolfFatherId = accursedWolfFather.Id,
			AccursedWolfFatherInfectsVictim = true
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = builder.Process(vote.CreateResponse([victim.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		reveal.SelectableRolesForPlayers[victim.Id].Should()
			.Contain(MainRoleType.SimpleWerewolf)
			.And.Contain(MainRoleType.SimpleVillager);

		builder.ArrangeKnownRole(
			unestablishedAgent.Id,
			MainRoleType.SimpleWerewolf);
		var session = builder.GetGameState()!;
		var historyBefore = session.GameHistoryLog.ToArray();
		var cardsBefore = session.GetModeratorPhysicalCharacterCards().ToArray();
		var victimBefore = (
			victim.State.CurrentRole,
			victim.State.ModeratorKnownRole,
			victim.State.PubliclyRevealedRole,
			victim.State.PhysicalCharacterCardId);
		var staleResponse = reveal.CreateResponse(new()
		{
			[victim.Id] = MainRoleType.SimpleWerewolf
		});

		Action submitStaleResponse = () => builder.Process(staleResponse);

		submitStaleResponse.Should().Throw<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			reveal.InstructionId);
		session.GameHistoryLog.Should().Equal(historyBefore);
		session.GetModeratorPhysicalCharacterCards().Should().Equal(cardsBefore);
		(
			victim.State.CurrentRole,
			victim.State.ModeratorKnownRole,
			victim.State.PubliclyRevealedRole,
			victim.State.PhysicalCharacterCardId).Should().Be(victimBefore);

		var legalResult = builder.Process(reveal.CreateResponse(new()
		{
			[victim.Id] = MainRoleType.SimpleVillager
		}));

		legalResult.IsSuccess.Should().BeTrue();
		victim.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		victim.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_InitialObservedAgentConfirmsEntailedAgencyRoleAndRejectsMappingPayload()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialAgent = players[0];
		var dawnVictim = players[1];
		builder.CompleteNightPhase([initialAgent.Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new()
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});

		builder.GameService.GetPossibleRoles(builder.GameId, initialAgent.Id)
			.Should().Equal(MainRoleType.SimpleWerewolf);
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = builder.Process(vote.CreateResponse([initialAgent.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		reveal.PlayersForAssignment.Should().BeEmpty();
		reveal.SelectableRolesForPlayers[initialAgent.Id].Should()
			.Equal(MainRoleType.SimpleWerewolf);
		AssertRoleMappingRejectedWithoutMutation(
			builder,
			reveal,
			initialAgent.Id,
			MainRoleType.SimpleVillager);

		var legalResult = builder.Process(reveal.CreateResponse([]));

		legalResult.IsSuccess.Should().BeTrue();
		initialAgent.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleWerewolf);
		MarkTestCompleted();
	}

	[Fact]
	public void SimulationStartStateAgent_UsesSameInitialAgencyConstraintAsLiveObservation()
	{
		MainRoleType[] roles =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(roles.Length, roles);
		var capability = SimulatorCapability.SafetyScreening;
		var material = new RunSeedMaterial(
			capability.CreateCompatibilityIdentity(scenario),
			capability.HeadlessResponsePolicy.StrategyIdentity,
			runNumber: 270);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			capability);
		var initialAgentSeat = startState.RoleAssignments.Single(assignment =>
			assignment.Role.EstablishesInitialWerewolfAgency()).SeatNumber;
		var config = startState.CreateGameSessionConfig();
		var service = new GameService();
		var start = service.StartNewSimulationGame(
			config,
			startState.FactionFacts);
		var initialAgentId = config.PlayerRoster[initialAgentSeat - 1].Id;

		service.GetPossibleRoles(start.GameGuid, initialAgentId)
			.Should().Equal(MainRoleType.SimpleWerewolf);
		var provenance = service.GetEarliestWerewolfAgencyFact(
			start.GameGuid,
			initialAgentId);
		provenance.Should().NotBeNull();
		provenance!.Source.Kind.Should().Be(
			FactionFactSourceKind.SimulationStartState);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_KnownNonAgentRejectsInitialAgencyRoleThenAcceptsVillagerRole()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.VillageIdiot,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialAgent = players[0];
		var dawnVictim = players[1];
		var knownNonAgent = players[2];
		builder.CompleteNightPhase([initialAgent.Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new()
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});

		var possibleRoles = builder.GameService.GetPossibleRoles(
			builder.GameId,
			knownNonAgent.Id);
		possibleRoles.Should().Contain(MainRoleType.SimpleVillager);
		possibleRoles.Should().NotContain(role =>
			role.EstablishesInitialWerewolfAgency());
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var reveal = builder.Process(vote.CreateResponse([knownNonAgent.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		AssertRoleMappingRejectedWithoutMutation(
			builder,
			reveal,
			knownNonAgent.Id,
			MainRoleType.SimpleWerewolf);

		var legalResult = builder.Process(reveal.CreateResponse(new()
		{
			[knownNonAgent.Id] = MainRoleType.SimpleVillager
		}));

		legalResult.IsSuccess.Should().BeTrue();
		knownNonAgent.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		MarkTestCompleted();
	}

	[Fact]
	public void WerewolfAlignedWolfHound_EarliestAgencyFactIsAlignmentChoice()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.WolfHound,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification = builder.ConfirmNightStart()
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wolfHound = players[0];
		var simpleWerewolf = players[1];
		var alignment = builder.Process(
			identification.CreateResponse([wolfHound.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = builder.Process(alignment.CreateResponse(
			WolfHoundAlignmentOptionIds.Werewolves))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var observation = builder.Process(sleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		builder.Process(observation.CreateResponse([
			wolfHound.Id,
			simpleWerewolf.Id
		]));

		var provenance = builder.GameService.GetEarliestWerewolfAgencyFact(
			builder.GameId,
			wolfHound.Id);
		provenance.Should().NotBeNull();

		provenance!.Source.Kind.Should().Be(
			FactionFactSourceKind.ExplicitTransition);
		provenance.Fact.AgentKnowledge.Should().Be(
			FactionAgentKnowledge.KnownAgent);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_MixedDawnBatchAcknowledgesKnownAndConfirmsEntailedUnknownTogether()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var knownVictim = players[2];
		var unknownVictim = players[3];

		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, players[1].Id],
			WerewolfVictimId = knownVictim.Id,
			SeerId = knownVictim.Id,
			SeerTargetId = players[4].Id,
			BigBadWolfId = players[1].Id,
			BigBadWolfTargetId = unknownVictim.Id
		});

		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		reveal.AffectedPlayerIds.Should().BeEquivalentTo(new[]
		{
			knownVictim.Id,
			unknownVictim.Id
		});
		reveal.PlayersForAssignment.Should().BeEmpty();
		reveal.PrivateInstruction.Should().ContainAll(
			MainRoleType.Seer.GetPublicName(),
			unknownVictim.Name,
			MainRoleType.SimpleVillager.GetPublicName());
		reveal.SelectableRolesForPlayers[unknownVictim.Id]
			.Should().OnlyContain(role => role == MainRoleType.SimpleVillager);
		reveal.SelectableRolesForPlayers[unknownVictim.Id]
			.Should().NotContain(MainRoleType.BigBadWolf);

		var afterReveal = builder.Process(reveal.CreateResponse([]));

		afterReveal.IsSuccess.Should().BeTrue();
		knownVictim.State.PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
		knownVictim.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.Seer);
		unknownVictim.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		unknownVictim.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		knownVictim.State.Health.Should().Be(PlayerHealth.Dead);
		unknownVictim.State.Health.Should().Be(PlayerHealth.Dead);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_MixedKnownSingletonAndMultiBatchCommitsOneExchange()
	{
		var builder = CreateBuilder()
			.WithPlayers(9)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.Hunter,
				MainRoleType.VillageIdiot,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var initialAgent = players[0];
		var whiteWerewolf = players[1];
		var bigBadWolf = players[2];
		var knownVictim = players[3];
		var ordinaryUnknown = players[5];
		var firstNightCollectiveVictim = players[7];
		var firstNightBigBadWolfVictim = players[8];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownRole(
			bigBadWolf.Id,
			MainRoleType.BigBadWolf);
		builder.ArrangeKnownRole(
			knownVictim.Id,
			MainRoleType.Hunter);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [
				initialAgent.Id,
				whiteWerewolf.Id,
				bigBadWolf.Id
			],
			WerewolfVictimId = firstNightCollectiveVictim.Id,
			BigBadWolfId = bigBadWolf.Id,
			BigBadWolfTargetId = firstNightBigBadWolfVictim.Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new()
		{
			[firstNightCollectiveVictim.Id] = MainRoleType.SimpleVillager,
			[firstNightBigBadWolfVictim.Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();

		var whiteWake = builder.CompleteWerewolfNightActionSubsequentNight(
			ordinaryUnknown.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var whiteTarget = builder.Process(whiteWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var whiteSleep = builder.Process(
			whiteTarget.CreateResponse([initialAgent.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		builder.Process(whiteSleep.CreateResponse());
		var finishNight = builder.CompleteBigBadWolfNightAction(
			bigBadWolf.Id,
			knownVictim.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		var reveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.AffectedPlayerIds.Should().BeEquivalentTo([
			ordinaryUnknown.Id,
			initialAgent.Id,
			knownVictim.Id
		]);
		reveal.SelectableRolesForPlayers.Keys.Should().BeEquivalentTo([
			ordinaryUnknown.Id,
			initialAgent.Id
		]);
		reveal.SelectableRolesForPlayers[initialAgent.Id].Should()
			.Equal(MainRoleType.SimpleWerewolf);
		reveal.SelectableRolesForPlayers[ordinaryUnknown.Id].Should()
			.Contain(MainRoleType.VillageIdiot)
			.And.Contain(MainRoleType.SimpleVillager);
		reveal.PlayersForAssignment.Should().Equal(ordinaryUnknown.Id);
		reveal.PrivateInstruction.Should().ContainAll(
			knownVictim.Name,
			MainRoleType.Hunter.GetPublicName(),
			initialAgent.Name,
			MainRoleType.SimpleWerewolf.GetPublicName());

		var response = reveal.CreateResponse(new()
		{
			[ordinaryUnknown.Id] = MainRoleType.VillageIdiot
		});

		response.AssignedPlayerRoles.Should().ContainSingle()
			.Which.Should().Be(
				new KeyValuePair<Guid, MainRoleType>(
					ordinaryUnknown.Id,
					MainRoleType.VillageIdiot));
		var accepted = builder.Process(response);

		accepted.IsSuccess.Should().BeTrue();
		initialAgent.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleWerewolf);
		ordinaryUnknown.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.VillageIdiot);
		knownVictim.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.Hunter);
		MarkTestCompleted();
	}

	[Fact]
	public void GenericRoleReveal_InfectedUnknownVoteVictimKeepsExactRoleOptionsUnrestricted()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var infectedVictim = players[2];
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, players[1].Id],
			WerewolfVictimId = infectedVictim.Id,
			AccursedWolfFatherId = players[1].Id,
			AccursedWolfFatherInfectsVictim = true
		});
		infectedVictim.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		infectedVictim.State.CurrentRole.Should().BeNull();
		var possibleRoles = builder.GameService.GetPossibleRoles(
			builder.GameId,
			infectedVictim.Id);
		possibleRoles.Should().Contain(MainRoleType.SimpleWerewolf);
		possibleRoles.Should().Contain(MainRoleType.SimpleVillager);
		possibleRoles.Should().NotContain(MainRoleType.AccursedWolfFather);
		var provenance = builder.GameService.GetEarliestWerewolfAgencyFact(
			builder.GameId,
			infectedVictim.Id);
		provenance.Should().NotBeNull();
		provenance!.Source.Kind.Should().Be(
			FactionFactSourceKind.ExplicitTransition);
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var reveal = builder.Process(vote.CreateResponse([infectedVictim.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.SelectableRolesForPlayers[infectedVictim.Id]
			.Should().Contain(MainRoleType.SimpleWerewolf);
		reveal.SelectableRolesForPlayers[infectedVictim.Id]
			.Should().Contain(MainRoleType.SimpleVillager);
		reveal.SelectableRolesForPlayers[infectedVictim.Id].Should().NotContain(
			MainRoleType.AccursedWolfFather);

		var afterReveal = builder.Process(reveal.CreateResponse(new()
		{
			[infectedVictim.Id] = MainRoleType.SimpleVillager
		}));

		afterReveal.IsSuccess.Should().BeTrue();
		infectedVictim.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		infectedVictim.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void RoleIdentification_AfterInfection_PreservesKnownWerewolfFactionAgent()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var infectedVictim = players[2];
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, players[1].Id],
			WerewolfVictimId = infectedVictim.Id,
			AccursedWolfFatherId = players[1].Id,
			AccursedWolfFatherInfectsVictim = true
		});
		infectedVictim.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		infectedVictim.State.CurrentRole.Should().BeNull();
		var possibleRoles = builder.GameService.GetPossibleRoles(
			builder.GameId,
			infectedVictim.Id);
		possibleRoles.Should().Contain(MainRoleType.SimpleWerewolf);
		possibleRoles.Should().Contain(MainRoleType.SimpleVillager);
		possibleRoles.Should().NotContain(MainRoleType.AccursedWolfFather);
		var provenance = builder.GameService.GetEarliestWerewolfAgencyFact(
			builder.GameId,
			infectedVictim.Id);
		provenance.Should().NotBeNull();
		provenance!.Source.Kind.Should().Be(
			FactionFactSourceKind.ExplicitTransition);
		builder.ArrangeKnownRole(
			infectedVictim.Id,
			MainRoleType.SimpleVillager);

		infectedVictim.State.GetFactionAgentKnowledge(Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.KnownAgent);
		builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Identifier == FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier &&
				entry.Facts.Any(fact => fact.PlayerId == infectedVictim.Id));
		infectedVictim.State.ModeratorKnownRole.Should().Be(
			MainRoleType.SimpleVillager);
		infectedVictim.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		MarkTestCompleted();
	}

    [Fact]
	public void GenericRoleReveal_UnknownDawnVictim_BindsDealPoolCardAndRecordsInitialCurrentRole()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        builder.StartGame();
        builder.ConfirmGameStart();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var seer = players[1];
        var unknownVictim = players[2];

        builder.CompleteNightPhase(new NightActionInputs
        {
            WerewolfIds = [werewolf.Id],
            WerewolfVictimId = unknownVictim.Id,
            SeerId = seer.Id,
            SeerTargetId = players[3].Id
        });

		var reveal = builder.GetCurrentInstruction()
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		reveal.PlayersForAssignment.Should().BeEmpty();
		reveal.SelectableRolesForPlayers[unknownVictim.Id].Should()
			.OnlyContain(role => role == MainRoleType.SimpleVillager);
        reveal.AffectedPlayerIds.Should().Equal(unknownVictim.Id);
		var matchingDealPoolCardIds = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards()
			.Where(state =>
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.Card.PrintedRole == MainRoleType.SimpleVillager)
			.Select(state => state.Card.Id)
			.ToHashSet();
		matchingDealPoolCardIds.Should().NotBeEmpty();
        unknownVictim.State.CurrentRole.Should().BeNull();
		unknownVictim.State.ModeratorKnownRole.Should().BeNull();
		unknownVictim.State.PhysicalCharacterCardId.Should().BeNull();
        unknownVictim.State.PubliclyRevealedRole.Should().BeNull();
        unknownVictim.State.Health.Should().Be(PlayerHealth.Alive);
        builder.GetGameState()!.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
            .Should().NotContain(entry => entry.PlayerId == unknownVictim.Id);

		var acceptedReveal = reveal.CreateResponse([]);
		var afterReveal = builder.Process(acceptedReveal);

		afterReveal.IsSuccess.Should().BeTrue();
		unknownVictim.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		unknownVictim.State.ModeratorKnownRole.Should().Be(MainRoleType.SimpleVillager);
        unknownVictim.State.PubliclyRevealedRole.Should().Be(MainRoleType.SimpleVillager);
		unknownVictim.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		unknownVictim.State.PhysicalCharacterCardId.Should().NotBeNull();
		var ownedCardId = unknownVictim.State.PhysicalCharacterCardId!.Value;
		matchingDealPoolCardIds.Should().Contain(ownedCardId);
		builder.GetGameState()!.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == ownedCardId)
			.Should().Be(new PhysicalCharacterCardState(
				new PhysicalCharacterCard(
					ownedCardId,
					MainRoleType.SimpleVillager),
				PhysicalCharacterCardZone.PlayerOwned,
				unknownVictim.Id));
        unknownVictim.State.Health.Should().Be(PlayerHealth.Dead);

        var history = builder.GetGameState()!.GameHistoryLog.ToList();
		var ownershipIndex = history.FindIndex(entry =>
			entry is PhysicalCharacterCardOwnershipObservedLogEntry ownership &&
			ownership.PlayerId == unknownVictim.Id &&
			ownership.CardId == ownedCardId);
		var identificationIndex = history.FindIndex(entry =>
			entry is RoleIdentificationLogEntry identification &&
			identification.Role == MainRoleType.SimpleVillager &&
			identification.PlayerIds.Contains(unknownVictim.Id));
		var revealIndex = history.FindIndex(entry => entry is RoleRevealLogEntry);
		var eliminationIndex = history.FindIndex(entry =>
                entry is PlayerEliminatedLogEntry eliminated &&
				eliminated.PlayerId == unknownVictim.Id);
		history.OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == unknownVictim.Id &&
				entry.CardId == ownedCardId);
		ownershipIndex.Should().BeLessThan(identificationIndex);
		identificationIndex.Should().BeLessThan(revealIndex);
		revealIndex.Should().BeLessThan(eliminationIndex);
		history.OfType<AssignRoleLogEntry>().Should().NotContain(entry =>
			entry.PlayerIds.Contains(unknownVictim.Id));
		history.OfType<RoleIdentificationLogEntry>().Should().ContainSingle(entry =>
			entry.Role == MainRoleType.SimpleVillager &&
			entry.PlayerIds.SetEquals(new[] { unknownVictim.Id }));

        var recoveredService = new GameService();
        var recoveredId = recoveredService.RehydrateSession(builder.GetGameState()!.Serialize());
        var recovered = recoveredService.GetGameStateView(recoveredId)!;
        var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)!;

		var recoveredVictim = recovered.GetPlayerState(unknownVictim.Id);
		recoveredVictim.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredVictim.ModeratorKnownRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredVictim.PhysicalCharacterCardId.Should().Be(ownedCardId);
		recoveredVictim.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		recoveredVictim.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		recoveredVictim.Health.Should().Be(PlayerHealth.Dead);
		recovered.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == ownedCardId)
			.Should().Match<PhysicalCharacterCardState>(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == unknownVictim.Id);
		recovered.GameHistoryLog
			.OfType<PhysicalCharacterCardOwnershipObservedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == unknownVictim.Id &&
				entry.CardId == ownedCardId);
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();
        recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>().Should()
            .ContainSingle(entry => entry.PlayerId == unknownVictim.Id);
        recoveredNext.GetType().Should().Be(afterReveal.ModeratorInstruction!.GetType());
        recoveredNext.InstructionId.Should().Be(afterReveal.ModeratorInstruction.InstructionId);
        Action replayAcceptedReveal = () => recoveredService.ProcessInstruction(
            recoveredId,
            acceptedReveal);
        replayAcceptedReveal.Should().Throw<InvalidOperationException>();
        recovered.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().ContainSingle();
        recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>().Should()
            .ContainSingle(entry => entry.PlayerId == unknownVictim.Id);

		MarkTestCompleted();
	}

	private static void AssertRoleMappingRejectedWithoutMutation(
		GameTestBuilder builder,
		AssignRolesInstruction reveal,
		Guid playerId,
		MainRoleType rejectedRole)
	{
		var session = builder.GetGameState()!;
		var player = session.GetPlayer(playerId);
		var historyBefore = session.GameHistoryLog.ToArray();
		var cardsBefore = session.GetModeratorPhysicalCharacterCards().ToArray();
		var playerBefore = (
			player.State.CurrentRole,
			player.State.ModeratorKnownRole,
			player.State.PubliclyRevealedRole,
			player.State.PhysicalCharacterCardId);

		Action submitContradictoryMapping = () => builder.Process(
			new ModeratorResponse
			{
				InstructionId = reveal.InstructionId,
				Type = ExpectedInputType.AssignPlayerRoles,
				AssignedPlayerRoles = new Dictionary<Guid, MainRoleType>
				{
					[playerId] = rejectedRole
				}
			});

		submitContradictoryMapping.Should().Throw<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			reveal.InstructionId);
		session.GameHistoryLog.Should().Equal(historyBefore);
		session.GetModeratorPhysicalCharacterCards().Should().Equal(cardsBefore);
		(
			player.State.CurrentRole,
			player.State.ModeratorKnownRole,
			player.State.PubliclyRevealedRole,
			player.State.PhysicalCharacterCardId).Should().Be(playerBefore);
	}
}
