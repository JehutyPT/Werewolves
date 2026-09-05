using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedHunterElderTests
{
	private const string ForcedDescendantReactionId =
		"actor-reactive-forced-descendant";
	private static readonly PhysicalCharacterCard HunterCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000146"),
		MainRoleType.Hunter);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000147"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000148"),
		MainRoleType.Fox);
	private static readonly PhysicalCharacterCard ElderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000149"),
		MainRoleType.Elder);
	[Fact]
	public void BorrowedElder_FirstCollectiveAttackResistsSilentlyAndContinuesWithoutSourceLeak()
	{
		var fixture = CreateElderActorSession(preActivate: false);
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var gameStart = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				gameStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		nightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var builder = GameTestBuilder.ForExistingGame(service, gameId);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var werewolfObservation = builder
			.CompleteActorNightAction(fixture.ActorId, ElderCard.Id)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		werewolfObservation.RoleIdentification.Should().BeNull();
		werewolfObservation.CountConstraint.Should().Be(
			NumberRangeConstraint.Exact(1));
		werewolfObservation.SelectablePlayerIds.Should().BeEquivalentTo(
			fixture.Session.GetPlayers()
				.Select(player => player.Id)
				.Where(playerId => playerId != fixture.ActorId));
		var victimSelection = service.ProcessInstruction(
				gameId,
				werewolfObservation.CreateResponse([fixture.WerewolfId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = service.ProcessInstruction(
				gameId,
				victimSelection.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		werewolfSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var finishNight = service.ProcessInstruction(
				gameId,
				werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);

		IGameSession beforeResolution = service.GetGameStateView(gameId)!;
		var historyCountBeforeResolution =
			beforeResolution.GameHistoryLog.Count();
		beforeResolution.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		var activation = beforeResolution
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.ActingPlayerId.Should().Be(fixture.ActorId);
		activation.SourceRole.Should().Be(MainRoleType.Elder);

		var independentFlow = service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse());

		var dayDebate = independentFlow.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		dayDebate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		IGameSession resolved = service.GetGameStateView(gameId)!;
		resolved.GetCurrentPhase().Should().Be(GamePhase.Day);
		var actor = resolved.GetPlayerState(fixture.ActorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		var resolutionHistory = resolved.GameHistoryLog
			.Skip(historyCountBeforeResolution)
			.ToArray();
		resolutionHistory.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		resolutionHistory.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		resolutionHistory.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.ActorId &&
				entry.EffectType == StatusEffectTypes.ElderProtectionLost);
		resolutionHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		AssertActorSafeElderText(
			string.Join(
				"\n",
				resolutionHistory.Select(entry => entry.ToString())) +
			"\n" + dayDebate.PublicAnnouncement +
			"\n" + dayDebate.PrivateInstruction,
			activation.ActivationId);
	}

	[Fact]
	public void BorrowedElder_CollectiveInfectionBypassesDefenderAndConsumesResistanceSilently()
	{
		var fixture = CreateElderActorInfectionSession();
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var gameStart = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				gameStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		nightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var builder = GameTestBuilder.ForExistingGame(service, gameId);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var werewolfObservation = builder
			.CompleteActorNightAction(fixture.ActorId, ElderCard.Id)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var victimSelection = service.ProcessInstruction(
				gameId,
				werewolfObservation.CreateResponse(
					[fixture.WerewolfId, fixture.WolfFatherId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = service.ProcessInstruction(
				gameId,
				victimSelection.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		werewolfSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var wolfFatherWake = service.ProcessInstruction(
				gameId,
				werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		wolfFatherWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		wolfFatherWake.AffectedPlayerIds.Should().Equal(
			fixture.WolfFatherId);
		var infectionChoice = service.ProcessInstruction(
				gameId,
				wolfFatherWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		infectionChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection);
		var wolfFatherSleep = service.ProcessInstruction(
				gameId,
				infectionChoice.CreateResponse(
					AccursedWolfFatherInfectionOptionIds.Infect))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		wolfFatherSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		wolfFatherSleep.AffectedPlayerIds.Should().Equal(
			fixture.WolfFatherId);
		var finishNight = service.ProcessInstruction(
				gameId,
				wolfFatherSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);

		IGameSession beforeResolution = service.GetGameStateView(gameId)!;
		var historyCountBeforeResolution =
			beforeResolution.GameHistoryLog.Count();
		beforeResolution.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.DefenderProtect &&
				entry.TargetIds!.SequenceEqual(new[] { fixture.ActorId }));
		beforeResolution.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection &&
				entry.TargetIds!.SequenceEqual(new[] { fixture.ActorId }));
		beforeResolution.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.SourceRole == MainRoleType.AccursedWolfFather &&
				entry.ActionType ==
					NightActionType.AccursedWolfFatherInfection &&
				entry.TargetIds!.SequenceEqual(new[] { fixture.ActorId }));
		beforeResolution.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		var activation = beforeResolution
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.ActingPlayerId.Should().Be(fixture.ActorId);
		activation.SourceRole.Should().Be(MainRoleType.Elder);

		var independentFlow = service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse());

		var dayDebate = independentFlow.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		dayDebate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		IGameSession resolved = service.GetGameStateView(gameId)!;
		resolved.GetCurrentPhase().Should().Be(GamePhase.Day);
		var actor = resolved.GetPlayerState(fixture.ActorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeFalse();
		actor.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		var resolutionHistory = resolved.GameHistoryLog
			.Skip(historyCountBeforeResolution)
			.ToArray();
		resolutionHistory.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		resolutionHistory.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		resolutionHistory.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Identifier == "accursed-wolf-father-infection");
		resolutionHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		AssertActorSafeElderText(
			string.Join(
				"\n",
				resolutionHistory.Select(entry => entry.ToString())) +
			"\n" + dayDebate.PublicAnnouncement +
			"\n" + dayDebate.PrivateInstruction,
				activation.ActivationId);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BorrowedElder_PublicWitchRestorationLetsSameActivationResistAgainAndRecoversAfterLaterDeath(
		bool replaceCollectiveWithInfection)
	{
		var fixture = CreateElderActorWitchRestorationSession();
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var gameStart = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				gameStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		nightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var builder = GameTestBuilder.ForExistingGame(service, gameId);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var werewolfObservation = builder
			.CompleteActorNightAction(fixture.ActorId, ElderCard.Id)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var victimSelection = service.ProcessInstruction(
				gameId,
				werewolfObservation.CreateResponse(
					[fixture.WerewolfId, fixture.WolfFatherId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = service.ProcessInstruction(
				gameId,
				victimSelection.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		werewolfSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var wolfFatherWake = service.ProcessInstruction(
				gameId,
				werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		wolfFatherWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		wolfFatherWake.AffectedPlayerIds.Should().Equal(
			fixture.WolfFatherId);
		var infectionChoice = service.ProcessInstruction(
				gameId,
				wolfFatherWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		infectionChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection);
		var wolfFatherSleep = service.ProcessInstruction(
				gameId,
				infectionChoice.CreateResponse(
					replaceCollectiveWithInfection
						? AccursedWolfFatherInfectionOptionIds.Infect
						: AccursedWolfFatherInfectionOptionIds.Decline))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		wolfFatherSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		wolfFatherSleep.AffectedPlayerIds.Should().Equal(
			fixture.WolfFatherId);
		var witchWake = service.ProcessInstruction(
				gameId,
				wolfFatherSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		witchWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		witchWake.AffectedPlayerIds.Should().Equal(fixture.WitchId);
		var healingTarget = service.ProcessInstruction(
				gameId,
				witchWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		healingTarget.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchHealingTarget);
		healingTarget.SelectablePlayerIds.Should().Contain(fixture.ActorId);
		var poisonTarget = service.ProcessInstruction(
				gameId,
				healingTarget.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		poisonTarget.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		var witchSleep = service.ProcessInstruction(
				gameId,
				poisonTarget.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		witchSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		witchSleep.AffectedPlayerIds.Should().Equal(fixture.WitchId);
		var finishNight = service.ProcessInstruction(
				gameId,
				witchSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);

		IGameSession beforeResolution = service.GetGameStateView(gameId)!;
		var historyCountBeforeResolution =
			beforeResolution.GameHistoryLog.Count();
		beforeResolution.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		var activation = beforeResolution
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.ActingPlayerId.Should().Be(fixture.ActorId);
		activation.SourceRole.Should().Be(MainRoleType.Elder);

		var independentFlow = service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse());

		var dayDebate = independentFlow.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		dayDebate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		IGameSession resolved = service.GetGameStateView(gameId)!;
		var actor = resolved.GetPlayerState(fixture.ActorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeFalse();
		actor.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		var resolutionHistory = resolved.GameHistoryLog
			.Skip(historyCountBeforeResolution)
			.ToArray();
		resolutionHistory.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		resolutionHistory.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		resolutionHistory.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolved.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeEquivalentTo(activation);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			resolved.Serialize());
		IGameSession recovered = recoveredService.GetGameStateView(
			recoveredGameId)!;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeEquivalentTo(activation);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		recovered.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.SourceRole == MainRoleType.Witch &&
				entry.ActionType == NightActionType.WitchSave &&
				entry.TargetIds!.SequenceEqual(new[] { fixture.ActorId }));
		AssertActorSafeElderText(
			string.Join(
				"\n",
				recovered.GameHistoryLog.Select(entry => entry.ToString())) +
			"\n" + dayDebate.PublicAnnouncement +
			"\n" + dayDebate.PrivateInstruction,
			activation.ActivationId);

		var recoveredDayDebate = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredDayDebate.Should().BeEquivalentTo(dayDebate);
		var dayVote = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredDayDebate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		dayVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var nextNightStart = recoveredService.ProcessInstruction(
				recoveredGameId,
				dayVote.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		nextNightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var historical = (GameSession)recoveredService.GetGameStateView(
			recoveredGameId)!;
		historical.GetCurrentPhase().Should().Be(GamePhase.Night);
		historical.GetModeratorRemainingActorSetupCards()
			.Select(card => card.PrintedRole)
			.Should().BeEquivalentTo(
				new[] { MainRoleType.Seer, MainRoleType.Fox });
		historical.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeEquivalentTo(activation);
		historical.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		historical.TransitionMainPhase(GamePhase.Dawn);
		var historyCountBeforeRestoredResistance =
			historical.GameHistoryLog.Count();

		NightInteractionResolver.ResolveNightPhase(historical);

		var restoredResistanceHistory = historical.GameHistoryLog
			.Skip(historyCountBeforeRestoredResistance)
			.ToArray();
		historical.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		historical.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeEquivalentTo(activation);
		historical.GetActorBorrowedElderResistanceCommits()
			.Select(commit => commit.PowerIdentity.PowerInstanceId)
			.Should().Equal(
				activation.ActivationId,
				activation.ActivationId);
		restoredResistanceHistory
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		restoredResistanceHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		restoredResistanceHistory.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		historical.TransitionMainPhase(GamePhase.Day);
		historical.TransitionMainPhase(GamePhase.Night);
		// #144 owns production GameService admission of Actor. At this genuine
		// pre-admission Night boundary, invoke the same production expiry
		// transition that Actor's next opening owns before arranging a later death.
		historical.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		historical.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		historical.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
		historical.EliminatePlayer(
			fixture.ActorId,
			EliminationReason.EventElimination);
		var historicalPayload = RecoveryPayloadTestDriver.Capture(historical)
			.Serialize();
		var historicalService = new GameService();
		var historicalGameId = historicalService.RehydrateSession(
			historicalPayload);
		var historicalNightStart = historicalService
			.GetCurrentInstruction(historicalGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		historicalNightStart.Should().BeEquivalentTo(nextNightStart);
		IGameSession historicalState = historicalService.GetGameStateView(
			historicalGameId)!;
		historicalState.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		historicalState.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Dead);
		historicalState.GetPlayerState(fixture.ActorId)
			.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		historicalState.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
		historicalState.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
		historicalState.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.ActorId &&
				entry.Reason == EliminationReason.EventElimination);
		AssertActorSafeElderText(
			string.Join(
				"\n",
				historicalState.GameHistoryLog.Select(entry => entry.ToString())),
			activation.ActivationId);

		var continued = historicalService.ProcessInstruction(
			historicalGameId,
			historicalNightStart.CreateResponse());

		continued.IsSuccess.Should().BeTrue();
		historicalService.GetGameStateView(historicalGameId)!
			.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
	}

	[Fact]
	public void BorrowedElder_WitchRestorationDoesNotRestoreSpentNativeElderProtection()
	{
		var fixture = CreateElderActorSession();
		var activation = fixture.Session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var nativeElder = fixture.Session.GetPlayers()
			.First(player =>
				player.Id != fixture.ActorId &&
				player.Id != fixture.WerewolfId);
		var witch = fixture.Session.GetPlayers()
			.First(player =>
				player.Id != fixture.ActorId &&
				player.Id != fixture.WerewolfId &&
				player.Id != nativeElder.Id);
		fixture.Session.AssignRole(nativeElder.Id, MainRoleType.Elder);
		fixture.Session.AssignRole(witch.Id, MainRoleType.Witch);
		fixture.Session.ApplyStatusEffect(
			StatusEffectTypes.ElderProtectionLost,
			nativeElder.Id);
		var elderRole = new ElderRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.CommitOneUseRolePowerNightAction(
			NightActionType.WitchSave,
			fixture.ActorId,
			new OneUseRolePowerResourceIdentity(
				witch.Id,
				MainRoleType.Witch,
				"witch-potions",
				witch.Id,
				RolePowerInstanceOrigin.Native,
				Guid.Parse("00000000-0000-0000-0000-000000000150")));
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		NightInteractionResolver.ResolveNightPhase(fixture.Session);
		fixture.Session.GetActorBorrowedElderResistanceCommits()
			.Should().ContainSingle().Which.RestoringWitchSaveLogIndex
			.Should().NotBeNull();

		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.Session.TransitionMainPhase(GamePhase.Night);
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);

		elderRole.TryResolveResistance(
				fixture.Session,
				fixture.Session.GetPlayer(fixture.ActorId),
				allowBorrowedActor: true,
				out var restoredBorrowedResistance)
			.Should().BeTrue();
		restoredBorrowedResistance.PowerIdentity.PowerInstanceId.Should().Be(
			activation.ActivationId);
		elderRole.TryResolveResistance(
				fixture.Session,
				nativeElder,
				allowBorrowedActor: false,
				out _)
			.Should().BeFalse();
		nativeElder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
	}

	[Theory]
	[InlineData(
		MainRoleType.WhiteWerewolf,
		NightActionType.WhiteWerewolfVictimSelection,
		ModeratorInstructionSemantic.SelectWhiteWerewolfTarget)]
	[InlineData(
		MainRoleType.BigBadWolf,
		NightActionType.BigBadWolfVictimSelection,
		ModeratorInstructionSemantic.SelectBigBadWolfTarget)]
	public void BorrowedElder_AdditionalWerewolfPhysicalAttacksConsumeResistanceSilently(
		MainRoleType attackerRole,
		NightActionType attackType,
		ModeratorInstructionSemantic targetSemantic)
	{
		var fixture = CreateElderActorAdditionalAttackSession(attackerRole);
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var gameStart = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				gameStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		nightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var builder = GameTestBuilder.ForExistingGame(service, gameId);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var werewolfWake = builder
			.CompleteActorNightAction(fixture.ActorId, ElderCard.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		werewolfWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		var victimSelection = service.ProcessInstruction(
				gameId,
				werewolfWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = service.ProcessInstruction(
				gameId,
				victimSelection.CreateResponse([fixture.CollectiveTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		werewolfSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var attackerWake = service.ProcessInstruction(
				gameId,
				werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		attackerWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		attackerWake.AffectedPlayerIds.Should().Equal(fixture.AttackerId);
		var targetSelection = service.ProcessInstruction(
				gameId,
				attackerWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		targetSelection.Semantic.Should().Be(targetSemantic);
		targetSelection.SelectablePlayerIds.Should().Contain(fixture.ActorId);
		var attackerSleep = service.ProcessInstruction(
				gameId,
				targetSelection.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		attackerSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		attackerSleep.AffectedPlayerIds.Should().Equal(fixture.AttackerId);
		var finishNight = service.ProcessInstruction(
				gameId,
				attackerSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);

		IGameSession beforeResolution = service.GetGameStateView(gameId)!;
		var historyCountBeforeResolution =
			beforeResolution.GameHistoryLog.Count();
		beforeResolution.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == attackType &&
				entry.TargetIds!.SequenceEqual(new[] { fixture.ActorId }));
		beforeResolution.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		var activation = beforeResolution
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.ActingPlayerId.Should().Be(fixture.ActorId);
		activation.SourceRole.Should().Be(MainRoleType.Elder);

		var independentFlow = service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse());

		var dayDebate = independentFlow.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		dayDebate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		IGameSession resolved = service.GetGameStateView(gameId)!;
		var actor = resolved.GetPlayerState(fixture.ActorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		var resolutionHistory = resolved.GameHistoryLog
			.Skip(historyCountBeforeResolution)
			.ToArray();
		resolutionHistory.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		resolutionHistory.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		resolutionHistory.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			resolved.Serialize());
		IGameSession recovered = recoveredService.GetGameStateView(
			recoveredGameId)!;
		recovered.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		AssertActorSafeElderText(
			string.Join(
				"\n",
				resolutionHistory.Select(entry => entry.ToString())) +
			"\n" + dayDebate.PublicAnnouncement +
			"\n" + dayDebate.PrivateInstruction,
				activation.ActivationId);
	}

	[Fact]
	public void BorrowedElder_UnrestoredSameActivationAvailabilityProbe_DeniesSecondResistanceAndDeterminesNormalPhysicalVictim()
	{
		var fixture = CreateElderActorSession();
		var activation = fixture.Session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var elderRole = new ElderRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var firstTriggerLogIndex = fixture.Session.GameHistoryLog.Count();
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		elderRole.TryResolveResistance(
				fixture.Session,
				fixture.Session.GetPlayer(fixture.ActorId),
				allowBorrowedActor: true,
				out var firstResistance)
			.Should().BeTrue();
		fixture.Session.CommitActorBorrowedElderResistance(
			firstResistance.PowerIdentity,
			fixture.ActorId,
			firstTriggerLogIndex);

		// Availability/attack-resolution probe only: a second public attack in
		// this ActivationId is unreachable because Actor expires before the next
		// wolf opening. The test-owned phase boundary preserves the identity and
		// asks the real Dawn resolver for the ordinary unrestored consequence.
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.Session.TransitionMainPhase(GamePhase.Night);
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		fixture.Session.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeEquivalentTo(activation);
		elderRole.TryResolveResistance(
				fixture.Session,
				fixture.Session.GetPlayer(fixture.ActorId),
				allowBorrowedActor: true,
				out _)
			.Should().BeFalse();
		var historyCountBeforeResolution = fixture.Session.GameHistoryLog.Count();

		NightInteractionResolver.ResolveNightPhase(fixture.Session);

		var resolutionHistory = fixture.Session.GameHistoryLog
			.Skip(historyCountBeforeResolution)
			.ToArray();
		fixture.Session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Which.ToString().Should().Be(
				"ActorBorrowedRolePowerCommitted");
		resolutionHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.ActorId &&
				entry.Reason == EliminationReason.WerewolfAttack);
		AssertActorSafeElderText(
			string.Join(
				"\n",
				resolutionHistory.Select(entry => entry.ToString())),
			activation.ActivationId);
	}

	[Fact]
	public void BorrowedElder_DistinctLegitimateFreshActivationIdentityIsNotBlockedByAnotherSessionsResistanceHistory()
	{
		var elderRole = new ElderRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var spentFixture = CreateElderActorSession();
		var spentActivation = spentFixture.Session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		var triggeringLogIndex = spentFixture.Session.GameHistoryLog.Count();
		spentFixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			spentFixture.ActorId);
		spentFixture.Session.TransitionMainPhase(GamePhase.Dawn);
		elderRole.TryResolveResistance(
				spentFixture.Session,
				spentFixture.Session.GetPlayer(spentFixture.ActorId),
				allowBorrowedActor: true,
				out var spentResistance)
			.Should().BeTrue();
		spentFixture.Session.CommitActorBorrowedElderResistance(
			spentResistance.PowerIdentity,
			spentFixture.ActorId,
			triggeringLogIndex);
		elderRole.TryResolveResistance(
				spentFixture.Session,
				spentFixture.Session.GetPlayer(spentFixture.ActorId),
				allowBorrowedActor: true,
				out _)
			.Should().BeFalse();

		// Actor has only one legitimate Elder setup card per game. A second
		// test-owned session supplies a distinct, fully committed activation
		// without reusing or rewriting the first session's spent card.
		var freshFixture = CreateElderActorSession();
		var freshActivation = freshFixture.Session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		freshActivation.ActivationId.Should().NotBe(
			spentActivation.ActivationId);
		elderRole.TryResolveResistance(
				freshFixture.Session,
				freshFixture.Session.GetPlayer(freshFixture.ActorId),
				allowBorrowedActor: true,
				out var freshResistance)
			.Should().BeTrue();
		freshResistance.PowerIdentity.PowerInstanceId.Should().Be(
			freshActivation.ActivationId);
		freshFixture.Session.GetActorBorrowedElderResistanceCommits()
			.Should().BeEmpty();
	}

	[Fact]
	public void BorrowedElder_VillageVote_RevealsActorAndCompletesEarlierCascadeBeforeSuppression()
	{
		var pending = CreatePendingBorrowedElderSuppressionAnnouncement();
		var state = pending.Service.GetGameStateView(pending.GameId)!;
		var history = state.GameHistoryLog.ToArray();
		var cascadeCompletedIndex = Array.FindIndex(
			history,
			entry => entry is EliminationCascadeCompletedLogEntry
			{
				ScopeId: "Day:1:Vote:1"
			});
		var borrowedMarkerIndex = Array.FindIndex(
			history,
			entry => entry is ActorBorrowedRolePowerCommittedLogEntry);
		var suppressionFactIndex = Array.FindIndex(
			history,
			entry => entry is VillagerRolePowerSuppressionCommittedLogEntry);

		cascadeCompletedIndex.Should().BeGreaterThanOrEqualTo(0);
		borrowedMarkerIndex.Should().BeGreaterThan(cascadeCompletedIndex);
		suppressionFactIndex.Should().BeGreaterThan(borrowedMarkerIndex);
		state.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle()
			.Which.ToString().Should().Be("ActorBorrowedRolePowerCommitted");
		state.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				pending.Announcement.InstructionId);
		state.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().BeEmpty();
		pending.Announcement.PublicAnnouncement.Should().Be(
			GameStrings.VillagerRolePowerSuppressionAnnouncement);
		pending.Announcement.PrivateInstruction.Should().BeNull();
		pending.Announcement.AffectedPlayerIds.Should().BeNullOrEmpty();
		AssertActorSafeElderText(
			string.Concat(
				pending.Announcement.PublicAnnouncement,
				"\n",
				pending.Announcement.PrivateInstruction,
				"\n",
				string.Join(
					"\n",
					history.Skip(cascadeCompletedIndex))),
			pending.Fixture.ActivationId);

		var consecutiveVote = pending.Service.ProcessInstruction(
				pending.GameId,
				pending.Announcement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		consecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var resumed = pending.Service.GetGameStateView(pending.GameId)!;
		resumed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		resumed.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle();
		resumed.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				pending.Announcement.InstructionId);
	}

	[Fact]
	public void BorrowedElder_PendingSuppressionAnnouncement_RoundTripsAndAcknowledgesExactlyOnce()
	{
		var pending = CreatePendingBorrowedElderSuppressionAnnouncement();
		var pendingState = pending.Service.GetGameStateView(pending.GameId)!;
		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			pendingState.Serialize());
		var recoveredAnnouncement = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredAnnouncement.InstructionId.Should().Be(
			pending.Announcement.InstructionId);
		recoveredAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		recoveredAnnouncement.PublicAnnouncement.Should().Be(
			GameStrings.VillagerRolePowerSuppressionAnnouncement);
		recoveredAnnouncement.PrivateInstruction.Should().BeNull();
		recoveredAnnouncement.AffectedPlayerIds.Should().BeNullOrEmpty();
		var invalidResponses = new ModeratorResponse[]
		{
			new()
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.Continue
			},
			new()
			{
				InstructionId = recoveredAnnouncement.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid>
				{
					pending.Fixture.ShotTargetId
				}
			},
			new()
			{
				InstructionId = recoveredAnnouncement.InstructionId,
				Type = ExpectedInputType.Continue,
				SelectedPlayerIds = new HashSet<Guid>
				{
					pending.Fixture.ShotTargetId
				}
			}
		};
		foreach (var invalidResponse in invalidResponses)
		{
			var before = recoveredService.GetGameStateView(recoveredGameId)!
				.Serialize();
			var pendingInstructionId = recoveredService
				.GetCurrentInstruction(recoveredGameId)!.InstructionId;
			Action process = () => recoveredService.ProcessInstruction(
				recoveredGameId,
				invalidResponse);

			process.Should().Throw<InvalidOperationException>();
			recoveredService.GetGameStateView(recoveredGameId)!
				.Serialize().Should().Be(before);
			recoveredService.GetCurrentInstruction(recoveredGameId)!
				.InstructionId.Should().Be(pendingInstructionId);
		}

		var consecutiveVote = recoveredService.ProcessInstruction(
				recoveredGameId,
				recoveredAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		consecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var acknowledged = recoveredService.GetGameStateView(recoveredGameId)!;
		acknowledged.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		acknowledged.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle();
		acknowledged.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should().Be(
				recoveredAnnouncement.InstructionId);

		var afterAckService = new GameService();
		var afterAckGameId = afterAckService.RehydrateSession(
			acknowledged.Serialize());
		var restoredConsecutiveVote = afterAckService
			.GetCurrentInstruction(afterAckGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		restoredConsecutiveVote.InstructionId.Should().Be(
			consecutiveVote.InstructionId);
		restoredConsecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		var restored = afterAckService.GetGameStateView(afterAckGameId)!;
		restored.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == "Day:1:Vote:1");
		restored.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		restored.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().ContainSingle();
		restored.GameHistoryLog
			.OfType<
				VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle();
		var beforeStaleAcknowledgment = restored.Serialize();
		Action replayAcknowledgment = () => afterAckService.ProcessInstruction(
			afterAckGameId,
			recoveredAnnouncement.CreateResponse());

		replayAcknowledgment.Should().Throw<InvalidOperationException>();
		afterAckService.GetGameStateView(afterAckGameId)!
			.Serialize().Should().Be(beforeStaleAcknowledgment);
	}

	[Fact]
	public void BorrowedElder_DefenderProtectedCollectiveAttackLeavesResistanceUnspent()
	{
		var fixture = CreateElderActorDefenderSession();
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var gameStart = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var nightStart = service.ProcessInstruction(
				gameId,
				gameStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var builder = GameTestBuilder.ForExistingGame(service, gameId);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var defenderWake = builder
			.CompleteActorNightAction(fixture.ActorId, ElderCard.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		defenderWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		defenderWake.AffectedPlayerIds.Should().Equal(fixture.DefenderId);
		var defenderTarget = service.ProcessInstruction(
				gameId,
				defenderWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		defenderTarget.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectDefenderTarget);
		defenderTarget.SelectablePlayerIds.Should().Contain(fixture.ActorId);
		var defenderSleep = service.ProcessInstruction(
				gameId,
				defenderTarget.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		defenderSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		defenderSleep.AffectedPlayerIds.Should().Equal(fixture.DefenderId);
		var werewolfObservation = service.ProcessInstruction(
				gameId,
				defenderSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var victimSelection = service.ProcessInstruction(
				gameId,
				werewolfObservation.CreateResponse([fixture.WerewolfId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = service.ProcessInstruction(
				gameId,
				victimSelection.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = service.ProcessInstruction(
				gameId,
				werewolfSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);

		IGameSession beforeResolution = service.GetGameStateView(gameId)!;
		var historyBeforeResolution =
			beforeResolution.GameHistoryLog.ToArray();
		var historyCountBeforeResolution = historyBeforeResolution.Length;
		var defenderCommitIndex = Array.FindIndex(
			historyBeforeResolution,
			entry => entry is RecurringRolePowerCommittedLogEntry
			{
				ActionType: NightActionType.DefenderProtect,
				TargetIds: [var protectedPlayerId]
			} && protectedPlayerId == fixture.ActorId);
		var collectiveAttackIndex = Array.FindIndex(
			historyBeforeResolution,
			entry => entry is NightActionLogEntry
			{
				ActionType: NightActionType.WerewolfVictimSelection,
				TargetIds: [var attackedPlayerId]
			} && attackedPlayerId == fixture.ActorId);
		defenderCommitIndex.Should().BeGreaterThanOrEqualTo(0);
		collectiveAttackIndex.Should().BeGreaterThan(defenderCommitIndex);
		beforeResolution.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		var activation = beforeResolution
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.SourceRole.Should().Be(MainRoleType.Elder);

		var independentFlow = service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse());

		var dayDebate = independentFlow.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		dayDebate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		IGameSession resolved = service.GetGameStateView(gameId)!;
		var actor = resolved.GetPlayerState(fixture.ActorId);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.Health.Should().Be(PlayerHealth.Alive);
		actor.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		resolved.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		resolved.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeEquivalentTo(activation);
		var resolutionHistory = resolved.GameHistoryLog
			.Skip(historyCountBeforeResolution)
			.ToArray();
		resolutionHistory.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		resolutionHistory.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == fixture.ActorId);
		AssertActorSafeElderText(
			string.Join(
				"\n",
				resolutionHistory.Select(entry => entry.ToString())) +
			"\n" + dayDebate.PublicAnnouncement +
			"\n" + dayDebate.PrivateInstruction,
			activation.ActivationId);
	}

	[Fact]
	public void BorrowedHunter_NonActorEliminationWavePublishesNoHunterReactionOrSourceIdentifiers()
	{
		var fixture = CreateActiveHunterActorSession();
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var nonActorEliminationAnnouncement =
			AdvanceToDayEliminationAnnouncement(
				service,
				gameId,
				fixture.ShotTargetId);

		var continued = service.ProcessInstruction(
			gameId,
			nonActorEliminationAnnouncement.CreateResponse());

		continued.IsSuccess.Should().BeTrue();
		continued.ModeratorInstruction?.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		var resolved = service.GetGameStateView(gameId)!;
		resolved.GetPlayers().Should().NotContain(player =>
			player.State.CurrentRole == MainRoleType.Hunter);
		resolved.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		resolved.GetPlayerState(fixture.ShotTargetId).Health.Should().Be(
			PlayerHealth.Dead);
		resolved.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.ShotTargetId &&
				entry.Reason == EliminationReason.DayVote);
		AssertActorSafeText(
			string.Join(
				"\n",
				resolved.GameHistoryLog.Select(entry => entry.ToString())) +
			"\n" + continued.ModeratorInstruction?.PublicAnnouncement +
			"\n" + continued.ModeratorInstruction?.PrivateInstruction,
			fixture);
	}

	[Fact]
	public void BorrowedHunter_EliminatedActorFiresAfterForcedReactionWithActorSafeExactOneSelection()
	{
		var fixture = CreateActiveHunterActorSession();
		var forcedReaction = new ForcedDescendantReaction(
			fixture.ActorId,
			fixture.ForcedVictimId);
		var service = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					forcedReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var start = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var debate = service.ProcessInstruction(gameId, start.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var vote = service.ProcessInstruction(gameId, debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var actorReveal = service.ProcessInstruction(
				gameId,
				vote.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		actorReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		actorReveal.AffectedPlayerIds.Should().Equal(fixture.ActorId);
		var revealCopy = string.Concat(
			actorReveal.PublicAnnouncement,
			"\n",
			actorReveal.PrivateInstruction);
		revealCopy.Should().NotContain(GameStrings.HunterRoleName)
			.And.NotContain(MainRoleType.Hunter.ToString())
			.And.NotContain(ForcedDescendantReactionId)
			.And.NotContain(HunterCard.Id.ToString())
			.And.NotContain(fixture.ActivationId.ToString());

		var actorEliminationAnnouncement = service.ProcessInstruction(
				gameId,
				actorReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		actorEliminationAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		IGameSession afterActorElimination = service.GetGameStateView(gameId)!;
		var eliminatedActor = afterActorElimination.GetPlayerState(
			fixture.ActorId);
		eliminatedActor.CurrentRole.Should().Be(MainRoleType.Actor);
		eliminatedActor.PubliclyRevealedRole.Should().Be(MainRoleType.Actor);
		eliminatedActor.PhysicalCharacterCardId.Should().Be(
			fixture.ActorCardId);
		eliminatedActor.Health.Should().Be(PlayerHealth.Dead);

		var forcedReveal = service.ProcessInstruction(
				gameId,
				actorEliminationAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		forcedReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		forcedReveal.AffectedPlayerIds.Should().Equal(
			fixture.ForcedVictimId);

		var finalShot = service.ProcessInstruction(
				gameId,
				forcedReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		finalShot.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		finalShot.AffectedPlayerIds.Should().Equal(fixture.ActorId);
		var afterForcedReaction = service.GetGameStateView(gameId)!;
		afterForcedReaction.GetPlayerState(fixture.ForcedVictimId).Health
			.Should().Be(PlayerHealth.Dead);
		finalShot.SelectablePlayerIds.Should().BeEquivalentTo(
			afterForcedReaction.GetPlayers()
				.Where(player => player.State.Health == PlayerHealth.Alive)
				.Select(player => player.Id));
		afterForcedReaction.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ReactionId == ForcedDescendantReactionId &&
				entry.TriggeringEliminations.Any(elimination =>
					elimination.PlayerId == fixture.ActorId));
		var selectionCopy = string.Concat(
			finalShot.PublicAnnouncement,
			"\n",
			finalShot.PrivateInstruction);
		selectionCopy.Should()
			.Contain(GameStrings.ActorBorrowedHunterFinalShotSelectionInstruction)
			.And.NotContain(GameStrings.HunterRoleName)
			.And.NotContain(MainRoleType.Hunter.ToString())
			.And.NotContain(EliminationCascadeReactionIds.HunterFinalShot)
			.And.NotContain(HunterCard.Id.ToString())
			.And.NotContain(fixture.ActivationId.ToString());
	}

	[Fact]
	public void BorrowedHunter_KnownEmptyTargetSetSkipsSelectorAndAdvancesWithoutSyntheticResponse()
	{
		var fixture = CreateActiveHunterActorSession();
		var eliminateAll = new EliminateAllOtherLivingPlayersReaction(
			fixture.ActorId);
		var service = CreateServiceWithForcedReaction(eliminateAll);
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var actorEliminationAnnouncement =
			AdvanceToDayEliminationAnnouncement(
				service,
				gameId,
				fixture.ActorId);
		var forcedReveal = service.ProcessInstruction(
				gameId,
				actorEliminationAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		forcedReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);

		var independentBoundary = service.ProcessInstruction(
			gameId,
			forcedReveal.CreateResponse());

		independentBoundary.ModeratorInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>();
		independentBoundary.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishedGame);
		service.GetCurrentInstruction(gameId)!.InstructionId.Should().Be(
			independentBoundary.ModeratorInstruction!.InstructionId);
		var completed = service.GetGameStateView(gameId)!;
		completed.GetPlayers().Should().OnlyContain(player =>
			player.State.Health == PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.HunterShot);
	}

	[Theory]
	[InlineData("empty")]
	[InlineData("multiple")]
	[InlineData("self")]
	[InlineData("dead")]
	[InlineData("outside-candidate")]
	public void BorrowedHunter_InvalidExactOneResponse_IsRejectedAtomically(
		string invalidCase)
	{
		var pending = CreatePendingBorrowedHunterFinalShot();
		var additionalTargetId = pending.Selector.SelectablePlayerIds
			.First(playerId => playerId != pending.Fixture.ShotTargetId);
		HashSet<Guid> selectedPlayerIds = invalidCase switch
		{
			"empty" => [],
			"multiple" =>
				[pending.Fixture.ShotTargetId, additionalTargetId],
			"self" => [pending.Fixture.ActorId],
			"dead" => [pending.Fixture.ForcedVictimId],
			"outside-candidate" =>
				[Guid.Parse("00000000-0000-0000-0000-000000000999")],
			_ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
		};
		var invalidResponse = new ModeratorResponse
		{
			InstructionId = pending.Selector.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = selectedPlayerIds
		};
		var before = pending.Service.GetGameStateView(pending.GameId)!;
		var serializedBefore = before.Serialize();
		var historyBefore = before.GameHistoryLog.ToArray();

		Action process = () => pending.Service.ProcessInstruction(
			pending.GameId,
			invalidResponse);

		process.Should().Throw<InvalidOperationException>();
		var after = pending.Service.GetGameStateView(pending.GameId)!;
		after.Serialize().Should().Be(serializedBefore);
		after.GameHistoryLog.Should().Equal(historyBefore);
		pending.Service.GetCurrentInstruction(pending.GameId)!.InstructionId
			.Should().Be(pending.Selector.InstructionId);
	}

	[Fact]
	public void BorrowedHunter_PendingSelectorRoundTripsCommitsOnceAndRejectsStaleReplayWithoutLeak()
	{
		var pending = CreatePendingBorrowedHunterFinalShot();
		var pendingState = pending.Service.GetGameStateView(pending.GameId)!;
		var recoveredReaction = new ForcedDescendantReaction(
			pending.Fixture.ActorId,
			pending.Fixture.ForcedVictimId);
		var recoveredService = CreateServiceWithForcedReaction(
			recoveredReaction);
		var recoveredGameId = recoveredService.RehydrateSession(
			pendingState.Serialize());
		var recoveredSelector = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		recoveredSelector.InstructionId.Should().Be(
			pending.Selector.InstructionId);
		recoveredSelector.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		recoveredSelector.CountConstraint.Should().Be(
			NumberRangeConstraint.Single);
		recoveredSelector.AffectedPlayerIds.Should().Equal(
			pending.Fixture.ActorId);
		recoveredSelector.SelectablePlayerIds.Should().BeEquivalentTo(
			pending.Selector.SelectablePlayerIds);
		recoveredSelector.PublicAnnouncement.Should().Be(
			pending.Selector.PublicAnnouncement);
		recoveredSelector.PrivateInstruction.Should().Be(
			pending.Selector.PrivateInstruction);
		AssertActorSafeText(
			string.Concat(
				recoveredSelector.PublicAnnouncement,
				"\n",
				recoveredSelector.PrivateInstruction),
			pending.Fixture);

		var acceptedTargetResponse = recoveredSelector.CreateResponse(
			[pending.Fixture.ShotTargetId]);
		var targetReveal = recoveredService.ProcessInstruction(
				recoveredGameId,
				acceptedTargetResponse)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		targetReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		targetReveal.AffectedPlayerIds.Should().Equal(
			pending.Fixture.ShotTargetId);
		var beforeStaleReplay = recoveredService
			.GetGameStateView(recoveredGameId)!;
		var serializedBeforeStaleReplay = beforeStaleReplay.Serialize();
		var historyBeforeStaleReplay = beforeStaleReplay.GameHistoryLog.ToArray();

		Action replayAcceptedTarget = () =>
			recoveredService.ProcessInstruction(
				recoveredGameId,
				acceptedTargetResponse);

		var staleException = replayAcceptedTarget.Should()
			.Throw<InvalidOperationException>().Which;
		AssertActorSafeText(staleException.Message, pending.Fixture);
		var afterStaleReplay = recoveredService
			.GetGameStateView(recoveredGameId)!;
		afterStaleReplay.Serialize().Should().Be(
			serializedBeforeStaleReplay);
		afterStaleReplay.GameHistoryLog.Should().Equal(
			historyBeforeStaleReplay);
		recoveredService.GetCurrentInstruction(recoveredGameId)!.InstructionId
			.Should().Be(targetReveal.InstructionId);

		var afterShot = recoveredService.ProcessInstruction(
			recoveredGameId,
			targetReveal.CreateResponse());

		afterShot.IsSuccess.Should().BeTrue();
		var completed = recoveredService.GetGameStateView(recoveredGameId)!;
		completed.GetPlayerState(pending.Fixture.ShotTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Where(entry =>
				entry.PlayerId == pending.Fixture.ShotTargetId)
			.Should().ContainSingle().Which.Reason.Should().Be(
				EliminationReason.EventElimination);
		AssertActorSafeText(
			string.Join(
				"\n",
				completed.GameHistoryLog.Select(entry => entry.ToString())),
			pending.Fixture);
	}

	[Fact]
	public void BorrowedHunter_CommittedShotRecoversAtTargetRevealAndCompletesCascadeWithoutSelectorReplay()
	{
		var pending = CreatePendingBorrowedHunterFinalShot();
		var targetReveal = pending.Service.ProcessInstruction(
				pending.GameId,
				pending.Selector.CreateResponse(
					[pending.Fixture.ShotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var committed = pending.Service.GetGameStateView(pending.GameId)!;
		committed.GetPlayerState(pending.Fixture.ShotTargetId).Health.Should()
			.Be(PlayerHealth.Alive);
		committed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		committed.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Should().NotContain(entry =>
				entry.ReactionId ==
				EliminationCascadeReactionIds.HunterFinalShot);

		var recoveredService = CreateServiceWithForcedReaction(
			new ForcedDescendantReaction(
				pending.Fixture.ActorId,
				pending.Fixture.ForcedVictimId));
		var recoveredGameId = recoveredService.RehydrateSession(
			committed.Serialize());
		var recoveredTargetReveal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredTargetReveal.Should().BeEquivalentTo(targetReveal);
		var continued = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredTargetReveal.CreateResponse());

		continued.IsSuccess.Should().BeTrue();
		continued.ModeratorInstruction?.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		var completed = recoveredService.GetGameStateView(recoveredGameId)!;
		completed.GetPlayerState(pending.Fixture.ShotTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		completed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == pending.Fixture.ShotTargetId &&
				entry.Reason == EliminationReason.EventElimination);
		completed.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == "Day:1:Vote:1");
	}

	[Fact]
	public void BorrowedHunter_CommittedShotSurvivesActivationExpiryAndRecoversWithoutReplay()
	{
		var pending = CreatePendingBorrowedHunterFinalShot();
		var targetReveal = pending.Service.ProcessInstruction(
				pending.GameId,
				pending.Selector.CreateResponse(
					[pending.Fixture.ShotTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var nightStart = pending.Service.ProcessInstruction(
				pending.GameId,
				targetReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		nightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var arranged = (GameSession)pending.Service.GetGameStateView(
			pending.GameId)!;
		arranged.GetCurrentPhase().Should().Be(GamePhase.Night);
		arranged.GetPlayerState(pending.Fixture.ActorId).Health.Should().Be(
			PlayerHealth.Dead);
		arranged.GetPlayerState(pending.Fixture.ShotTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		arranged.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		arranged.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == pending.Fixture.ShotTargetId &&
				entry.Reason == EliminationReason.EventElimination);
		arranged.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		var arrangedPayload = RecoveryPayloadTestDriver.Capture(arranged)
			.Serialize();

		var recoveredService = CreateServiceWithForcedReaction(
			new ForcedDescendantReaction(
				pending.Fixture.ActorId,
				pending.Fixture.ForcedVictimId));
		var recoveredGameId = recoveredService.RehydrateSession(
			arrangedPayload);
		var recoveredNightStart = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredNightStart.Should().BeEquivalentTo(nightStart);
		IGameSession recovered = recoveredService.GetGameStateView(
			recoveredGameId)!;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		recovered.GetPlayerState(pending.Fixture.ActorId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GetPlayerState(pending.Fixture.ShotTargetId).Health.Should()
			.Be(PlayerHealth.Dead);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == pending.Fixture.ShotTargetId &&
				entry.Reason == EliminationReason.EventElimination);

		var afterNightStart = recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredNightStart.CreateResponse());

		afterNightStart.IsSuccess.Should().BeTrue();
		afterNightStart.ModeratorInstruction?.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		var continued = recoveredService.GetGameStateView(recoveredGameId)!;
		continued.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		continued.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == pending.Fixture.ShotTargetId &&
				entry.Reason == EliminationReason.EventElimination);
	}

	private static GameService CreateServiceWithForcedReaction(
		IEliminationCascadeReaction reaction) =>
		new(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					reaction,
					EliminationCascadeReactionBoundary.Forced)
			]);

	private static PendingElderSuppression
		CreatePendingBorrowedElderSuppressionAnnouncement()
	{
		var fixture = CreateActiveElderActorVoteSuppressionSession();
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var start = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var debate = service.ProcessInstruction(gameId, start.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var vote = service.ProcessInstruction(gameId, debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var actorReveal = service.ProcessInstruction(
				gameId,
				vote.CreateResponse([fixture.ActorId]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		actorReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		actorReveal.AffectedPlayerIds.Should().Equal(fixture.ActorId);
		var actorEliminationAnnouncement = service.ProcessInstruction(
				gameId,
				actorReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		actorEliminationAnnouncement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var afterActorElimination = service.GetGameStateView(gameId)!;
		var eliminatedActor = afterActorElimination.GetPlayerState(
			fixture.ActorId);
		eliminatedActor.CurrentRole.Should().Be(MainRoleType.Actor);
		eliminatedActor.PubliclyRevealedRole.Should().Be(MainRoleType.Actor);
		eliminatedActor.PhysicalCharacterCardId.Should().Be(
			fixture.ActorCardId);
		eliminatedActor.Health.Should().Be(PlayerHealth.Dead);
		afterActorElimination.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().BeEmpty();
		afterActorElimination.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();

		var cascade = service.ProcessInstruction(
			gameId,
			actorEliminationAnnouncement.CreateResponse());
		SelectPlayersInstruction? finalShot = null;
		for (var step = 0; step < 12 && finalShot == null; step++)
		{
			if (cascade.ModeratorInstruction is SelectPlayersInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.SelectHunterFinalShotTarget
				} shot)
			{
				finalShot = shot;
				break;
			}

			var inFlight = service.GetGameStateView(gameId)!;
			inFlight.GameHistoryLog
				.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
				.Should().BeEmpty();
			inFlight.GameHistoryLog
				.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
				.Should().BeEmpty();
			cascade = cascade.ModeratorInstruction switch
			{
				AssignRolesInstruction assignment => service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => playerId == fixture.HunterId
								? MainRoleType.Hunter
								: MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation => service.ProcessInstruction(
					gameId,
					confirmation.CreateResponse()),
				_ => cascade
			};
			if (cascade.ModeratorInstruction is not (
				AssignRolesInstruction or ConfirmationInstruction or
				SelectPlayersInstruction))
			{
				break;
			}
		}

		finalShot.Should().NotBeNull(
			"the Hunter heartbreak reaction must finish before suppression");
		var pendingShot = finalShot!;
		pendingShot.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		pendingShot.AffectedPlayerIds.Should().Equal(fixture.HunterId);
		var beforeShot = service.GetGameStateView(gameId)!;
		beforeShot.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.ActorId &&
				entry.Reason == EliminationReason.DayVote);
		beforeShot.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.HunterId &&
				entry.Reason == EliminationReason.LoversHeartbreak);
		beforeShot.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().NotContain(entry => entry.ScopeId == "Day:1:Vote:1");
		beforeShot.GameHistoryLog
			.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
			.Should().BeEmpty();

		var afterShot = service.ProcessInstruction(
			gameId,
			pendingShot.CreateResponse([fixture.ShotTargetId]));
		ConfirmationInstruction? suppression = null;
		for (var step = 0; step < 12 && suppression == null; step++)
		{
			if (afterShot.ModeratorInstruction is ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic
							.AnnounceVillagerRolePowerSuppression
				} announcement)
			{
				suppression = announcement;
				break;
			}

			afterShot = afterShot.ModeratorInstruction switch
			{
				AssignRolesInstruction assignment => service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation => service.ProcessInstruction(
					gameId,
					confirmation.CreateResponse()),
				_ => afterShot
			};
			if (afterShot.ModeratorInstruction is not (
				AssignRolesInstruction or ConfirmationInstruction))
			{
				break;
			}
		}

		suppression.Should().NotBeNull(
			"a village-voted Actor with an active Elder card must suppress only after the cascade completes");
		var pendingAnnouncement = suppression!;
		var suppressed = service.GetGameStateView(gameId)!;
		suppressed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.ShotTargetId &&
				entry.Reason == EliminationReason.HunterShot);
		suppressed.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId == "Day:1:Vote:1");
		return new PendingElderSuppression(
			service,
			gameId,
			fixture,
			pendingAnnouncement);
	}

	private static ConfirmationInstruction
		AdvanceToDayEliminationAnnouncement(
			GameService service,
			Guid gameId,
			Guid targetId)
	{
		var start = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<StartGameConfirmationInstruction>().Subject;
		var debate = service.ProcessInstruction(gameId, start.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var vote = service.ProcessInstruction(gameId, debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var roleReveal = service.ProcessInstruction(
				gameId,
				vote.CreateResponse([targetId]))
			.ModeratorInstruction;
		if (roleReveal is AssignRolesInstruction assignment)
		{
			assignment.Semantic.Should().Be(
				ModeratorInstructionSemantic.AssignDayVoteTargetRole);
			var publicSession = service.GetGameStateView(gameId)!;
			return service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => publicSession
								.GetPlayerState(playerId)
								.CurrentRole!.Value)))
				.ModeratorInstruction.Should()
				.BeOfType<ConfirmationInstruction>().Subject;
		}

		var actorReveal = roleReveal.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		return service.ProcessInstruction(gameId, actorReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
	}

	private static PendingHunterShot CreatePendingBorrowedHunterFinalShot()
	{
		var fixture = CreateActiveHunterActorSession();
		var forcedReaction = new ForcedDescendantReaction(
			fixture.ActorId,
			fixture.ForcedVictimId);
		var service = CreateServiceWithForcedReaction(forcedReaction);
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var actorEliminationAnnouncement =
			AdvanceToDayEliminationAnnouncement(
				service,
				gameId,
				fixture.ActorId);
		var forcedReveal = service.ProcessInstruction(
				gameId,
				actorEliminationAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var selector = service.ProcessInstruction(
				gameId,
				forcedReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return new PendingHunterShot(service, gameId, fixture, selector);
	}

	private static void AssertActorSafeText(
		string text,
		HunterFixture fixture)
	{
		text.Should().NotContain(GameStrings.HunterRoleName)
			.And.NotContain(MainRoleType.Hunter.ToString())
			.And.NotContain(EliminationCascadeReactionIds.HunterFinalShot)
			.And.NotContain(HunterCard.Id.ToString())
			.And.NotContain(fixture.ActivationId.ToString());
	}

	private static void AssertActorSafeElderText(
		string text,
		Guid activationId)
	{
		text.Should().NotContain(GameStrings.ElderRoleName)
			.And.NotContain(MainRoleType.Elder.ToString())
			.And.NotContain("elder-werewolf-attack-resistance")
			.And.NotContain(ElderCard.Id.ToString())
			.And.NotContain(activationId.ToString())
			.And.NotContain(StatusEffectTypes.ElderProtectionLost.ToString());
	}

	private static ElderSuppressionFixture
		CreateActiveElderActorVoteSuppressionSession()
	{
		var setup = new ActorSetupCards(
			version: 13,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Werewolf",
				"Cupid",
				"Hunter",
				"Shot target",
				"Villager A",
				"Villager B",
				"Villager C"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var cupidId = players[2].Id;
		var hunterId = players[3].Id;
		var shotTargetId = players[4].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Name switch
				{
					"Performer" => MainRoleType.Actor,
					"Werewolf" => MainRoleType.SimpleWerewolf,
					"Cupid" => MainRoleType.Cupid,
					"Hunter" => MainRoleType.Hunter,
					_ => MainRoleType.SimpleVillager
				});
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		var hunterCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Hunter);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			hunterId,
			hunterCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { cupidId },
			MainRoleType.Cupid);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { hunterId },
			MainRoleType.Hunter);
		session.CommitLoversPair(
			[actorId, hunterId],
			new RolePowerInstanceIdentity(
				cupidId,
				MainRoleType.Cupid,
				CupidRole.LinkLoversPowerIdentifier.Value,
				cupidId,
				RolePowerInstanceOrigin.Native));
		session.TrySpendActorSetupCard(
			actorId,
			ElderCard.Id,
			out var activation).Should().BeTrue();
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		session.PerformDayActionNoTarget(DayPowerType.JudgeExtraVote);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new ElderSuppressionFixture(
			session,
			actorId,
			hunterId,
			shotTargetId,
			actorCard.Card.Id,
			activation!.ActivationId);
	}

	private static ElderFixture CreateElderActorSession(bool preActivate = true)
	{
		var setup = new ActorSetupCards(
			version: 8,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		if (preActivate)
		{
			session.TrySpendActorSetupCard(
				actorId,
				ElderCard.Id,
				out _).Should().BeTrue();
		}
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new ElderFixture(session, actorId, werewolfId);
	}

	private static ElderInfectionFixture CreateElderActorInfectionSession()
	{
		var setup = new ActorSetupCards(
			version: 9,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Werewolf",
				"Wolf Father",
				"Villager A",
				"Villager B",
				"Villager C"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		var wolfFatherId = players[2].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: player.Id == wolfFatherId
							? MainRoleType.AccursedWolfFather
							: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { wolfFatherId },
			MainRoleType.AccursedWolfFather);
		session.PerformNightAction(NightActionType.DefenderProtect, actorId);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new ElderInfectionFixture(
			session,
			actorId,
			werewolfId,
			wolfFatherId);
	}

	private static ElderWitchRestorationFixture
		CreateElderActorWitchRestorationSession()
	{
		var setup = new ActorSetupCards(
			version: 12,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Werewolf",
				"Wolf Father",
				"Witch",
				"Villager A",
				"Villager B",
				"Villager C"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		var wolfFatherId = players[2].Id;
		var witchId = players[3].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: player.Id == wolfFatherId
							? MainRoleType.AccursedWolfFather
							: player.Id == witchId
								? MainRoleType.Witch
								: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { wolfFatherId },
			MainRoleType.AccursedWolfFather);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { witchId },
			MainRoleType.Witch);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new ElderWitchRestorationFixture(
			session,
			actorId,
			werewolfId,
			wolfFatherId,
			witchId);
	}

	private static ElderAdditionalAttackFixture
		CreateElderActorAdditionalAttackSession(MainRoleType attackerRole)
	{
		if (attackerRole is not MainRoleType.WhiteWerewolf and not
			MainRoleType.BigBadWolf)
		{
			throw new ArgumentOutOfRangeException(nameof(attackerRole));
		}

		var setup = new ActorSetupCards(
			version: 10,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Werewolf",
				"Additional attacker",
				"Collective target",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				attackerRole,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		var attackerId = players[2].Id;
		var collectiveTargetId = players[3].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: player.Id == attackerId
							? attackerRole
							: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { attackerId },
			attackerRole);
		var werewolfAgentIds = new HashSet<Guid>
		{
			werewolfId,
			attackerId
		};
		if (attackerRole == MainRoleType.WhiteWerewolf)
		{
			werewolfAgentIds.Add(actorId);
		}
		SeedRequiredFactionBeneficiaryFacts(session, werewolfAgentIds);
		session.TransitionMainPhase(GamePhase.Dawn);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		session.PerformNightAction(
			NightActionType.DefenderProtect,
			collectiveTargetId);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new ElderAdditionalAttackFixture(
			session,
			actorId,
			werewolfId,
			attackerId,
			collectiveTargetId);
	}

	private static ElderDefenderFixture CreateElderActorDefenderSession()
	{
		var setup = new ActorSetupCards(
			version: 11,
			[ElderCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Defender",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C",
				"Villager D"
			],
			[
				MainRoleType.Actor,
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var defenderId = players[1].Id;
		var werewolfId = players[2].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == defenderId
						? MainRoleType.Defender
						: player.Id == werewolfId
							? MainRoleType.SimpleWerewolf
							: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { defenderId },
			MainRoleType.Defender);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new ElderDefenderFixture(
			session,
			actorId,
			defenderId,
			werewolfId);
	}

	private static HunterFixture CreateActiveHunterActorSession()
	{
		var setup = new ActorSetupCards(
			version: 8,
			[HunterCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				"Performer",
				"Werewolf",
				"Forced victim",
				"Shot target",
				"Villager A",
				"Villager B"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Name == "Werewolf"
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		session.TrySpendActorSetupCard(
			actorId,
			HunterCard.Id,
			out var activation).Should().BeTrue();
		SeedRequiredFactionBeneficiaryFacts(session);
		session.TransitionMainPhase(GamePhase.Day);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(start)
			.RehydrateGameSession();
		return new HunterFixture(
			session,
			actorId,
			players[2].Id,
			players[3].Id,
			actorCard.Card.Id,
			activation!.ActivationId);
	}

	private static void SeedRequiredFactionBeneficiaryFacts(
		GameSession session)
	{
		var werewolfId = session.GetPlayers()
			.Single(player => player.Name == "Werewolf").Id;
		SeedRequiredFactionBeneficiaryFacts(
			session,
			new HashSet<Guid> { werewolfId });
	}

	private static void SeedRequiredFactionBeneficiaryFacts(
		GameSession session,
		IReadOnlySet<Guid> werewolfAgentIds)
	{
		var players = session.GetPlayers().ToArray();
		FactionFactEffectiveBoundary? agentGroupBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			agentGroupBoundary = boundary;
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts = players.Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						werewolfAgentIds.Contains(player.Id)
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
					.ToImmutableArray()
			};
		});

		InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				agentGroupBoundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);
	}

	private sealed class ForcedDescendantReaction(
		Guid actorId,
		Guid forcedVictimId) : IEliminationCascadeReaction
	{
		public string ReactionId => ForcedDescendantReactionId;

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			eliminatedPlayerIds.Contains(actorId)
				? EliminationCascadeReactionResult.Complete(
					[
						new EliminationRequest(
							forcedVictimId,
							EliminationReason.EventElimination)
					])
				: EliminationCascadeReactionResult.Complete();
	}

	private sealed class EliminateAllOtherLivingPlayersReaction(Guid actorId)
		: IEliminationCascadeReaction
	{
		public string ReactionId => "actor-reactive-eliminate-all";

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			eliminatedPlayerIds.Contains(actorId)
				? EliminationCascadeReactionResult.Complete(
					session.GetPlayers()
						.Where(player =>
							player.Id != actorId &&
							player.State.Health == PlayerHealth.Alive)
						.Select(player => new EliminationRequest(
							player.Id,
							EliminationReason.EventElimination))
						.ToArray())
				: EliminationCascadeReactionResult.Complete();
	}

	private sealed record PendingHunterShot(
		GameService Service,
		Guid GameId,
		HunterFixture Fixture,
		SelectPlayersInstruction Selector);

	private sealed record PendingElderSuppression(
		GameService Service,
		Guid GameId,
		ElderSuppressionFixture Fixture,
		ConfirmationInstruction Announcement);

	private sealed record HunterFixture(
		GameSession Session,
		Guid ActorId,
		Guid ForcedVictimId,
		Guid ShotTargetId,
		Guid ActorCardId,
		Guid ActivationId);

	private sealed record ElderFixture(
		GameSession Session,
		Guid ActorId,
		Guid WerewolfId);

	private sealed record ElderSuppressionFixture(
		GameSession Session,
		Guid ActorId,
		Guid HunterId,
		Guid ShotTargetId,
		Guid ActorCardId,
		Guid ActivationId);

	private sealed record ElderInfectionFixture(
		GameSession Session,
		Guid ActorId,
		Guid WerewolfId,
		Guid WolfFatherId);

	private sealed record ElderWitchRestorationFixture(
		GameSession Session,
		Guid ActorId,
		Guid WerewolfId,
		Guid WolfFatherId,
		Guid WitchId);

	private sealed record ElderAdditionalAttackFixture(
		GameSession Session,
		Guid ActorId,
		Guid WerewolfId,
		Guid AttackerId,
		Guid CollectiveTargetId);

	private sealed record ElderDefenderFixture(
		GameSession Session,
		Guid ActorId,
		Guid DefenderId,
		Guid WerewolfId);

}
