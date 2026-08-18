using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Narrow typed seam for constructing contradictory recovery payloads.
/// Integration tests still enter through opaque Serialize/Rehydrate behavior.
/// </summary>
internal sealed class RecoveryPayloadTestDriver
{
	private static readonly JsonSerializerOptions SerializationOptions = new()
	{
		Converters =
		{
			new GameResultConverter(),
			new GameLogEntryConverter(),
			new ModeratorInstructionConverter(),
			new JsonStringEnumConverter()
		}
	};

	private readonly GameSessionDto _payload;

	private RecoveryPayloadTestDriver(GameSessionDto payload)
	{
		_payload = payload;
	}

	internal static RecoveryPayloadTestDriver Parse(string serializedSession)
	{
		var payload = JsonSerializer.Deserialize<GameSessionDto>(
			serializedSession,
			SerializationOptions)
			?? throw new InvalidOperationException(
				"The recovery test payload could not be deserialized.");
		return new RecoveryPayloadTestDriver(payload);
	}

	internal static RecoveryPayloadTestDriver Capture(GameSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		var driver = Parse(session.Serialize());
		var payload = driver._payload;
		var execution = session.Execution;
		var actorSetupCards = session.GetModeratorActorSetupCards();
		var actorActivation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();

		payload.Id = session.Id;
		payload.TurnNumber = session.TurnNumber;
		payload.SeatingOrder = session.GetPlayers()
			.Select(player => player.Id)
			.ToList();
		payload.RolesInPlay = session.RoleLockIn.DealPool
			.Select(card => card.PrintedRole)
			.ToList();
		payload.RoleLockIn = RoleLockInDto.FromValue(session.RoleLockIn);
		payload.PublicGroupPartition = session.PublicGroupPartition is null
			? null
			: PublicGroupPartitionDto.FromValue(session.PublicGroupPartition);
		payload.ActorSetupCards = ActorSetupCardsDto.FromValue(actorSetupCards);
		payload.ActiveActorBorrowedRolePowerActivation = actorActivation is null
			? null
			: ActorBorrowedRolePowerActivationDto.FromValue(actorActivation);
		if (actorActivation != null)
		{
			driver.RecordActorSetupCardSpend(actorActivation);
		}

		payload.ActorBorrowedSeerCheckCommits = session
			.GetActorBorrowedSeerCheckCommits()
			.Select(ActorBorrowedSeerCheckCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedDefenderProtectionCommits = session
			.GetActorBorrowedDefenderProtectionCommits()
			.Select(ActorBorrowedDefenderProtectionCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedFoxCheckCommits = session
			.GetActorBorrowedFoxCheckCommits()
			.Select(ActorBorrowedFoxCheckCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedBearTamerGrowlCommits = session
			.GetActorBorrowedBearTamerGrowlCommits()
			.Select(ActorBorrowedBearTamerGrowlCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedKnightRustySwordScheduleCommits = session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Select(ActorBorrowedKnightRustySwordScheduleCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedHunterFinalShotCommits = session
			.GetActorBorrowedHunterFinalShotCommits()
			.Select(ActorBorrowedHunterFinalShotCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedElderResistanceCommits = session
			.GetActorBorrowedElderResistanceCommits()
			.Select(ActorBorrowedElderResistanceCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedElderSuppressionCommits = session
			.GetActorBorrowedElderSuppressionCommits()
			.Select(ActorBorrowedElderSuppressionCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedScapegoatTieReplacementCommits = session
			.GetActorBorrowedScapegoatTieReplacementCommits()
			.Select(ActorBorrowedScapegoatTieReplacementCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedScapegoatVoterRestrictionCommits = session
			.GetActorBorrowedScapegoatVoterRestrictionCommits()
			.Select(ActorBorrowedScapegoatVoterRestrictionCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedVillageIdiotPardonCommits = session
			.GetActorBorrowedVillageIdiotPardonCommits()
			.Select(ActorBorrowedVillageIdiotPardonCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedWitchPotionUseCommits = session
			.GetActorBorrowedWitchPotionUseCommits()
			.Select(ActorBorrowedWitchPotionUseCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedWitchPotionDeclineCommits = session
			.GetActorBorrowedWitchPotionDeclineCommits()
			.Select(ActorBorrowedWitchPotionDeclineCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedCupidLoversCommits = session
			.GetActorBorrowedCupidLoversCommits()
			.Select(ActorBorrowedCupidLoversCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedStutteringJudgeSignalSetupCommits = session
			.GetActorBorrowedStutteringJudgeSignalSetupCommits()
			.Select(ActorBorrowedStutteringJudgeSignalSetupCommitDto.FromValue)
			.ToList();
		payload.ActorBorrowedStutteringJudgeSignalObservationCommits = session
			.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Select(ActorBorrowedStutteringJudgeSignalObservationCommitDto.FromValue)
			.ToList();
		payload.PhysicalCharacterCards = session
			.GetModeratorPhysicalCharacterCards()
			.Select(state => new PhysicalCharacterCardStateDto
			{
				CardId = state.Card.Id,
				Zone = state.Zone,
				OwnerPlayerId = state.OwnerPlayerId
			})
			.ToList();
		payload.Players = session.GetPlayers()
			.Select(player => new PlayerDto
			{
				Id = player.Id,
				Name = player.Name,
				MainRole = player.State.MainRole,
				PhysicalCharacterCardId =
					player.State.PhysicalCharacterCardId,
				PhysicalCharacterCardRole =
					player.State.PhysicalCharacterCardRole,
				ModeratorKnownRole = player.State.ModeratorKnownRole,
				PubliclyRevealedRole = player.State.PubliclyRevealedRole,
				ActiveEffects = player.State.GetActiveStatusEffects()
					.Aggregate(
						StatusEffectTypes.None,
						(current, effect) => current | effect),
				Health = player.State.Health,
				HasVotingRight = player.State.HasVotingRight,
				DurableVotingPower = player.State.DurableVotingPower,
				FactionBeneficiary = player.State.FactionBeneficiary,
				FactionAgentKnowledge = Enum.GetValues<Faction>()
					.ToDictionary(
						faction => faction,
						faction => player.State
							.GetFactionAgentKnowledge(faction))
			})
			.ToList();
		payload.GameHistoryLog = session.GameHistoryLog.ToList();
		payload.PendingInstruction = execution.PendingInstruction;
		payload.PendingInstructionSemantic =
			execution.PendingInstruction?.Semantic;
		payload.AcceptedObservationRecoveryCursor =
			execution.AcceptedObservationRecoveryCursor;
		payload.DomainRecoveryCursor = execution.DomainRecoveryCursor;
		payload.PhaseStateCache = new GamePhaseStateCacheDto
		{
			CurrentPhase = execution.CurrentPhase,
			SubPhase = execution.SubPhaseId,
			CompletedSubPhaseStages = execution.CompletedSubPhaseStages.ToList()
		};
		payload.IsStableRecoveryBoundary = true;

		return driver;
	}

	internal RecoveryPayloadTestDriver RecordActorSetupCardSpend(
		ActorBorrowedRolePowerActivation activation)
	{
		ArgumentNullException.ThrowIfNull(activation);
		_payload.ActorSetupCardSpends ??= [];
		_payload.ActorSetupCardSpends.RemoveAll(spend =>
			spend.CardId == activation.SelectedCardId);
		_payload.ActorSetupCardSpends.Add(new ActorSetupCardSpendDto
		{
			CardId = activation.SelectedCardId,
			ActivationId = activation.ActivationId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver WithPendingInstruction(
		ModeratorInstruction instruction)
	{
		ArgumentNullException.ThrowIfNull(instruction);
		_payload.PendingInstruction = instruction;
		_payload.PendingInstructionSemantic = instruction.Semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver WithRecoveryCursors(
		AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor = null,
		DomainRecoveryCursor? domainRecoveryCursor = null)
	{
		_payload.AcceptedObservationRecoveryCursor =
			acceptedObservationRecoveryCursor;
		_payload.DomainRecoveryCursor = domainRecoveryCursor;
		return this;
	}

	internal RecoveryPayloadTestDriver WithSubPhase(Enum? subPhase)
	{
		_payload.PhaseStateCache.SubPhase = subPhase?.ToString();
		_payload.PhaseStateCache.ActiveSubPhaseStage = null;
		_payload.PhaseStateCache.CurrentListenerId = null;
		_payload.PhaseStateCache.CurrentListenerType = null;
		_payload.PhaseStateCache.CurrentListenerState = null;
		return this;
	}

	internal ModeratorInstruction? PendingInstruction =>
		_payload.PendingInstruction;

	internal AcceptedObservationRecoveryCursor?
		AcceptedObservationRecoveryCursor =>
		_payload.AcceptedObservationRecoveryCursor;

	internal DomainRecoveryCursor? DomainRecoveryCursor =>
		_payload.DomainRecoveryCursor;

	internal GameSession RehydrateGameSession()
	{
		var service = new GameService();
		var gameId = service.RehydrateSession(Serialize());
		return (GameSession)(service.GetGameStateView(gameId)
			?? throw new InvalidOperationException(
				"The recovery test payload did not register a Game Session."));
	}

	internal static ActorBorrowedHunterPendingRecoverySnapshot
		CreateActorBorrowedHunterPendingSelectorSnapshot(
			IStateChangeObserver sourceObserver)
	{
		ArgumentNullException.ThrowIfNull(sourceObserver);
		var hunterCard = new PhysicalCharacterCard(
			Guid.Parse("00000000-0000-0000-0000-000000000261"),
			MainRoleType.Hunter);
		var setup = new ActorSetupCards(
			version: 8,
			[
				hunterCard,
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000262"),
					MainRoleType.Seer),
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000263"),
					MainRoleType.Fox)
			]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Target", "Villager A", "Villager B"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var sourceSession = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = sourceSession.GetPlayers().ToArray();
		var actorId = players[0].Id;
		foreach (var player in players)
		{
			sourceSession.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Name == "Werewolf"
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = sourceSession.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!sourceSession.TryRecordPhysicalCharacterCardOwnership(
				sourceSession.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Hunter recovery fixture could not bind Actor's physical card.");
		}

		sourceSession.IdentifyRole([actorId], MainRoleType.Actor);
		if (!sourceSession.TrySpendActorSetupCard(
				actorId,
				hunterCard.Id,
				out var activation))
		{
			throw new InvalidOperationException(
				"The Hunter recovery fixture could not activate its setup card.");
		}

		SeedActorBorrowedHunterRecoveryFacts(sourceSession, players[1].Id);
		sourceSession.TransitionMainPhase(GamePhase.Day);
		var serializedSource = RecoveryPayloadTestDriver.Capture(sourceSession)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = RequireActorBorrowedHunterInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		var debate = RequireActorBorrowedHunterInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, recoveredStart.CreateResponse())
				.ModeratorInstruction);
		var vote = RequireActorBorrowedHunterInstruction<
			SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, debate.CreateResponse())
				.ModeratorInstruction);
		var actorReveal = RequireActorBorrowedHunterInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				vote.CreateResponse([actorId])).ModeratorInstruction);
		if (actorReveal.Semantic !=
			ModeratorInstructionSemantic.AssignDayVoteTargetRole)
		{
			throw new InvalidOperationException(
				"The Hunter recovery fixture did not reach Actor's public reveal.");
		}

		var elimination = RequireActorBorrowedHunterInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, actorReveal.CreateResponse())
				.ModeratorInstruction);
		if (elimination.Semantic !=
			ModeratorInstructionSemantic.AnnounceDayElimination)
		{
			throw new InvalidOperationException(
				"The Hunter recovery fixture did not reach Actor's Elimination announcement.");
		}

		var selector = RequireActorBorrowedHunterInstruction<
			SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, elimination.CreateResponse())
				.ModeratorInstruction);
		if (selector.Semantic !=
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget ||
			selector.CountConstraint != NumberRangeConstraint.Single ||
			selector.AffectedPlayerIds is not [var affectedPlayerId] ||
			affectedPlayerId != actorId)
		{
			throw new InvalidOperationException(
				"The Hunter recovery fixture did not reach the correlated final-shot selector.");
		}

		var pendingState = service.GetGameStateView(gameId)
			?? throw new InvalidOperationException(
				"The Hunter recovery fixture lost its pending Game Session.");
		return new ActorBorrowedHunterPendingRecoverySnapshot(
			pendingState.Serialize(),
			gameId,
			selector,
			hunterCard.Id,
			activation!.ActivationId);
	}

	internal static ActorBorrowedElderPendingRecoverySnapshot
		CreateActorBorrowedElderPendingSuppressionAnnouncementSnapshot(
			IStateChangeObserver sourceObserver)
	{
		ArgumentNullException.ThrowIfNull(sourceObserver);
		var elderCard = new PhysicalCharacterCard(
			Guid.Parse("00000000-0000-0000-0000-000000000271"),
			MainRoleType.Elder);
		var setup = new ActorSetupCards(
			version: 8,
			[
				elderCard,
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000272"),
					MainRoleType.Seer),
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000273"),
					MainRoleType.Fox)
			]);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager A", "Villager B", "Villager C", "Villager D"],
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
		var sourceSession = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = sourceSession.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		foreach (var player in players)
		{
			sourceSession.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = sourceSession.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!sourceSession.TryRecordPhysicalCharacterCardOwnership(
				sourceSession.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Elder recovery fixture could not bind Actor's physical card.");
		}

		sourceSession.IdentifyRole([actorId], MainRoleType.Actor);
		if (!sourceSession.TrySpendActorSetupCard(
				actorId,
				elderCard.Id,
				out var activation))
		{
			throw new InvalidOperationException(
				"The Elder recovery fixture could not activate its setup card.");
		}

		SeedActorBorrowedHunterRecoveryFacts(sourceSession, werewolfId);
		sourceSession.TransitionMainPhase(GamePhase.Day);
		var serializedSource = Capture(sourceSession)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = RequireActorBorrowedElderInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		var debate = RequireActorBorrowedElderInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, recoveredStart.CreateResponse())
				.ModeratorInstruction);
		var vote = RequireActorBorrowedElderInstruction<
			SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, debate.CreateResponse())
				.ModeratorInstruction);
		var actorReveal = RequireActorBorrowedElderInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				vote.CreateResponse([actorId])).ModeratorInstruction);
		if (actorReveal.Semantic !=
			ModeratorInstructionSemantic.AssignDayVoteTargetRole)
		{
			throw new InvalidOperationException(
				"The Elder recovery fixture did not reach Actor's public reveal.");
		}

		var elimination = RequireActorBorrowedElderInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, actorReveal.CreateResponse())
				.ModeratorInstruction);
		if (elimination.Semantic !=
			ModeratorInstructionSemantic.AnnounceDayElimination)
		{
			throw new InvalidOperationException(
				"The Elder recovery fixture did not reach Actor's Elimination announcement.");
		}

		var progress = service.ProcessInstruction(
			gameId,
			elimination.CreateResponse());
		ConfirmationInstruction? announcement = null;
		for (var step = 0; step < 12 && announcement == null; step++)
		{
			if (progress.ModeratorInstruction is ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic
						.AnnounceVillagerRolePowerSuppression
				} pendingAnnouncement)
			{
				announcement = pendingAnnouncement;
				break;
			}

			progress = progress.ModeratorInstruction switch
			{
				AssignRolesInstruction assignment => service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => playerId == actorId
								? MainRoleType.Actor
								: playerId == werewolfId
									? MainRoleType.SimpleWerewolf
									: MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation =>
					service.ProcessInstruction(
						gameId,
						confirmation.CreateResponse()),
				_ => throw new InvalidOperationException(
					"The Elder recovery fixture left the suppression continuation.")
			};
		}

		if (announcement is not
			{
				PublicAnnouncement: var publicAnnouncement,
				PrivateInstruction: null,
				AffectedPlayerIds: null
			} ||
			!StringComparer.Ordinal.Equals(
				publicAnnouncement,
				GameStrings.VillagerRolePowerSuppressionAnnouncement) ||
			announcement.SoundEffects.Count != 0)
		{
			throw new InvalidOperationException(
				"The Elder recovery fixture did not reach the canonical suppression announcement.");
		}

		var pendingState = service.GetGameStateView(gameId)
			?? throw new InvalidOperationException(
				"The Elder recovery fixture lost its pending Game Session.");
		return new ActorBorrowedElderPendingRecoverySnapshot(
			pendingState.Serialize(),
			gameId,
			announcement,
			elderCard.Id,
			activation!.ActivationId);
	}

	internal static ActorBorrowedScapegoatPendingRecoverySnapshot
		CreateActorBorrowedScapegoatPendingSnapshot(
			ActorBorrowedScapegoatRecoveryStep step,
			IStateChangeObserver sourceObserver)
	{
		ArgumentNullException.ThrowIfNull(sourceObserver);
		var scapegoatCard = new PhysicalCharacterCard(
			Guid.Parse("00000000-0000-0000-0000-000000000281"),
			MainRoleType.Scapegoat);
		var setup = new ActorSetupCards(
			version: 7,
			[
				scapegoatCard,
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000282"),
					MainRoleType.Seer),
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000283"),
					MainRoleType.Fox)
			]);
		var config = new GameSessionConfig(
			[
				GameStrings.ActorRoleName,
				"Werewolf",
				"Permitted voter",
				"Villager A",
				"Villager B",
				"Villager C"
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
		var sourceSession = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = sourceSession.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		foreach (var player in players)
		{
			sourceSession.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = sourceSession.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!sourceSession.TryRecordPhysicalCharacterCardOwnership(
				sourceSession.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Scapegoat recovery fixture could not bind Actor's physical card.");
		}

		sourceSession.IdentifyRole([actorId], MainRoleType.Actor);
		if (!sourceSession.TrySpendActorSetupCard(
				actorId,
				scapegoatCard.Id,
				out var activation))
		{
			throw new InvalidOperationException(
				"The Scapegoat recovery fixture could not activate its setup card.");
		}

		SeedActorBorrowedHunterRecoveryFacts(sourceSession, werewolfId);
		sourceSession.TransitionMainPhase(GamePhase.Day);
		var serializedSource = RecoveryPayloadTestDriver.Capture(sourceSession)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = RequireActorBorrowedScapegoatInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		var debate = RequireActorBorrowedScapegoatInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, recoveredStart.CreateResponse())
				.ModeratorInstruction);
		var vote = RequireActorBorrowedScapegoatInstruction<
			SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, debate.CreateResponse())
				.ModeratorInstruction);
		var reveal = RequireActorBorrowedScapegoatInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, vote.CreateResponse([]))
				.ModeratorInstruction);
		if (reveal.Semantic !=
				ModeratorInstructionSemantic.RevealScapegoatForTie ||
			reveal.AffectedPlayerIds is not [var affectedActorId] ||
			affectedActorId != actorId ||
			!StringComparer.Ordinal.Equals(
				reveal.PublicAnnouncement,
				GameStrings.ActorRoleName) ||
			!StringComparer.Ordinal.Equals(
				reveal.PrivateInstruction,
				GameStrings.PublicRoleRevealInstruction) ||
			reveal.SoundEffects.Count != 0)
		{
			throw new InvalidOperationException(
				"The Scapegoat recovery fixture did not reach the canonical borrowed reveal.");
		}

		ActorBorrowedScapegoatPendingRecoverySnapshot Capture(
			ActorBorrowedScapegoatRecoveryStep capturedStep,
			ModeratorInstruction instruction)
		{
			var pendingState = service.GetGameStateView(gameId) as GameSession
				?? throw new InvalidOperationException(
					"The Scapegoat recovery fixture lost its pending Game Session.");
			var liveInstruction = service.GetCurrentInstruction(gameId);
			if (liveInstruction == null ||
				liveInstruction.InstructionId != instruction.InstructionId ||
				liveInstruction.Semantic != instruction.Semantic)
			{
				throw new InvalidOperationException(
					"The Scapegoat recovery fixture did not reach the requested live pending instruction.");
			}

			var serializedSession = capturedStep ==
				ActorBorrowedScapegoatRecoveryStep.Reveal
				? RecoveryPayloadTestDriver.Capture(pendingState)
					.WithPendingInstruction(instruction)
					.Serialize()
				: pendingState.Serialize();

			return new ActorBorrowedScapegoatPendingRecoverySnapshot(
				capturedStep,
				serializedSession,
				gameId,
				instruction,
				scapegoatCard.Id,
				activation!.ActivationId);
		}

		if (step == ActorBorrowedScapegoatRecoveryStep.Reveal)
		{
			return Capture(step, reveal);
		}

		var selection = RequireActorBorrowedScapegoatInstruction<
			SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, reveal.CreateResponse())
				.ModeratorInstruction);
		var expectedCandidates = players
			.Where(player => player.Id != actorId)
			.Select(player => player.Id)
			.ToHashSet();
		if (selection.Semantic !=
				ModeratorInstructionSemantic.SelectScapegoatPermittedVoters ||
			selection.CountConstraint != NumberRangeConstraint.AtLeast(1) ||
			!selection.SelectablePlayerIds.SetEquals(expectedCandidates) ||
			selection.AffectedPlayerIds is not { } selectionAffectedIds ||
			!selectionAffectedIds.ToHashSet().SetEquals(expectedCandidates) ||
			selection.PublicAnnouncement is not null ||
			!StringComparer.Ordinal.Equals(
				selection.PrivateInstruction,
				GameStrings.ScapegoatPermittedVotersSelectionInstruction) ||
			selection.RoleIdentification is not null ||
			selection.EmptySelectionOptionLabel is not null)
		{
			throw new InvalidOperationException(
				"The Scapegoat recovery fixture did not reach the canonical permitted-voter selector.");
		}

		if (step == ActorBorrowedScapegoatRecoveryStep.PermittedVoterSelection)
		{
			return Capture(step, selection);
		}

		if (step != ActorBorrowedScapegoatRecoveryStep.PermittedVoterAnnouncement)
		{
			throw new ArgumentOutOfRangeException(nameof(step), step, null);
		}

		var permittedVoterId = selection.SelectablePlayerIds.First();
		var announcement = RequireActorBorrowedScapegoatInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				selection.CreateResponse([permittedVoterId]))
				.ModeratorInstruction);
		var permittedVoterName = sourceSession.GetPlayer(permittedVoterId).Name;
		if (announcement.Semantic !=
				ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters ||
			announcement.AffectedPlayerIds is not [var affectedVoterId] ||
			affectedVoterId != permittedVoterId ||
			!StringComparer.Ordinal.Equals(
				announcement.PublicAnnouncement,
				GameStrings.ScapegoatPermittedVotersAnnouncement.Format(
					permittedVoterName)) ||
			announcement.PrivateInstruction is not null ||
			announcement.SoundEffects.Count != 0)
		{
			throw new InvalidOperationException(
				"The Scapegoat recovery fixture did not reach the canonical permitted-voter announcement.");
		}

		return Capture(step, announcement);
	}

	internal static ActorBorrowedVillageIdiotPendingRecoverySnapshot
		CreateActorBorrowedVillageIdiotPendingPardonSnapshot(
			IStateChangeObserver sourceObserver)
	{
		ArgumentNullException.ThrowIfNull(sourceObserver);
		var villageIdiotCard = new PhysicalCharacterCard(
			Guid.Parse("00000000-0000-0000-0000-000000000291"),
			MainRoleType.VillageIdiot);
		var setup = new ActorSetupCards(
			version: 7,
			[
				villageIdiotCard,
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000292"),
					MainRoleType.Seer),
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000293"),
					MainRoleType.Fox)
			]);
		var config = new GameSessionConfig(
			[
				GameStrings.ActorRoleName,
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
		var sourceSession = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = sourceSession.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		foreach (var player in players)
		{
			sourceSession.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = sourceSession.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!sourceSession.TryRecordPhysicalCharacterCardOwnership(
				sourceSession.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Village Idiot recovery fixture could not bind Actor's physical card.");
		}

		sourceSession.IdentifyRole([actorId], MainRoleType.Actor);
		if (!sourceSession.TrySpendActorSetupCard(
				actorId,
				villageIdiotCard.Id,
				out var activation))
		{
			throw new InvalidOperationException(
				"The Village Idiot recovery fixture could not activate its setup card.");
		}

		SeedActorBorrowedHunterRecoveryFacts(sourceSession, werewolfId);
		sourceSession.TransitionMainPhase(GamePhase.Day);
		var serializedSource = Capture(sourceSession)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = RequireActorBorrowedVillageIdiotInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		var debate = RequireActorBorrowedVillageIdiotInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, recoveredStart.CreateResponse())
				.ModeratorInstruction);
		var vote = RequireActorBorrowedVillageIdiotInstruction<
			SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, debate.CreateResponse())
				.ModeratorInstruction);
		var actorReveal = RequireActorBorrowedVillageIdiotInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				vote.CreateResponse([actorId])).ModeratorInstruction);
		if (actorReveal.Semantic !=
			ModeratorInstructionSemantic.AssignDayVoteTargetRole)
		{
			throw new InvalidOperationException(
				"The Village Idiot recovery fixture did not reach Actor's public reveal.");
		}

		var pardon = RequireActorBorrowedVillageIdiotInstruction<
			ConfirmationInstruction>(
			service.ProcessInstruction(gameId, actorReveal.CreateResponse())
				.ModeratorInstruction);
		if (pardon.Semantic !=
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon ||
			pardon.AffectedPlayerIds is not [var affectedActorId] ||
			affectedActorId != actorId ||
			!StringComparer.Ordinal.Equals(
				pardon.PublicAnnouncement,
				GameStrings.ActorBorrowedVillageIdiotPardonAnnouncement.Format(
					sourceSession.GetPlayer(actorId).Name)) ||
			pardon.PrivateInstruction is not null ||
			pardon.SoundEffects.Count != 0)
		{
			throw new InvalidOperationException(
				"The Village Idiot recovery fixture did not reach the canonical borrowed pardon announcement.");
		}

		var pendingState = service.GetGameStateView(gameId) as GameSession
			?? throw new InvalidOperationException(
				"The Village Idiot recovery fixture lost its pending Game Session.");
		var liveInstruction = service.GetCurrentInstruction(gameId);
		if (liveInstruction == null ||
			liveInstruction.InstructionId != pardon.InstructionId ||
			liveInstruction.Semantic != pardon.Semantic)
		{
			throw new InvalidOperationException(
				"The Village Idiot recovery fixture did not reach the live pending pardon announcement.");
		}

		var serializedSession = pendingState.Serialize();
		var persisted = Parse(serializedSession)._payload;
		if (persisted.DomainRecoveryCursor is not null ||
			persisted.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon ||
			persisted.PendingInstruction?.InstructionId != pardon.InstructionId)
		{
			throw new InvalidOperationException(
				"The Village Idiot recovery fixture did not persist a cursorless pending pardon boundary.");
		}

		return new ActorBorrowedVillageIdiotPendingRecoverySnapshot(
			serializedSession,
			gameId,
			pardon,
			villageIdiotCard.Id,
			activation!.ActivationId,
			ActorBorrowedVillageIdiotPardonCommit.ExpectedResourceId);
	}

	internal static ActorBorrowedBearTamerPendingRecoverySnapshot
		CreateActorBorrowedBearTamerPendingGrowlSnapshot(
			IStateChangeObserver sourceObserver)
	{
		ArgumentNullException.ThrowIfNull(sourceObserver);
		var bearTamerCard = new PhysicalCharacterCard(
			Guid.Parse("00000000-0000-0000-0000-000000000301"),
			MainRoleType.BearTamer);
		var setup = new ActorSetupCards(
			version: 15,
			[
				bearTamerCard,
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000302"),
					MainRoleType.Seer),
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000303"),
					MainRoleType.Fox)
			]);
		var config = new GameSessionConfig(
			[
				GameStrings.ActorRoleName,
				"Clockwise Werewolf",
				"Dawn victim",
				"Villager A",
				"Villager B",
				"Villager C"
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
		var sourceSession = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = sourceSession.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		var victimId = players[2].Id;
		foreach (var player in players)
		{
			sourceSession.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: player.Id == werewolfId
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = sourceSession.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!sourceSession.TryRecordPhysicalCharacterCardOwnership(
				sourceSession.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Bear Tamer recovery fixture could not bind Actor's physical card.");
		}

		sourceSession.IdentifyRole([actorId], MainRoleType.Actor);
		if (!sourceSession.TrySpendActorSetupCard(
				actorId,
				bearTamerCard.Id,
				out var activation))
		{
			throw new InvalidOperationException(
				"The Bear Tamer recovery fixture could not activate its setup card.");
		}

		SeedActorBorrowedHunterRecoveryFacts(sourceSession, werewolfId);
		sourceSession.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			victimId);
		sourceSession.TransitionMainPhase(GamePhase.Dawn);
		var serializedSource = Capture(sourceSession)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = RequireActorBorrowedBearTamerInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		var progress = service.ProcessInstruction(
			gameId,
			recoveredStart.CreateResponse());
		ConfirmationInstruction? growl = null;
		for (var step = 0; step < 20 && growl == null; step++)
		{
			if (progress.ModeratorInstruction is ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.AnnounceBearTamerGrowl
				} pendingGrowl)
			{
				growl = pendingGrowl;
				break;
			}

			progress = progress.ModeratorInstruction switch
			{
				ConfirmationInstruction confirmation => service.ProcessInstruction(
					gameId,
					confirmation.CreateResponse()),
				AssignRolesInstruction assignment => service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => sourceSession.GetPlayer(playerId).State
								.CurrentRole ?? throw new InvalidOperationException(
									"The Bear Tamer recovery fixture cannot assign an unknown Dawn role.")))),
				_ => throw new InvalidOperationException(
					"The Bear Tamer recovery fixture left the Dawn continuation before the growl.")
			};
		}

		if (growl is not
			{
				PublicAnnouncement: null,
				AffectedPlayerIds: null
			} ||
			!StringComparer.Ordinal.Equals(
				growl.PrivateInstruction,
				GameStrings.BearTamerGrowlInstruction) ||
			!growl.SoundEffects.SequenceEqual([SoundEffectsEnum.BearGrowl]))
		{
			throw new InvalidOperationException(
				"The Bear Tamer recovery fixture did not reach the canonical growl guidance.");
		}

		var pendingState = service.GetGameStateView(gameId) as GameSession
			?? throw new InvalidOperationException(
				"The Bear Tamer recovery fixture lost its pending Game Session.");
		var liveInstruction = service.GetCurrentInstruction(gameId);
		var currentActivation = pendingState
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		if (liveInstruction == null ||
			liveInstruction.InstructionId != growl.InstructionId ||
			liveInstruction.Semantic != growl.Semantic ||
			currentActivation is not
			{
				ActingRole: MainRoleType.Actor,
				SourceRole: MainRoleType.BearTamer,
				ActingPlayerId: var activeActorId
			} ||
			activeActorId != actorId ||
			currentActivation.SelectedCardId != bearTamerCard.Id ||
			currentActivation.ActivationId != activation!.ActivationId ||
			pendingState.GetActorBorrowedBearTamerGrowlCommits().Count != 0)
		{
			throw new InvalidOperationException(
				"The Bear Tamer recovery fixture did not reach the active borrowed pending growl.");
		}

		var serializedSession = pendingState.Serialize();
		var persisted = Parse(serializedSession)._payload;
		if (persisted.DomainRecoveryCursor is not null ||
			persisted.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl ||
			persisted.PendingInstruction?.InstructionId != growl.InstructionId)
		{
			throw new InvalidOperationException(
				"The Bear Tamer recovery fixture did not persist a cursorless pending growl boundary.");
		}

		return new ActorBorrowedBearTamerPendingRecoverySnapshot(
			serializedSession,
			gameId,
			growl,
			actorId,
			bearTamerCard.Id,
			activation!.ActivationId);
	}

	internal static ActorBorrowedKnightPendingRecoverySnapshot
		CreateActorBorrowedKnightPendingRustySwordAnnouncementSnapshot(
			IStateChangeObserver sourceObserver)
	{
		ArgumentNullException.ThrowIfNull(sourceObserver);
		var knightCard = new PhysicalCharacterCard(
			Guid.Parse("00000000-0000-0000-0000-000000000311"),
			MainRoleType.KnightWithRustySword);
		var setup = new ActorSetupCards(
			version: 16,
			[
				knightCard,
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000312"),
					MainRoleType.BearTamer),
				new PhysicalCharacterCard(
					Guid.Parse("00000000-0000-0000-0000-000000000313"),
					MainRoleType.Seer)
			]);
		var roles = new[]
		{
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Player B", "Player C", "Player D", "Player E", "Player F"],
			roles.ToList(),
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var sourceSession = new GameSession(
			sessionId,
			start,
			config,
			sourceObserver);
		var players = sourceSession.GetPlayers().ToArray();
		for (var index = 0; index < players.Length; index++)
		{
			sourceSession.AssignRole(players[index].Id, roles[index]);
		}

		var actorId = players[0].Id;
		var targetId = players[1].Id;
		var otherWerewolfId = players[2].Id;
		var actorCard = sourceSession.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!sourceSession.TryRecordPhysicalCharacterCardOwnership(
				sourceSession.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture could not bind Actor's physical card.");
		}

		sourceSession.IdentifyRole([actorId], MainRoleType.Actor);
		if (!sourceSession.TrySpendActorSetupCard(
				actorId,
				knightCard.Id,
				out var activation))
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture could not activate its setup card.");
		}

		SeedActorBorrowedKnightRecoveryFacts(
			sourceSession,
			new HashSet<Guid> { targetId, otherWerewolfId });
		sourceSession.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			actorId);
		sourceSession.TransitionMainPhase(GamePhase.Dawn);
		var serializedSource = Capture(sourceSession)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();

		var service = new GameService();
		var gameId = service.RehydrateSession(serializedSource);
		var recoveredStart = RequireActorBorrowedKnightInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		var progress = service.ProcessInstruction(
			gameId,
			recoveredStart.CreateResponse());
		for (var step = 0; step < 30; step++)
		{
			var current = service.GetGameStateView(gameId) as GameSession
				?? throw new InvalidOperationException(
					"The Knight recovery fixture lost its Game Session.");
			if (current.GetCurrentPhase() == GamePhase.Day)
			{
				break;
			}

			progress = progress.ModeratorInstruction switch
			{
				ConfirmationInstruction confirmation => service.ProcessInstruction(
					gameId,
					confirmation.CreateResponse()),
				AssignRolesInstruction assignment => service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => sourceSession.GetPlayer(playerId).State
								.CurrentRole ?? throw new InvalidOperationException(
									"The Knight recovery fixture cannot assign an unknown Dawn role.")))),
				_ => throw new InvalidOperationException(
					"The Knight recovery fixture left the first Dawn continuation before Day.")
			};
		}

		var pendingState = service.GetGameStateView(gameId) as GameSession
			?? throw new InvalidOperationException(
				"The Knight recovery fixture lost its pending Game Session.");
		var schedules = pendingState
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.ToArray();
		if (pendingState.GetCurrentPhase() != GamePhase.Day ||
			schedules is not [var schedule] ||
			schedule.TargetPlayerId != targetId ||
			schedule.ActorSetupCardId != knightCard.Id ||
			schedule.PowerIdentity.ActingPlayerId != actorId ||
			schedule.PowerIdentity.SourceRole !=
				MainRoleType.KnightWithRustySword ||
			!StringComparer.Ordinal.Equals(
				schedule.PowerIdentity.SourcePowerIdentifier,
				ActorBorrowedKnightRustySwordScheduleCommit
					.ExpectedSourcePowerIdentifier) ||
			schedule.PowerIdentity.PowerInstanceOrigin !=
				RolePowerInstanceOrigin.Borrowed)
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture did not create the correlated private schedule.");
		}

		var history = pendingState.GameHistoryLog.ToArray();
		var markerIndex = schedule.PublicMarkerLogIndex;
		var marker = history
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.SingleOrDefault();
		if (marker is null ||
			markerIndex < 0 ||
			markerIndex >= history.Length ||
			!ReferenceEquals(history[markerIndex], marker) ||
			!StringComparer.Ordinal.Equals(
				marker.ToString(),
				"ActorBorrowedRolePowerCommitted") ||
			marker.ToString().Contains(
				actorId.ToString(),
				StringComparison.Ordinal) ||
			marker.ToString().Contains(
				targetId.ToString(),
				StringComparison.Ordinal) ||
			marker.ToString().Contains(
				knightCard.Id.ToString(),
				StringComparison.Ordinal) ||
			marker.ToString().Contains(
				activation!.ActivationId.ToString(),
				StringComparison.Ordinal) ||
			history.OfType<StatusEffectLogEntry>().Any(entry =>
				entry.EffectType == StatusEffectTypes.RustySwordDisease) ||
			pendingState.GetPlayerState(targetId).HasStatusEffect(
				StatusEffectTypes.RustySwordDisease) ||
			pendingState.GameHistoryLog.OfType<NightActionLogEntry>().Any(entry =>
				entry.ActionType == NightActionType.RustySword) ||
			pendingState.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Any(entry =>
				entry.Role == MainRoleType.KnightWithRustySword))
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture exposed its private schedule before the due consequence.");
		}

		pendingState.TransitionMainPhase(GamePhase.Night);
		if (!pendingState.TryExpireActorBorrowedRolePowerActivation())
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture could not expire the completed borrowed activation.");
		}

		CommitActorBorrowedKnightCurrentWerewolfAgentFacts(
			pendingState,
			new HashSet<Guid>());
		var knight = new KnightWithTheRustySwordRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		pendingState.GetOrCreateListener(knight.Id, () => knight);
		var nightHook = new SubPhaseManager<RecoveryHookDriverSubPhase>(
			RecoveryHookDriverSubPhase.Active,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(
					RecoveryHookDriverSubPhase.Complete)
			],
			possibleNextSubPhases: [RecoveryHookDriverSubPhase.Complete]);
		var nightResponse = start.CreateResponse();
		var nightCompleted = false;
		for (var step = 0; step < 20; step++)
		{
			var instruction = nightHook.Execute(pendingState, nightResponse)
				.ModeratorInstruction;
			if (instruction == null)
			{
				nightCompleted = true;
				break;
			}

			nightResponse = instruction switch
			{
				ConfirmationInstruction confirmation =>
					confirmation.CreateResponse(),
				SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic
						.ObserveWerewolfFactionAgentGroup
				} observation => observation.CreateResponse([]),
				_ => throw new InvalidOperationException(
					$"The Knight recovery fixture encountered unexpected following-Night instruction {instruction.Semantic}.")
			};
		}

		if (!nightCompleted ||
			pendingState.GameHistoryLog.OfType<NightActionLogEntry>().Any(entry =>
				entry.ActionType == NightActionType.RustySword) ||
			pendingState.GetPlayerState(targetId).HasStatusEffect(
				StatusEffectTypes.RustySwordDisease))
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture did not carry its private schedule through the ordinary following-Night cadence.");
		}

		CommitActorBorrowedKnightCurrentWerewolfAgentFacts(
			pendingState,
			new HashSet<Guid> { otherWerewolfId });
		pendingState.TransitionMainPhase(GamePhase.Dawn);
		var serializedDueState = Capture(pendingState)
			.RecordActorSetupCardSpend(activation!)
			.WithPendingInstruction(start)
			.Serialize();
		service = new GameService();
		gameId = service.RehydrateSession(serializedDueState);
		pendingState = service.GetGameStateView(gameId) as GameSession
			?? throw new InvalidOperationException(
				"The Knight recovery fixture could not restore the due consequence state.");
		EliminationCascadeRuntimeStore.Configure(
			pendingState,
			[
				new EliminationCascadeReactionBinding(
					knight,
					EliminationCascadeReactionBoundary.PreReveal)
			]);

		var dueStart = RequireActorBorrowedKnightInstruction<
			StartGameConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		progress = service.ProcessInstruction(
			gameId,
			dueStart.CreateResponse());
		ConfirmationInstruction? announcement = null;
		for (var step = 0; step < 30 && announcement == null; step++)
		{
			if (progress.ModeratorInstruction is ConfirmationInstruction
				{
					Semantic: ModeratorInstructionSemantic.AnnounceDawnVictims
				} pendingAnnouncement &&
				pendingAnnouncement.AffectedPlayerIds?.Contains(targetId) == true)
			{
				announcement = pendingAnnouncement;
				break;
			}

			progress = progress.ModeratorInstruction switch
			{
				ConfirmationInstruction confirmation => service.ProcessInstruction(
					gameId,
					confirmation.CreateResponse()),
				AssignRolesInstruction assignment => service.ProcessInstruction(
					gameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							playerId => pendingState.GetPlayer(playerId).State
								.CurrentRole ?? throw new InvalidOperationException(
									"The Knight recovery fixture cannot assign an unknown due Dawn role.")))),
				_ => throw new InvalidOperationException(
					"The Knight recovery fixture left the due Dawn continuation before its announcement.")
			};
		}

		var expectedAnnouncement = GameStrings.MultipleVictimEliminatedAnnounce
			.Format(
				GameStrings.RustySwordDiseaseEliminationAnnouncement.Format(
					pendingState.GetPlayer(targetId).Name));
		if (announcement is null ||
			!StringComparer.Ordinal.Equals(
				announcement.PublicAnnouncement,
				expectedAnnouncement) ||
			announcement.PrivateInstruction is not null ||
			announcement.AffectedPlayerIds is not [var affectedPlayerId] ||
			affectedPlayerId != targetId ||
			announcement.SoundEffects.Count != 0)
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture did not reach the canonical due Rusty Sword announcement.");
		}

		var liveInstruction = service.GetCurrentInstruction(gameId);
		if (liveInstruction?.InstructionId != announcement.InstructionId ||
			liveInstruction.Semantic != announcement.Semantic)
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture did not retain the live due announcement.");
		}

		var serializedSession = pendingState.Serialize();
		var persisted = Parse(serializedSession)._payload;
		if (persisted.DomainRecoveryCursor is not null ||
			persisted.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceDawnVictims ||
			persisted.PendingInstruction?.InstructionId != announcement.InstructionId)
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture did not persist a cursorless due announcement boundary.");
		}

		return new ActorBorrowedKnightPendingRecoverySnapshot(
			serializedSession,
			gameId,
			announcement,
			actorId,
			knightCard.Id,
			activation.ActivationId);
	}

	internal RecoveryPayloadTestDriver RewriteRolesInPlay(
		IEnumerable<MainRoleType> roles)
	{
		ArgumentNullException.ThrowIfNull(roles);
		_payload.RolesInPlay = roles.ToList();
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteSeatingOrder(
		IEnumerable<Guid> playerIds)
	{
		ArgumentNullException.ThrowIfNull(playerIds);
		_payload.SeatingOrder = playerIds.ToList();
		return this;
	}

	internal RecoveryPayloadTestDriver RemovePublicGroupPartition()
	{
		_payload.PublicGroupPartition = null;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePublicGroupPartition(
		IEnumerable<Guid> firstGroupPlayerIds,
		IEnumerable<Guid> secondGroupPlayerIds)
	{
		ArgumentNullException.ThrowIfNull(firstGroupPlayerIds);
		ArgumentNullException.ThrowIfNull(secondGroupPlayerIds);
		_payload.PublicGroupPartition = new PublicGroupPartitionDto
		{
			FirstGroupPlayerIds = firstGroupPlayerIds.ToList(),
			SecondGroupPlayerIds = secondGroupPlayerIds.ToList()
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveAngelExpiry()
	{
		var index = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		if (index < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Angel expiry.");
		}

		_payload.GameHistoryLog.RemoveAt(index);
		return this;
	}

	internal RecoveryPayloadTestDriver DuplicateAngelExpiry()
	{
		var index = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		if (index < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Angel expiry.");
		}

		_payload.GameHistoryLog.Insert(
			index + 1,
			_payload.GameHistoryLog[index]);
		return this;
	}

	internal RecoveryPayloadTestDriver DuplicatePostExpirySimpleVillagerProjection()
	{
		var expiryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		var projectionIndex = _payload.GameHistoryLog.FindIndex(
			expiryIndex + 1,
			entry => entry is AssignRoleLogEntry
			{
				AssignedMainRole: MainRoleType.SimpleVillager
			});
		if (expiryIndex < 0 || projectionIndex < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no post-expiry Simple Villager projection.");
		}

		_payload.GameHistoryLog.Insert(
			projectionIndex + 1,
			_payload.GameHistoryLog[projectionIndex]);
		return this;
	}

	internal RecoveryPayloadTestDriver MoveKnownAngelProjectionToHistoryTail()
	{
		var expiryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is AngelExpiredLogEntry);
		if (expiryIndex < 0 ||
			expiryIndex + 1 >= _payload.GameHistoryLog.Count ||
			_payload.GameHistoryLog[expiryIndex + 1] is not AssignRoleLogEntry
			{
				AssignedMainRole: MainRoleType.SimpleVillager
			} projection)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no immediate known-holder Angel projection.");
		}

		_payload.GameHistoryLog.RemoveAt(expiryIndex + 1);
		_payload.GameHistoryLog.Add(projection);
		return this;
	}

	internal RecoveryPayloadTestDriver AppendAngelVictory(
		int turnNumber,
		GamePhase phase,
		VictoryCheckWindow window)
	{
		_payload.GameHistoryLog.Add(new VictoryConditionMetLogEntry
		{
			Timestamp = _payload.GameHistoryLog[^1].Timestamp.AddTicks(1),
			TurnNumber = turnNumber,
			CurrentPhase = phase,
			GameResult = new SingleFactionGameResult(Faction.Angel),
			VictoryCheckWindow = window
		});
		return this;
	}

	internal RecoveryPayloadTestDriver MismatchOneUseResource(
		Guid cursorResourceId)
	{
		RequireDomainCursor().OneUseResourceId = cursorResourceId;
		return this;
	}

	internal RecoveryPayloadTestDriver OmitSourceRole()
	{
		RequireDomainCursor().SourceRole = null;
		return this;
	}

	internal RecoveryPayloadTestDriver OmitPowerInstanceOrigin()
	{
		RequireDomainCursor().PowerInstanceOrigin = null;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestOneUseAction(
		NightActionType actionType)
	{
		RequireDomainCursor().CommittedActionType = actionType;
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed One-Use Resource action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			ActionType = actionType
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveLatestNightAction(
		NightActionType actionType)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is NightActionLogEntry nightAction &&
			nightAction.ActionType == actionType);
		if (entryIndex < 0)
		{
			throw new InvalidOperationException(
				$"The recovery test payload has no '{actionType}' Night Action.");
		}

		_payload.GameHistoryLog.RemoveAt(entryIndex);
		return this;
	}

	internal RecoveryPayloadTestDriver RetargetLatestOneUseActionAndCursor(
		Guid targetId)
	{
		if (targetId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test target cannot be empty.",
				nameof(targetId));
		}

		RequireDomainCursor().CommittedTargetIds = [targetId];
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed One-Use Resource action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			TargetIds = [targetId]
		};
		return this;
	}

	internal RecoveryPayloadTestDriver
		RetargetLatestRecurringNightActionAndCursor(Guid targetId)
	{
		if (targetId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test target cannot be empty.",
				nameof(targetId));
		}

		RequireDomainCursor().CommittedTargetIds = [targetId];
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is RecurringRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    RecurringRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed recurring Night action.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			TargetIds = [targetId]
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringCursorTargets(
		params Guid[] targetIds)
	{
		ArgumentNullException.ThrowIfNull(targetIds);
		if (targetIds.Length == 0 ||
		    targetIds.Any(targetId => targetId == Guid.Empty) ||
		    targetIds.Distinct().Count() != targetIds.Length)
		{
			throw new ArgumentException(
				"Recovery test targets must contain one or more distinct, non-empty GUIDs.",
				nameof(targetIds));
		}

		RequireDomainCursor().CommittedTargetIds = targetIds.ToList();
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringCursorSourceRole(
		MainRoleType sourceRole)
	{
		RequireDomainCursor().SourceRole = sourceRole;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringActorAndCursor(
		Guid actingPlayerId)
	{
		var cursor = RequireDomainCursor();
		cursor.ActingPlayerId = actingPlayerId;
		cursor.PowerInstanceId = actingPlayerId;
		RewriteLatestRecurringEntry(entry => entry with
		{
			ActingPlayerId = actingPlayerId,
			PowerInstanceId = actingPlayerId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringSourceRoleAndCursor(
		MainRoleType sourceRole)
	{
		RequireDomainCursor().SourceRole = sourceRole;
		RewriteLatestRecurringEntry(entry => entry with
		{
			SourceRole = sourceRole
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringPowerAndCursor(
		string sourcePowerIdentifier)
	{
		RequireDomainCursor().SourcePowerIdentifier =
			sourcePowerIdentifier;
		RewriteLatestRecurringEntry(entry => entry with
		{
			SourcePowerIdentifier = sourcePowerIdentifier
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringInstanceAndCursor(
		Guid powerInstanceId)
	{
		RequireDomainCursor().PowerInstanceId = powerInstanceId;
		RewriteLatestRecurringEntry(entry => entry with
		{
			PowerInstanceId = powerInstanceId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringOriginAndCursor(
		RolePowerInstanceOrigin powerInstanceOrigin)
	{
		RequireDomainCursor().PowerInstanceOrigin = powerInstanceOrigin;
		RewriteLatestRecurringEntry(entry => entry with
		{
			PowerInstanceOrigin = powerInstanceOrigin
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringActionAndCursor(
		NightActionType actionType)
	{
		RequireDomainCursor().CommittedActionType = actionType;
		RewriteLatestRecurringEntry(entry => entry with
		{
			ActionType = actionType
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringPhase(
		GamePhase phase)
	{
		RewriteLatestRecurringEntry(entry => entry with
		{
			CurrentPhase = phase
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringTurnNumber(
		int turnNumber)
	{
		RewriteLatestRecurringEntry(entry => entry with
		{
			TurnNumber = turnNumber
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteSessionTurnNumber(
		int turnNumber)
	{
		_payload.TurnNumber = turnNumber;
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveDomainRecoveryCursor()
	{
		_payload.DomainRecoveryCursor = null;
		return this;
	}

	internal RecoveryPayloadTestDriver
		DowngradeLatestRecurringCommitToLegacyNightAction()
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is RecurringRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    RecurringRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed recurring Night action.");
		}

		_payload.GameHistoryLog[entryIndex] = new NightActionLogEntry
		{
			Timestamp = entry.Timestamp,
			TurnNumber = entry.TurnNumber,
			CurrentPhase = entry.CurrentPhase,
			ActionType = entry.ActionType,
			TargetIds = entry.TargetIds
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteRecurringNextSemantic(
		ModeratorInstructionSemantic semantic)
	{
		RequireDomainCursor().NextInstructionSemantic = semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingConfirmationSemantic(
		ModeratorInstructionSemantic semantic)
	{
		if (_payload.PendingInstruction is not
		    ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId);
		_payload.PendingInstructionSemantic = semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingConfirmationInstructionId(
		Guid instructionId)
	{
		if (instructionId == Guid.Empty ||
		    _payload.PendingInstruction is not ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload requires a pending confirmation and a nonempty Instruction ID.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			instructionId,
			pending.SoundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver
		ReplacePendingConfirmationWithPlayerSelection()
	{
		if (_payload.PendingInstruction is not ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new SelectPlayersInstruction(
			pending.Semantic,
			_payload.Players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Single,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			roleIdentification: null,
			instructionId: pending.InstructionId);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewritePendingConfirmationAffectedPlayer(Guid playerId)
	{
		if (_payload.PendingInstruction is not
		    ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			[playerId],
			pending.InstructionId);
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingConfirmationPresentation(
		string? privateInstruction,
		IReadOnlyList<SoundEffectsEnum>? soundEffects)
	{
		if (_payload.PendingInstruction is not
		    ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			privateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId,
			soundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingConfirmationLocalizedText(
		string? publicAnnouncement,
		string? privateInstruction)
	{
		if (_payload.PendingInstruction is not ConfirmationInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending confirmation.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			publicAnnouncement,
			privateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId,
			pending.SoundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingPlayerSelectionPresentation(
		string? publicAnnouncement,
		string? privateInstruction,
		string? emptySelectionOptionLabel)
	{
		if (_payload.PendingInstruction is not
		    SelectPlayersInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending Player selection.");
		}

		_payload.PendingInstruction = new SelectPlayersInstruction(
			pending.Semantic,
			pending.SelectablePlayerIds.ToHashSet(),
			pending.CountConstraint,
			publicAnnouncement,
			privateInstruction,
			pending.AffectedPlayerIds,
			pending.RoleIdentification,
			pending.InstructionId)
		{
			EmptySelectionOptionLabel = emptySelectionOptionLabel
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingPlayerSelectionCountConstraint(
		NumberRangeConstraint countConstraint)
	{
		if (_payload.PendingInstruction is not
		    SelectPlayersInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending Player selection.");
		}

		_payload.PendingInstruction = new SelectPlayersInstruction(
			pending.Semantic,
			pending.SelectablePlayerIds.ToHashSet(),
			countConstraint,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.RoleIdentification,
			pending.InstructionId)
		{
			EmptySelectionOptionLabel = pending.EmptySelectionOptionLabel
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingPlayerSelectionSelectablePlayerIds(
		IEnumerable<Guid> selectablePlayerIds)
	{
		ArgumentNullException.ThrowIfNull(selectablePlayerIds);
		if (_payload.PendingInstruction is not
		    SelectPlayersInstruction pending)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no pending Player selection.");
		}

		_payload.PendingInstruction = new SelectPlayersInstruction(
			pending.Semantic,
			selectablePlayerIds.ToHashSet(),
			pending.CountConstraint,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.RoleIdentification,
			pending.InstructionId)
		{
			EmptySelectionOptionLabel = pending.EmptySelectionOptionLabel
		};
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteActorBorrowedHunterPendingSelectorPrivateInstruction(
			string privateInstruction)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(privateInstruction);
		if (_payload.PendingInstruction is not SelectPlayersInstruction pending ||
			_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no borrowed Hunter final-shot selector.");
		}

		return RewritePendingPlayerSelectionPresentation(
			pending.PublicAnnouncement,
			privateInstruction,
			pending.EmptySelectionOptionLabel);
	}

	internal RecoveryPayloadTestDriver
		RewriteActorBorrowedElderPendingSuppressionPublicAnnouncement(
			string publicAnnouncement)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(publicAnnouncement);
		if (_payload.PendingInstruction is not ConfirmationInstruction pending ||
			_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic
					.AnnounceVillagerRolePowerSuppression)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no borrowed Elder suppression announcement.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			publicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId,
			pending.SoundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteActorBorrowedScapegoatPendingPresentation(
			ActorBorrowedScapegoatRecoveryStep step)
	{
		var expectedSemantic = step switch
		{
			ActorBorrowedScapegoatRecoveryStep.Reveal =>
				ModeratorInstructionSemantic.RevealScapegoatForTie,
			ActorBorrowedScapegoatRecoveryStep.PermittedVoterSelection =>
				ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
			ActorBorrowedScapegoatRecoveryStep.PermittedVoterAnnouncement =>
				ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters,
			_ => throw new ArgumentOutOfRangeException(nameof(step), step, null)
		};
		if (_payload.PendingInstructionSemantic != expectedSemantic)
		{
			throw new InvalidOperationException(
				"The recovery test payload does not match the requested borrowed Scapegoat step.");
		}

		switch (step, _payload.PendingInstruction)
		{
			case (ActorBorrowedScapegoatRecoveryStep.Reveal,
				ConfirmationInstruction reveal):
				_payload.PendingInstruction = new ConfirmationInstruction(
					expectedSemantic,
					reveal.PublicAnnouncement,
					"Tampered private borrowed reveal presentation.",
					reveal.AffectedPlayerIds,
					reveal.InstructionId,
					reveal.SoundEffects);
				return this;
			case (ActorBorrowedScapegoatRecoveryStep.PermittedVoterSelection,
				SelectPlayersInstruction selection):
				_payload.PendingInstruction = new SelectPlayersInstruction(
					expectedSemantic,
					selection.SelectablePlayerIds.ToHashSet(),
					selection.CountConstraint,
					selection.PublicAnnouncement,
					"Tampered private permitted-voter selector presentation.",
					selection.AffectedPlayerIds,
					selection.RoleIdentification,
					selection.InstructionId)
				{
					EmptySelectionOptionLabel =
						selection.EmptySelectionOptionLabel
				};
				return this;
			case (ActorBorrowedScapegoatRecoveryStep.PermittedVoterAnnouncement,
				ConfirmationInstruction announcement):
				_payload.PendingInstruction = new ConfirmationInstruction(
					expectedSemantic,
					"Tampered public permitted-voter announcement.",
					announcement.PrivateInstruction,
					announcement.AffectedPlayerIds,
					announcement.InstructionId,
					announcement.SoundEffects);
				return this;
			default:
				throw new InvalidOperationException(
					"The recovery test payload does not match the requested borrowed Scapegoat step.");
		}
	}

	internal RecoveryPayloadTestDriver
		RewriteActorBorrowedVillageIdiotPendingPardonPublicAnnouncement(
			string publicAnnouncement)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(publicAnnouncement);
		var pendingSemantic = _payload.PendingInstructionSemantic;
		if (_payload.PendingInstruction is not ConfirmationInstruction pending ||
			pendingSemantic is null ||
			pendingSemantic.Value !=
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no borrowed Village Idiot pardon announcement.");
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pendingSemantic.Value,
			publicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId,
			pending.SoundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteActorBorrowedBearTamerPendingGrowlPresentation(
			ActorBorrowedBearTamerRecoveryTamper tamper)
	{
		var pendingSemantic = _payload.PendingInstructionSemantic;
		if (_payload.PendingInstruction is not ConfirmationInstruction pending ||
			pendingSemantic is null ||
			pendingSemantic.Value !=
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no borrowed Bear Tamer growl guidance.");
		}

		var publicAnnouncement = pending.PublicAnnouncement;
		var privateInstruction = pending.PrivateInstruction;
		IReadOnlyList<SoundEffectsEnum> soundEffects = pending.SoundEffects;
		switch (tamper)
		{
			case ActorBorrowedBearTamerRecoveryTamper.PublicAnnouncement:
				publicAnnouncement =
					"Tampered public borrowed growl announcement.";
				break;
			case ActorBorrowedBearTamerRecoveryTamper.PrivateGuidance:
				privateInstruction =
					"Tampered private borrowed growl guidance.";
				break;
			case ActorBorrowedBearTamerRecoveryTamper.SoundEffect:
				soundEffects = [];
				break;
			default:
				throw new ArgumentOutOfRangeException(
					nameof(tamper),
					tamper,
					null);
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pendingSemantic.Value,
			publicAnnouncement,
			privateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId,
			soundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver
		ExpireActorBorrowedBearTamerPendingGrowlActivation()
	{
		RequireCursorlessStableBoundary();
		if (_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl ||
			_payload.ActiveActorBorrowedRolePowerActivation is not
			{
				SourceRole: MainRoleType.BearTamer
			})
		{
			throw new InvalidOperationException(
				"The recovery test payload has no active borrowed Bear Tamer pending growl.");
		}

		var timestamp = _payload.GameHistoryLog.Last().Timestamp;
		_payload.GameHistoryLog.Add(
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = timestamp.AddTicks(1),
				TurnNumber = _payload.TurnNumber,
				CurrentPhase = GamePhase.Night
			});
		_payload.ActiveActorBorrowedRolePowerActivation = null;
		return this;
	}

	internal RecoveryPayloadTestDriver
		ReplaceActorBorrowedBearTamerPendingGrowlActivation()
	{
		RequireCursorlessStableBoundary();
		if (_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl ||
			_payload.ActiveActorBorrowedRolePowerActivation is not
			{
				SourceRole: MainRoleType.BearTamer
			} active)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no active borrowed Bear Tamer pending growl.");
		}

		var spends = _payload.ActorSetupCardSpends
			?? throw new InvalidOperationException(
				"The recovery test payload has no Actor Setup Card spends.");
		var replacementCard = _payload.ActorSetupCards?.Cards?
			.FirstOrDefault(card =>
				card.PrintedRole != MainRoleType.BearTamer &&
				spends.All(spend => spend.CardId != card.Id))
			?? throw new InvalidOperationException(
				"The recovery test payload has no unspent replacement Actor Setup Card.");
		var replacementActivationId = Guid.NewGuid();
		var timestamp = _payload.GameHistoryLog.Last().Timestamp;
		_payload.GameHistoryLog.Add(
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = timestamp.AddTicks(1),
				TurnNumber = _payload.TurnNumber,
				CurrentPhase = GamePhase.Night
			});
		spends.Add(new ActorSetupCardSpendDto
		{
			CardId = replacementCard.Id,
			ActivationId = replacementActivationId
		});
		_payload.ActiveActorBorrowedRolePowerActivation =
			new ActorBorrowedRolePowerActivationDto
			{
				ActivationId = replacementActivationId,
				ActingPlayerId = active.ActingPlayerId,
				ActingRole = MainRoleType.Actor,
				SelectedCardId = replacementCard.Id,
				SourceRole = replacementCard.PrintedRole
			};
		_payload.GameHistoryLog.Add(new ActorSetupCardSpendCommittedLogEntry
		{
			Timestamp = timestamp.AddTicks(2),
			TurnNumber = _payload.TurnNumber,
			CurrentPhase = GamePhase.Night
		});
		return this;
	}

	internal RecoveryPayloadTestDriver
		ExpireActorBorrowedScapegoatPendingRevealActivation()
	{
		RequireCursorlessStableBoundary();
		if (_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.RevealScapegoatForTie ||
			_payload.ActiveActorBorrowedRolePowerActivation is not
			{
				SourceRole: MainRoleType.Scapegoat
			})
		{
			throw new InvalidOperationException(
				"The recovery test payload has no active borrowed Scapegoat pending reveal.");
		}

		var timestamp = _payload.GameHistoryLog.Last().Timestamp;
		_payload.GameHistoryLog.Add(
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = timestamp.AddTicks(1),
				TurnNumber = _payload.TurnNumber,
				CurrentPhase = GamePhase.Night
			});
		_payload.ActiveActorBorrowedRolePowerActivation = null;
		return this;
	}

	internal RecoveryPayloadTestDriver
		ReplaceActorBorrowedScapegoatPendingRevealActivation()
	{
		RequireCursorlessStableBoundary();
		if (_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.RevealScapegoatForTie ||
			_payload.ActiveActorBorrowedRolePowerActivation is not
			{
				SourceRole: MainRoleType.Scapegoat
			} active)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no active borrowed Scapegoat pending reveal.");
		}

		var spends = _payload.ActorSetupCardSpends
			?? throw new InvalidOperationException(
				"The recovery test payload has no Actor Setup Card spends.");
		var replacementCard = _payload.ActorSetupCards?.Cards?
			.FirstOrDefault(card =>
				card.PrintedRole != MainRoleType.Scapegoat &&
				spends.All(spend => spend.CardId != card.Id))
			?? throw new InvalidOperationException(
				"The recovery test payload has no unspent replacement Actor Setup Card.");
		var replacementActivationId = Guid.NewGuid();
		var timestamp = _payload.GameHistoryLog.Last().Timestamp;
		_payload.GameHistoryLog.Add(
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = timestamp.AddTicks(1),
				TurnNumber = _payload.TurnNumber,
				CurrentPhase = GamePhase.Night
			});
		spends.Add(new ActorSetupCardSpendDto
		{
			CardId = replacementCard.Id,
			ActivationId = replacementActivationId
		});
		_payload.ActiveActorBorrowedRolePowerActivation =
			new ActorBorrowedRolePowerActivationDto
			{
				ActivationId = replacementActivationId,
				ActingPlayerId = active.ActingPlayerId,
				ActingRole = MainRoleType.Actor,
				SelectedCardId = replacementCard.Id,
				SourceRole = replacementCard.PrintedRole
			};
		_payload.GameHistoryLog.Add(new ActorSetupCardSpendCommittedLogEntry
		{
			Timestamp = timestamp.AddTicks(2),
			TurnNumber = _payload.TurnNumber,
			CurrentPhase = GamePhase.Night
		});
		return this;
	}

	internal RecoveryPayloadTestDriver
		ExpireActorBorrowedVillageIdiotPendingPardonActivation()
	{
		RequireCursorlessStableBoundary();
		if (_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon ||
			_payload.ActiveActorBorrowedRolePowerActivation is not
			{
				SourceRole: MainRoleType.VillageIdiot
			})
		{
			throw new InvalidOperationException(
				"The recovery test payload has no active borrowed Village Idiot pending pardon.");
		}

		var timestamp = _payload.GameHistoryLog.Last().Timestamp;
		_payload.GameHistoryLog.Add(
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = timestamp.AddTicks(1),
				TurnNumber = _payload.TurnNumber,
				CurrentPhase = GamePhase.Night
			});
		_payload.ActiveActorBorrowedRolePowerActivation = null;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RemoveActorBorrowedVillageIdiotPendingPardonLineage()
	{
		RequireCursorlessStableBoundary();
		if (_payload.PendingInstructionSemantic !=
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon ||
			_payload.ActorBorrowedVillageIdiotPardonCommits is not
				[var commit] ||
			commit.PublicMarkerLogIndex < 0 ||
			commit.PublicMarkerLogIndex >= _payload.GameHistoryLog.Count ||
			_payload.GameHistoryLog[commit.PublicMarkerLogIndex] is not
				ActorBorrowedRolePowerCommittedLogEntry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no correlated borrowed Village Idiot pardon lineage.");
		}

		_payload.ActorBorrowedVillageIdiotPardonCommits.Clear();
		_payload.GameHistoryLog.RemoveAt(commit.PublicMarkerLogIndex);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteActorBorrowedKnightPendingRustySwordAnnouncementPresentation(
			ActorBorrowedKnightRecoveryTamper tamper)
	{
		var pendingSemantic = _payload.PendingInstructionSemantic;
		if (_payload.PendingInstruction is not ConfirmationInstruction pending ||
			pendingSemantic is null ||
			pendingSemantic.Value !=
				ModeratorInstructionSemantic.AnnounceDawnVictims)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no borrowed Knight due announcement.");
		}

		var publicAnnouncement = pending.PublicAnnouncement;
		var privateInstruction = pending.PrivateInstruction;
		IReadOnlyList<Guid>? affectedPlayerIds = pending.AffectedPlayerIds;
		IReadOnlyList<SoundEffectsEnum> soundEffects = pending.SoundEffects;
		switch (tamper)
		{
			case ActorBorrowedKnightRecoveryTamper.PublicAnnouncement:
				publicAnnouncement =
					"Tampered public borrowed Rusty Sword announcement.";
				break;
			case ActorBorrowedKnightRecoveryTamper.PrivateGuidance:
				privateInstruction =
					"Tampered private borrowed Rusty Sword guidance.";
				break;
			case ActorBorrowedKnightRecoveryTamper.AffectedPlayer:
				var affected = pending.AffectedPlayerIds?.ToHashSet() ?? [];
				affectedPlayerIds =
				[
					_payload.Players.Select(player => player.Id)
						.First(playerId => !affected.Contains(playerId))
				];
				break;
			case ActorBorrowedKnightRecoveryTamper.SoundEffect:
				soundEffects = [SoundEffectsEnum.BearGrowl];
				break;
			default:
				throw new ArgumentOutOfRangeException(
					nameof(tamper),
					tamper,
					null);
		}

		_payload.PendingInstruction = new ConfirmationInstruction(
			pendingSemantic.Value,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			pending.InstructionId,
			soundEffects);
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestStutteringJudgeAction(
		DayPowerType actionType)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is
				OneUseRolePowerDayActionCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			OneUseRolePowerDayActionCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed Stuttering Judge vote.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			ActionType = actionType
		};
		return this;
	}

	internal RecoveryPayloadTestDriver TargetLatestStutteringJudgeAction()
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is OneUseRolePowerDayActionCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    OneUseRolePowerDayActionCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed Stuttering Judge vote.");
		}

		var targetId = _payload.Players
			.Select(player => player.Id)
			.First(id => id != entry.ResourceIdentity.ActingPlayerId);
		_payload.GameHistoryLog[entryIndex] = entry with
		{
			TargetIds = [targetId]
		};
		return this;
	}

	internal RecoveryPayloadTestDriver AddCrossTypeDuplicateOfStutteringJudgeResource()
	{
		var judgeCommit = _payload.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.LastOrDefault()
			?? throw new InvalidOperationException(
				"The recovery test payload has no committed Stuttering Judge vote.");
		var resourceIdentity = judgeCommit.ResourceIdentity;
		var targetId = _payload.Players
			.Select(player => player.Id)
			.First(id => id != resourceIdentity.ActingPlayerId);
		_payload.GameHistoryLog.Add(new OneUseRolePowerCommittedLogEntry
		{
			Timestamp = judgeCommit.Timestamp.AddTicks(1),
			TurnNumber = judgeCommit.TurnNumber,
			CurrentPhase = judgeCommit.CurrentPhase,
			ActionType = NightActionType.WitchKill,
			TargetIds = [targetId],
			ActingPlayerId = resourceIdentity.ActingPlayerId,
			SourceRole = resourceIdentity.SourceRole,
			SourcePowerIdentifier =
				resourceIdentity.SourcePowerIdentifier,
			PowerInstanceId = resourceIdentity.PowerInstanceId,
			PowerInstanceOrigin =
				resourceIdentity.PowerInstanceOrigin,
			OneUseResourceId = resourceIdentity.OneUseResourceId
		});
		return this;
	}

	internal RecoveryPayloadTestDriver
		InvalidateLatestVoterEligibilityRestrictionTurn()
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is VoterEligibilityRestrictionCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			VoterEligibilityRestrictionCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed voter-eligibility restriction.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			AppliesOnTurnNumber = entry.TurnNumber
		};
		return this;
	}

	internal RecoveryPayloadTestDriver RemoveInitialBeneficiaryClosureFact(
		Guid playerId)
	{
		if (playerId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test Player cannot be empty.",
				nameof(playerId));
		}

		var entryIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is FactionFactsCommittedLogEntry facts &&
			facts.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    FactionFactsCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Initial Beneficiary Closure.");
		}

		var matchingFacts = entry.Facts
			.Where(fact =>
				fact.Type == FactionFactType.Beneficiary &&
				fact.PlayerId == playerId)
			.ToArray();
		if (matchingFacts.Length != 1)
		{
			throw new InvalidOperationException(
				"The recovery test payload must have exactly one matching Beneficiary fact.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			Facts = entry.Facts
				.Where(fact =>
					fact.Type != FactionFactType.Beneficiary ||
					fact.PlayerId != playerId)
				.ToImmutableArray()
		};
		var player = _payload.Players.SingleOrDefault(
			candidate => candidate.Id == playerId)
			?? throw new InvalidOperationException(
				"The recovery test payload has no matching Player.");
		player.FactionBeneficiary = FactionBeneficiaryKnowledge.Unknown;
		return this;
	}

	internal RecoveryPayloadTestDriver
		SwapInitialBeneficiaryClosureAssignmentsAndCaches(
			Guid firstPlayerId,
			Guid secondPlayerId)
	{
		if (firstPlayerId == Guid.Empty ||
		    secondPlayerId == Guid.Empty ||
		    firstPlayerId == secondPlayerId)
		{
			throw new ArgumentException(
				"Recovery test Players must be distinct and non-empty.");
		}

		var entryIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is FactionFactsCommittedLogEntry facts &&
			facts.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    FactionFactsCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Initial Beneficiary Closure.");
		}

		var firstFact = RequireSingleBeneficiaryFact(
			entry,
			firstPlayerId);
		var secondFact = RequireSingleBeneficiaryFact(
			entry,
			secondPlayerId);
		_payload.GameHistoryLog[entryIndex] = entry with
		{
			Facts = entry.Facts
				.Select(fact =>
				{
					if (fact.PlayerId == firstPlayerId)
					{
						return FactionFact.Beneficiary(
							firstPlayerId,
							secondFact.Faction,
							fact.EffectiveBoundary,
							fact.BeneficiaryPrecedence!.Value);
					}

					return fact.PlayerId == secondPlayerId
						? FactionFact.Beneficiary(
							secondPlayerId,
							firstFact.Faction,
							fact.EffectiveBoundary,
							fact.BeneficiaryPrecedence!.Value)
						: fact;
				})
				.ToImmutableArray()
		};
		_payload.Players.Single(player => player.Id == firstPlayerId)
			.FactionBeneficiary =
			FactionBeneficiaryKnowledge.Known(secondFact.Faction);
		_payload.Players.Single(player => player.Id == secondPlayerId)
			.FactionBeneficiary =
			FactionBeneficiaryKnowledge.Known(firstFact.Faction);
		return this;
	}

	internal RecoveryPayloadTestDriver ReplacePendingInstructionWithConfirmation()
	{
		var pending = _payload.PendingInstruction
			?? throw new InvalidOperationException(
				"The recovery test payload has no pending instruction.");
		_payload.PendingInstruction = new ConfirmationInstruction(
			pending.Semantic,
			pending.PublicAnnouncement,
			pending.PrivateInstruction,
			pending.AffectedPlayerIds,
			pending.InstructionId);
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteCurrentPhase(GamePhase phase)
	{
		_payload.PhaseStateCache.CurrentPhase = phase;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteSubPhase(Enum subPhase)
	{
		ArgumentNullException.ThrowIfNull(subPhase);
		_payload.PhaseStateCache.SubPhase = subPhase.ToString();
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteDurableAndTransientContinuation(
		string activeSubPhaseStage,
		IEnumerable<string> completedSubPhaseStages,
		ListenerIdentifier listener,
		string listenerState)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(activeSubPhaseStage);
		ArgumentNullException.ThrowIfNull(completedSubPhaseStages);
		ArgumentNullException.ThrowIfNull(listener);
		ArgumentException.ThrowIfNullOrWhiteSpace(listenerState);

		_payload.PhaseStateCache.ActiveSubPhaseStage = activeSubPhaseStage;
		_payload.PhaseStateCache.CompletedSubPhaseStages =
			completedSubPhaseStages.ToList();
		_payload.PhaseStateCache.CurrentListenerId = listener.ListenerId;
		_payload.PhaseStateCache.CurrentListenerType =
			listener.ListenerType.ToString();
		_payload.PhaseStateCache.CurrentListenerState = listenerState;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePendingInstructionSemanticCheckpoint(
		ModeratorInstructionSemantic semantic)
	{
		_payload.PendingInstructionSemantic = semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteAcceptedObservationCursorVersion(
		int version)
	{
		RequireAcceptedObservationCursor().Version = version;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteAcceptedObservationCursorNextSemantic(
			ModeratorInstructionSemantic semantic)
	{
		RequireAcceptedObservationCursor().NextInstructionSemantic = semantic;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteAcceptedObservationCursorContinuationRole(MainRoleType role)
	{
		RequireAcceptedObservationCursor().ContinuationRole = role;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestScheduledObservationSourceIdentifier(string identifier)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is FactionFactsCommittedLogEntry
			{
				Source.Kind: FactionFactSourceKind.ScheduledObservation
			});
		if (entryIndex < 0 ||
			_payload.GameHistoryLog[entryIndex] is not
				FactionFactsCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no scheduled observation.");
		}

		_payload.GameHistoryLog[entryIndex] = entry with
		{
			Source = new FactionFactSource(entry.Source.Kind, identifier)
		};
		return this;
	}

	internal StatusEffectTypes GetActiveEffects(Guid playerId) =>
		_payload.Players.Single(player => player.Id == playerId).ActiveEffects;

	internal RecoveryPayloadTestDriver RewriteActiveEffects(
		Guid playerId,
		StatusEffectTypes activeEffects)
	{
		_payload.Players.Single(player => player.Id == playerId).ActiveEffects =
			activeEffects;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteCurrentRole(
		Guid playerId,
		MainRoleType currentRole)
	{
		_payload.Players.Single(player => player.Id == playerId).MainRole =
			currentRole;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteModeratorKnownRole(
		Guid playerId,
		MainRoleType? role)
	{
		_payload.Players.Single(player => player.Id == playerId)
			.ModeratorKnownRole = role;
		return this;
	}

	internal RecoveryPayloadTestDriver RewritePubliclyRevealedRole(
		Guid playerId,
		MainRoleType? role)
	{
		_payload.Players.Single(player => player.Id == playerId)
			.PubliclyRevealedRole = role;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteVotingState(
		Guid playerId,
		bool hasVotingRight,
		int durableVotingPower)
	{
		var player = _payload.Players.Single(player => player.Id == playerId);
		player.HasVotingRight = hasVotingRight;
		player.DurableVotingPower = durableVotingPower;
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestPermanentRoleSwapSource(
		FactionFactSourceKind sourceKind)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Source = new FactionFactSource(
				sourceKind,
				entry.Source.Identifier)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapSourceIdentifier(string identifier)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Source = new FactionFactSource(entry.Source.Kind, identifier)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestPermanentRoleSwapFacts(
		Func<ImmutableArray<FactionFact>, ImmutableArray<FactionFact>> rewrite)
	{
		ArgumentNullException.ThrowIfNull(rewrite);
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Facts = rewrite(entry.Facts)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestPermanentRoleSwapPolicy(
		Func<PermanentRoleSwapPolicy, PermanentRoleSwapPolicy> rewrite)
	{
		ArgumentNullException.ThrowIfNull(rewrite);
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Policy = rewrite(entry.Policy)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestThiefSwapAcquiredCard(
		Guid replacementCardId)
	{
		var swap = RequireLatestPermanentRoleSwap();
		var replacementCard = RequireRoleLockInCard(replacementCardId);
		var previousAcquiredCardId = swap.PhysicalCards.AcquiredCardId;
		var previousAcquired = RequirePhysicalCardState(previousAcquiredCardId);
		var replacement = RequirePhysicalCardState(replacementCardId);
		if (replacement.Zone != PhysicalCharacterCardZone.DealPool ||
		    replacement.OwnerPlayerId is not null)
		{
			throw new InvalidOperationException(
				"The replacement acquired card must be unused in the Deal Pool.");
		}

		previousAcquired.Zone = RequireOfferZone(previousAcquiredCardId);
		previousAcquired.OwnerPlayerId = null;
		replacement.Zone = PhysicalCharacterCardZone.PlayerOwned;
		replacement.OwnerPlayerId = swap.PlayerId;
		var player = _payload.Players.Single(player => player.Id == swap.PlayerId);
		player.MainRole = replacementCard.PrintedRole;
		player.ModeratorKnownRole = replacementCard.PrintedRole;
		player.PhysicalCharacterCardId = replacementCardId;
		player.PhysicalCharacterCardRole = replacementCard.PrintedRole;
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			NewCurrentRole = replacementCard.PrintedRole,
			PhysicalCards = new PermanentRoleSwapCardMovement(
				entry.PhysicalCards.OutgoingOwnedCardId,
				replacementCardId,
				entry.PhysicalCards.AdditionalSetAsideCardIds)
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteLatestThiefSwapUnchosenCard(
		Guid replacementCardId)
	{
		var swap = RequireLatestPermanentRoleSwap();
		var previousUnchosenCardId = swap.PhysicalCards
			.AdditionalSetAsideCardIds.Single();
		var previousUnchosen = RequirePhysicalCardState(previousUnchosenCardId);
		var replacement = RequirePhysicalCardState(replacementCardId);
		if (replacement.Zone != PhysicalCharacterCardZone.DealPool ||
		    replacement.OwnerPlayerId is not null)
		{
			throw new InvalidOperationException(
				"The replacement unchosen card must be unused in the Deal Pool.");
		}

		previousUnchosen.Zone = RequireOfferZone(previousUnchosenCardId);
		previousUnchosen.OwnerPlayerId = null;
		replacement.Zone = PhysicalCharacterCardZone.SetAside;
		replacement.OwnerPlayerId = null;
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			PhysicalCards = new PermanentRoleSwapCardMovement(
				entry.PhysicalCards.OutgoingOwnedCardId,
				entry.PhysicalCards.AcquiredCardId,
				[replacementCardId])
		});
		return this;
	}

	internal RecoveryPayloadTestDriver RewriteThiefOfferPrintedRoles(
		MainRoleType offer1Role,
		MainRoleType offer2Role)
	{
		var lockIn = _payload.RoleLockIn
			?? throw new InvalidOperationException(
				"The recovery test payload has no Role Lock-In.");
		if (lockIn.Offer1CardId is not { } offer1CardId ||
		    lockIn.Offer2CardId is not { } offer2CardId)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Thief offers.");
		}

		RewriteCard(offer1CardId, offer1Role);
		RewriteCard(offer2CardId, offer2Role);
		return this;

		void RewriteCard(Guid cardId, MainRoleType printedRole)
		{
			var index = lockIn.RoleComposition.FindIndex(card => card.Id == cardId);
			if (index < 0)
			{
				throw new InvalidOperationException(
					"The recovery test payload is missing a Thief offer card.");
			}

			lockIn.RoleComposition[index] =
				new PhysicalCharacterCard(cardId, printedRole);
			var persistedState = RequirePhysicalCardState(cardId);
			persistedState.Zone = PhysicalCharacterCardZone.SetAside;
			persistedState.OwnerPlayerId = null;
		}
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapBeneficiaryAndCache(Faction faction)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Facts = entry.Facts.Select(fact =>
				fact.Type == FactionFactType.Beneficiary
					? FactionFact.Beneficiary(
						fact.PlayerId,
						faction,
						fact.EffectiveBoundary,
						fact.BeneficiaryPrecedence!.Value)
					: fact).ToImmutableArray()
		});
		var swap = _payload.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Last();
		_payload.Players.Single(player => player.Id == swap.PlayerId)
			.FactionBeneficiary = FactionBeneficiaryKnowledge.Known(faction);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapAgentAndCache(
			Faction faction,
			FactionAgentKnowledge agentKnowledge)
	{
		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			Facts = entry.Facts.Select(fact =>
				fact.Type == FactionFactType.Agent && fact.Faction == faction
					? FactionFact.Agent(
						fact.PlayerId,
						faction,
						agentKnowledge,
						fact.EffectiveBoundary)
					: fact).ToImmutableArray()
		});
		var swap = _payload.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Last();
		_payload.Players.Single(player => player.Id == swap.PlayerId)
			.FactionAgentKnowledge![faction] = agentKnowledge;
		return this;
	}

	internal RecoveryPayloadTestDriver
		RewriteLatestPermanentRoleSwapPowerInstanceId(Guid powerInstanceId)
	{
		if (powerInstanceId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test power instance cannot be empty.",
				nameof(powerInstanceId));
		}

		RewriteLatestPermanentRoleSwap(entry => entry with
		{
			NewPowerInstanceId = powerInstanceId,
			Source = new FactionFactSource(
				FactionFactSourceKind.ExplicitTransition,
				$"permanent-role-swap:{entry.PlayerId:N}:{powerInstanceId:N}")
		});
		return this;
	}

	internal RecoveryPayloadTestDriver AppendPublicRolePowerCommit(
		GameLogEntryBase entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		if (entry is not RecurringRolePowerCommittedLogEntry and
			not TargetPrivateRolePowerCommittedLogEntry and
			not IOneUseRolePowerCommittedLogEntry)
		{
			throw new ArgumentException(
				"The recovery test entry is not a public Role Power commit.",
				nameof(entry));
		}

		_payload.GameHistoryLog.Add(entry);
		return this;
	}

	internal RecoveryPayloadTestDriver
		RemoveLatestActorBorrowedRolePowerMarker()
	{
		var markerIndex = _payload.GameHistoryLog.FindLastIndex(entry =>
			entry is ActorBorrowedRolePowerCommittedLogEntry);
		if (markerIndex < 0)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Actor borrowed Role Power marker.");
		}

		_payload.GameHistoryLog.RemoveAt(markerIndex);
		return this;
	}

	internal RecoveryPayloadTestDriver RetargetActorBorrowedDefenderCommit(
		Guid targetPlayerId)
	{
		if (targetPlayerId == Guid.Empty)
		{
			throw new ArgumentException(
				"A recovery test target Player identity cannot be empty.",
				nameof(targetPlayerId));
		}

		_payload.ActorBorrowedDefenderProtectionCommits.Single().TargetPlayerId =
			targetPlayerId;
		return this;
	}

	internal RecoveryPayloadTestDriver MutateActorBorrowedPrivateCommit(
		ActorBorrowedPrivateCommitMutation mutation)
	{
		if (mutation is
			ActorBorrowedPrivateCommitMutation.HunterFinalShotTarget or
			ActorBorrowedPrivateCommitMutation.ElderResistanceNightActionIndex or
			ActorBorrowedPrivateCommitMutation.ElderSuppressionAnnouncement or
			ActorBorrowedPrivateCommitMutation.ScapegoatTiePowerLineage or
			ActorBorrowedPrivateCommitMutation.ScapegoatRestrictionAnnouncement or
			ActorBorrowedPrivateCommitMutation.VillageIdiotPardonActingPlayerLineage or
			ActorBorrowedPrivateCommitMutation.BearTamerGrowlActingPlayerLineage or
			ActorBorrowedPrivateCommitMutation.KnightRustySwordTarget)
		{
			RequireStableRecoveryBoundary();
		}
		else
		{
			RequireCursorlessStableBoundary();
		}

		switch (mutation)
		{
			case ActorBorrowedPrivateCommitMutation.SeerTarget:
			{
				var commit = _payload.ActorBorrowedSeerCheckCommits.Single();
				commit.TargetPlayerId = RequireAlternatePlayerId(
					commit.TargetPlayerId,
					commit.PowerIdentity.ActingPlayerId);
				break;
			}
			case ActorBorrowedPrivateCommitMutation.SeerResult:
			{
				var commit = _payload.ActorBorrowedSeerCheckCommits.Single();
				commit.TargetAgentKnowledge = commit.TargetAgentKnowledge ==
					FactionAgentKnowledge.KnownAgent
						? FactionAgentKnowledge.KnownNonAgent
						: FactionAgentKnowledge.KnownAgent;
				break;
			}
			case ActorBorrowedPrivateCommitMutation.DefenderTarget:
			{
				var commit =
					_payload.ActorBorrowedDefenderProtectionCommits.Single();
				commit.TargetPlayerId = RequireAlternatePlayerId(
					commit.TargetPlayerId);
				break;
			}
			case ActorBorrowedPrivateCommitMutation.FoxCenter:
			{
				var commit = _payload.ActorBorrowedFoxCheckCommits.Single();
				commit.CenterPlayerId = RequireAlternatePlayerId(
					commit.CenterPlayerId);
				break;
			}
			case ActorBorrowedPrivateCommitMutation.FoxResultAndResource:
			{
				var commit = _payload.ActorBorrowedFoxCheckCommits.Single();
				if (commit.NeighborhoodAgentKnowledge ==
					FactionAgentKnowledge.KnownAgent)
				{
					commit.NeighborhoodAgentKnowledge =
						FactionAgentKnowledge.KnownNonAgent;
					commit.SpentResourceIdentity = CreateResourceIdentity(
						commit.PowerIdentity,
						Guid.NewGuid());
				}
				else
				{
					commit.NeighborhoodAgentKnowledge =
						FactionAgentKnowledge.KnownAgent;
					commit.SpentResourceIdentity = null;
				}
				break;
			}
			case ActorBorrowedPrivateCommitMutation.WitchUseTarget:
			{
				var commit = _payload.ActorBorrowedWitchPotionUseCommits.Single();
				commit.TargetPlayerId = RequireAlternatePlayerId(
					commit.TargetPlayerId,
					commit.PowerIdentity.ActingPlayerId);
				break;
			}
			case ActorBorrowedPrivateCommitMutation.WitchUseResource:
			{
				var commit = _payload.ActorBorrowedWitchPotionUseCommits.Single();
				commit.SpentResourceIdentity = commit.SpentResourceIdentity with
				{
					OneUseResourceId = AlternateWitchResourceId(
						commit.SpentResourceIdentity.OneUseResourceId)
				};
				break;
			}
			case ActorBorrowedPrivateCommitMutation.WitchDeclineResource:
			{
				var commit =
					_payload.ActorBorrowedWitchPotionDeclineCommits.Single();
				commit.OfferedResourceIdentity = commit.OfferedResourceIdentity with
				{
					OneUseResourceId = AlternateWitchResourceId(
						commit.OfferedResourceIdentity.OneUseResourceId)
				};
				break;
			}
			case ActorBorrowedPrivateCommitMutation.CupidPair:
			{
				var commit = _payload.ActorBorrowedCupidLoversCommits.Single();
				var alternatePair = _payload.Players
					.Select(player => player.Id)
					.Where(playerId =>
						playerId != commit.FirstPlayerId &&
						playerId != commit.SecondPlayerId)
					.Order()
					.Take(2)
					.ToArray();
				if (alternatePair.Length != 2)
				{
					throw new InvalidOperationException(
						"The recovery test payload has no alternate Lovers pair.");
				}

				commit.FirstPlayerId = alternatePair[0];
				commit.SecondPlayerId = alternatePair[1];
				foreach (var player in _payload.Players)
				{
					player.ActiveEffects = alternatePair.Contains(player.Id)
						? player.ActiveEffects | StatusEffectTypes.Lovers
						: player.ActiveEffects & ~StatusEffectTypes.Lovers;
				}
				break;
			}
			case ActorBorrowedPrivateCommitMutation.CupidDisposition:
			{
				var commit = _payload.ActorBorrowedCupidLoversCommits.Single();
				commit.Disposition = commit.Disposition ==
					ActorBorrowedCupidLoversDisposition.SameFaction
						? ActorBorrowedCupidLoversDisposition.CrossFaction
						: ActorBorrowedCupidLoversDisposition.SameFaction;
				break;
			}
			case ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage:
			{
				var commit = _payload
					.ActorBorrowedStutteringJudgeSignalSetupCommits.Single();
				var activationId = Guid.NewGuid();
				commit.PowerIdentity = commit.PowerIdentity with
				{
					PowerInstanceId = activationId
				};
				_payload.ActorSetupCardSpends!.Single(spend =>
					spend.CardId == commit.ActorSetupCardId).ActivationId = activationId;
				var active = _payload.ActiveActorBorrowedRolePowerActivation
					?? throw new InvalidOperationException(
						"The recovery test payload has no active Actor activation.");
				active.ActivationId = activationId;
				break;
			}
			case ActorBorrowedPrivateCommitMutation.JudgeObservationSignalAndResource:
			{
				var commit = _payload
					.ActorBorrowedStutteringJudgeSignalObservationCommits.Single();
				commit.SignalOccurred = !commit.SignalOccurred;
				commit.SpentResourceIdentity = commit.SignalOccurred
					? CreateResourceIdentity(
						commit.PowerIdentity,
						ActorBorrowedStutteringJudgeSignalObservationCommit
							.ExpectedOneUseResourceId)
					: null;
				break;
			}
			case ActorBorrowedPrivateCommitMutation.HunterFinalShotTarget:
			{
				var commit = _payload
					.ActorBorrowedHunterFinalShotCommits.Single();
				commit.TargetPlayerId = RequireAlternatePlayerId(
					commit.TargetPlayerId,
					[
						commit.PowerIdentity.ActingPlayerId,
						.. commit.TriggeringPlayerIds
					]);
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ElderResistanceNightActionIndex:
			{
				var commit = _payload
					.ActorBorrowedElderResistanceCommits.Single();
				commit.TriggeringNightActionLogIndex++;
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ElderSuppressionAnnouncement:
			{
				var commit = _payload
					.ActorBorrowedElderSuppressionCommits.Single();
				commit.AnnouncementInstructionId = Guid.NewGuid();
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ScapegoatTiePowerLineage:
			{
				var commit = _payload
					.ActorBorrowedScapegoatTieReplacementCommits.Single();
				var previousActivationId = commit.PowerIdentity.PowerInstanceId;
				var replacementActivationId = Guid.NewGuid();
				commit.PowerIdentity = commit.PowerIdentity with
				{
					PowerInstanceId = replacementActivationId
				};
				_payload.ActorSetupCardSpends!.Single(spend =>
					spend.CardId == commit.ActorSetupCardId).ActivationId =
					replacementActivationId;
				if (_payload.ActiveActorBorrowedRolePowerActivation is { } active &&
					active.ActivationId == previousActivationId)
				{
					active.ActivationId = replacementActivationId;
				}
				if (_payload.DomainRecoveryCursor is { } cursor)
				{
					if (cursor.PowerInstanceId == previousActivationId)
					{
						cursor.PowerInstanceId = replacementActivationId;
					}
					if (cursor.ActorBorrowedActivationId == previousActivationId)
					{
						cursor.ActorBorrowedActivationId = replacementActivationId;
					}
				}
				break;
			}
			case ActorBorrowedPrivateCommitMutation.ScapegoatRestrictionAnnouncement:
			{
				var commit = _payload
					.ActorBorrowedScapegoatVoterRestrictionCommits.Single();
				commit.AnnouncementInstructionId = Guid.NewGuid();
				break;
			}
			case ActorBorrowedPrivateCommitMutation.VillageIdiotPardonActingPlayerLineage:
			{
				var commit = _payload
					.ActorBorrowedVillageIdiotPardonCommits.Single();
				var actingPlayerId = RequireAlternatePlayerId(
					commit.PowerIdentity.ActingPlayerId);
				commit.PowerIdentity = commit.PowerIdentity with
				{
					ActingPlayerId = actingPlayerId
				};
				commit.SpentResourceIdentity = commit.SpentResourceIdentity with
				{
					ActingPlayerId = actingPlayerId
				};
				break;
			}
			case ActorBorrowedPrivateCommitMutation.BearTamerGrowlActingPlayerLineage:
			{
				var commit = _payload
					.ActorBorrowedBearTamerGrowlCommits.Single();
				commit.PowerIdentity = commit.PowerIdentity with
				{
					ActingPlayerId = RequireAlternatePlayerId(
						commit.PowerIdentity.ActingPlayerId)
				};
				break;
			}
			case ActorBorrowedPrivateCommitMutation.KnightRustySwordTarget:
			{
				var commit = _payload
					.ActorBorrowedKnightRustySwordScheduleCommits.Single();
				commit.TargetPlayerId = RequireAlternatePlayerId(
					commit.TargetPlayerId,
					commit.PowerIdentity.ActingPlayerId);
				break;
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}

		return this;
	}

	internal RecoveryPayloadTestDriver InjectSecondActorSpendAsActiveActivation(
		Guid selectedCardId,
		Guid activationId)
	{
		if (selectedCardId == Guid.Empty || activationId == Guid.Empty)
		{
			throw new ArgumentException(
				"The recovery test Actor spend requires stable card and activation identities.");
		}

		var cursor = RequireDomainCursor();
		if (cursor.Kind != DomainRecoveryCursorKind
				.ActorBorrowedStutteringJudgeSignalObservationCommit)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Actor borrowed Judge observation cursor.");
		}

		var selectedCard = _payload.ActorSetupCards?.Cards?
			.SingleOrDefault(card => card.Id == selectedCardId)
			?? throw new InvalidOperationException(
				"The recovery test payload has no selected Actor Setup Card.");
		var spends = _payload.ActorSetupCardSpends
			?? throw new InvalidOperationException(
				"The recovery test payload has no Actor Setup Card spends.");
		if (spends.Any(spend =>
				spend.CardId == selectedCardId ||
				spend.ActivationId == activationId))
		{
			throw new InvalidOperationException(
				"The recovery test Actor spend is not fresh.");
		}

		var currentActive = _payload.ActiveActorBorrowedRolePowerActivation
			?? throw new InvalidOperationException(
				"The recovery test payload has no active Actor activation.");
		spends.Add(new ActorSetupCardSpendDto
		{
			CardId = selectedCardId,
			ActivationId = activationId
		});
		_payload.ActiveActorBorrowedRolePowerActivation =
			new ActorBorrowedRolePowerActivationDto
			{
				ActivationId = activationId,
				ActingPlayerId = currentActive.ActingPlayerId,
				ActingRole = MainRoleType.Actor,
				SelectedCardId = selectedCardId,
				SourceRole = selectedCard.PrintedRole
			};
		var timestamp = _payload.GameHistoryLog.Last().Timestamp;
		_payload.GameHistoryLog.Add(
			new ActorBorrowedRolePowerActivationExpiredLogEntry
			{
				Timestamp = timestamp.AddTicks(1),
				TurnNumber = _payload.TurnNumber,
				CurrentPhase = GamePhase.Night
			});
		_payload.GameHistoryLog.Add(new ActorSetupCardSpendCommittedLogEntry
		{
			Timestamp = timestamp.AddTicks(2),
			TurnNumber = _payload.TurnNumber,
			CurrentPhase = GamePhase.Night
		});
		return this;
	}

	internal string Serialize() =>
		JsonSerializer.Serialize(_payload, SerializationOptions);

	private void RewriteLatestRecurringEntry(
		Func<
			RecurringRolePowerCommittedLogEntry,
			RecurringRolePowerCommittedLogEntry> rewrite)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is RecurringRolePowerCommittedLogEntry);
		if (entryIndex < 0 ||
		    _payload.GameHistoryLog[entryIndex] is not
			    RecurringRolePowerCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no committed recurring Night action.");
		}

		_payload.GameHistoryLog[entryIndex] = rewrite(entry);
	}

	private void RewriteLatestPermanentRoleSwap(
		Func<
			PermanentRoleSwapCommittedLogEntry,
			PermanentRoleSwapCommittedLogEntry> rewrite)
	{
		var entryIndex = _payload.GameHistoryLog.FindLastIndex(
			entry => entry is PermanentRoleSwapCommittedLogEntry);
		if (entryIndex < 0 ||
			_payload.GameHistoryLog[entryIndex] is not
				PermanentRoleSwapCommittedLogEntry entry)
		{
			throw new InvalidOperationException(
				"The recovery test payload has no Permanent Role Swap.");
		}

		_payload.GameHistoryLog[entryIndex] = rewrite(entry);
	}

	private PermanentRoleSwapCommittedLogEntry RequireLatestPermanentRoleSwap() =>
		_payload.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.LastOrDefault()
		?? throw new InvalidOperationException(
			"The recovery test payload has no Permanent Role Swap.");

	private PhysicalCharacterCard RequireRoleLockInCard(Guid cardId) =>
		_payload.RoleLockIn?.RoleComposition
			.SingleOrDefault(card => card.Id == cardId)
		?? throw new InvalidOperationException(
			"The recovery test payload is missing a Physical Character Card.");

	private PhysicalCharacterCardStateDto RequirePhysicalCardState(Guid cardId) =>
		_payload.PhysicalCharacterCards
			.SingleOrDefault(state => state.CardId == cardId)
		?? throw new InvalidOperationException(
			"The recovery test payload is missing Physical Character Card state.");

	private PhysicalCharacterCardZone RequireOfferZone(Guid cardId)
	{
		var lockIn = _payload.RoleLockIn
			?? throw new InvalidOperationException(
				"The recovery test payload has no Role Lock-In.");
		return cardId == lockIn.Offer1CardId
			? PhysicalCharacterCardZone.Offer1
			: cardId == lockIn.Offer2CardId
				? PhysicalCharacterCardZone.Offer2
				: throw new InvalidOperationException(
					"The recovery test card is not a locked Thief offer.");
	}

	private static FactionFact RequireSingleBeneficiaryFact(
		FactionFactsCommittedLogEntry entry,
		Guid playerId)
	{
		var facts = entry.Facts
			.Where(fact =>
				fact.Type == FactionFactType.Beneficiary &&
				fact.PlayerId == playerId)
			.ToArray();
		return facts.Length == 1
			? facts[0]
			: throw new InvalidOperationException(
				"The recovery test payload must have exactly one matching Beneficiary fact.");
	}

	private DomainRecoveryCursor RequireDomainCursor() =>
		_payload.DomainRecoveryCursor
		?? throw new InvalidOperationException(
			"The recovery test payload has no committed domain continuation.");

	private AcceptedObservationRecoveryCursor
		RequireAcceptedObservationCursor() =>
		_payload.AcceptedObservationRecoveryCursor
		?? throw new InvalidOperationException(
			"The recovery test payload has no accepted observation continuation.");

	private void RequireCursorlessStableBoundary()
	{
		if (!_payload.IsStableRecoveryBoundary ||
			_payload.AcceptedObservationRecoveryCursor is not null ||
			_payload.DomainRecoveryCursor is not null)
		{
			throw new InvalidOperationException(
				"The recovery test payload is not a cursorless stable boundary.");
		}
	}

	private void RequireStableRecoveryBoundary()
	{
		if (!_payload.IsStableRecoveryBoundary)
		{
			throw new InvalidOperationException(
				"The recovery test payload is not a stable recovery boundary.");
		}
	}

	private Guid RequireAlternatePlayerId(
		Guid currentPlayerId,
		params Guid[] excludedPlayerIds)
	{
		var alternate = _payload.Players
			.Select(player => player.Id)
			.FirstOrDefault(playerId =>
				playerId != currentPlayerId &&
				!excludedPlayerIds.Contains(playerId));
		return alternate != Guid.Empty
			? alternate
			: throw new InvalidOperationException(
				"The recovery test payload has no alternate Player.");
	}

	private static Guid AlternateWitchResourceId(Guid resourceId) =>
		resourceId == ActorBorrowedWitchPotionUseCommit.HealingResourceId
			? ActorBorrowedWitchPotionUseCommit.PoisonResourceId
			: ActorBorrowedWitchPotionUseCommit.HealingResourceId;

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		RolePowerInstanceIdentity powerIdentity,
		Guid resourceId) => new(
			powerIdentity.ActingPlayerId,
			powerIdentity.SourceRole,
			powerIdentity.SourcePowerIdentifier,
			powerIdentity.PowerInstanceId,
			powerIdentity.PowerInstanceOrigin,
			resourceId);

	private static void SeedActorBorrowedHunterRecoveryFacts(
		GameSession session,
		Guid werewolfId)
	{
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
				Facts = session.GetPlayers()
					.Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						player.Id == werewolfId
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
					.ToImmutableArray()
			};
		});

		if (InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				agentGroupBoundary) != InitialBeneficiaryClosureResult.Committed)
		{
			throw new InvalidOperationException(
				"The Hunter recovery fixture could not close Faction Beneficiary facts.");
		}
	}

	private static void SeedActorBorrowedKnightRecoveryFacts(
		GameSession session,
		IReadOnlySet<Guid> werewolfIds)
	{
		FactionFactEffectiveBoundary? closureBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			closureBoundary = boundary;
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-actor-borrowed-knight-recovery-faction-state"),
				Facts = session.GetPlayers()
					.Select(player => FactionFact.Beneficiary(
						player.Id,
						werewolfIds.Contains(player.Id)
							? Faction.Werewolf
							: Faction.Villager,
						boundary))
					.Concat(session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						werewolfIds.Contains(player.Id)
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary)))
					.ToImmutableArray()
			};
		});

		if (InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				closureBoundary) != InitialBeneficiaryClosureResult.Committed)
		{
			throw new InvalidOperationException(
				"The Knight recovery fixture could not close Faction Beneficiary facts.");
		}
	}

	private static void CommitActorBorrowedKnightCurrentWerewolfAgentFacts(
		GameSession session,
		IReadOnlySet<Guid> currentAgentIds)
	{
		var players = session.GetPlayers().ToArray();
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-actor-borrowed-knight-current-agent-transition"),
				Facts = players.Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						currentAgentIds.Contains(player.Id)
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
					.ToImmutableArray()
			};
		});
	}

	private static TInstruction RequireActorBorrowedHunterInstruction<TInstruction>(
		ModeratorInstruction? instruction)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction
		?? throw new InvalidOperationException(
			$"The Hunter recovery fixture expected {typeof(TInstruction).Name}.");

	private static TInstruction RequireActorBorrowedElderInstruction<TInstruction>(
		ModeratorInstruction? instruction)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction
		?? throw new InvalidOperationException(
			$"The Elder recovery fixture expected {typeof(TInstruction).Name}.");

	private static TInstruction
		RequireActorBorrowedScapegoatInstruction<TInstruction>(
			ModeratorInstruction? instruction)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction
		?? throw new InvalidOperationException(
			$"The Scapegoat recovery fixture expected {typeof(TInstruction).Name}.");

	private static TInstruction
		RequireActorBorrowedVillageIdiotInstruction<TInstruction>(
			ModeratorInstruction? instruction)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction
		?? throw new InvalidOperationException(
			$"The Village Idiot recovery fixture expected {typeof(TInstruction).Name}.");

	private static TInstruction
		RequireActorBorrowedBearTamerInstruction<TInstruction>(
			ModeratorInstruction? instruction)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction
		?? throw new InvalidOperationException(
			$"The Bear Tamer recovery fixture expected {typeof(TInstruction).Name}.");

	private static TInstruction
		RequireActorBorrowedKnightInstruction<TInstruction>(
			ModeratorInstruction? instruction)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction
		?? throw new InvalidOperationException(
			$"The Knight recovery fixture expected {typeof(TInstruction).Name}.");

	private enum RecoveryHookDriverSubPhase
	{
		Active,
		Complete
	}
}

internal sealed record ActorBorrowedHunterPendingRecoverySnapshot(
	string SerializedSession,
	Guid SessionId,
	SelectPlayersInstruction Selector,
	Guid ActorSetupCardId,
	Guid ActivationId);

internal sealed record ActorBorrowedElderPendingRecoverySnapshot(
	string SerializedSession,
	Guid SessionId,
	ConfirmationInstruction Announcement,
	Guid ActorSetupCardId,
	Guid ActivationId);

internal sealed record ActorBorrowedScapegoatPendingRecoverySnapshot(
	ActorBorrowedScapegoatRecoveryStep Step,
	string SerializedSession,
	Guid SessionId,
	ModeratorInstruction PendingInstruction,
	Guid ActorSetupCardId,
	Guid ActivationId);

internal sealed record ActorBorrowedVillageIdiotPendingRecoverySnapshot(
	string SerializedSession,
	Guid SessionId,
	ConfirmationInstruction Pardon,
	Guid ActorSetupCardId,
	Guid ActivationId,
	Guid PardonResourceId);

internal sealed record ActorBorrowedBearTamerPendingRecoverySnapshot(
	string SerializedSession,
	Guid SessionId,
	ConfirmationInstruction Growl,
	Guid ActorId,
	Guid ActorSetupCardId,
	Guid ActivationId);

internal sealed record ActorBorrowedKnightPendingRecoverySnapshot(
	string SerializedSession,
	Guid SessionId,
	ConfirmationInstruction Announcement,
	Guid ActorId,
	Guid ActorSetupCardId,
	Guid ActivationId);

public enum ActorBorrowedScapegoatRecoveryStep
{
	Reveal,
	PermittedVoterSelection,
	PermittedVoterAnnouncement
}

public enum ActorBorrowedBearTamerRecoveryTamper
{
	PublicAnnouncement,
	PrivateGuidance,
	SoundEffect
}

public enum ActorBorrowedKnightRecoveryTamper
{
	PublicAnnouncement,
	PrivateGuidance,
	AffectedPlayer,
	SoundEffect
}

public enum ActorBorrowedPrivateCommitMutation
{
	SeerTarget,
	SeerResult,
	DefenderTarget,
	FoxCenter,
	FoxResultAndResource,
	WitchUseTarget,
	WitchUseResource,
	WitchDeclineResource,
	CupidPair,
	CupidDisposition,
	JudgeSetupPowerLineage,
	JudgeObservationSignalAndResource,
	HunterFinalShotTarget,
	ElderResistanceNightActionIndex,
	ElderSuppressionAnnouncement,
	ScapegoatTiePowerLineage,
	ScapegoatRestrictionAnnouncement,
	VillageIdiotPardonActingPlayerLineage,
	BearTamerGrowlActingPlayerLineage,
	KnightRustySwordTarget
}
