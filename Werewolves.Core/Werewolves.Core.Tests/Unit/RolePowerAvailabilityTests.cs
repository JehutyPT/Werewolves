using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class RolePowerAvailabilityTests
{
	[Theory]
	[InlineData((int)RolePowerCategory.Chosen, (int)RolePowerInstanceOrigin.Native)]
	[InlineData((int)RolePowerCategory.Automatic, (int)RolePowerInstanceOrigin.Swapped)]
	[InlineData((int)RolePowerCategory.Reactive, (int)RolePowerInstanceOrigin.Borrowed)]
	[InlineData((int)RolePowerCategory.Passive, (int)RolePowerInstanceOrigin.Native)]
	[InlineData((int)RolePowerCategory.Recognition, (int)RolePowerInstanceOrigin.Swapped)]
	[InlineData((int)RolePowerCategory.Communication, (int)RolePowerInstanceOrigin.Borrowed)]
	public void Evaluate_AnyRolePowerCategory_ReturnsCompleteExecutionContext(
		int categoryValue,
		int instanceOriginValue)
	{
		var category = (RolePowerCategory)categoryValue;
		var instanceOrigin = (RolePowerInstanceOrigin)instanceOriginValue;
		var actingPlayer = new TestPlayer(
			Guid.Parse("10000000-0000-0000-0000-000000000001"),
			"Borrower",
			instanceOrigin == RolePowerInstanceOrigin.Borrowed
				? MainRoleType.SimpleVillager
				: MainRoleType.Seer);
		var sourcePower = new RolePowerDefinition(
			new RolePowerIdentifier("test-power"),
			category);
		var powerInstance = new RolePowerInstance(
			Guid.Parse("20000000-0000-0000-0000-000000000001"),
			MainRoleType.Seer,
			sourcePower,
			instanceOrigin);
		var resource = new OneUseRolePowerResource(
			Guid.Parse("30000000-0000-0000-0000-000000000001"),
			powerInstance);
		var expectedResult = RolePowerAvailabilityResult.Denied;
		var policy = new RecordingPolicy(expectedResult);
		var gateway = new RolePowerAvailabilityGateway(policy);
		var session = new TestGameSession();
		var attempt = new RolePowerAttempt(
			session,
			actingPlayer,
			MainRoleType.Seer,
			sourcePower,
			powerInstance,
			resource);

		var context = gateway.Evaluate(attempt);

		context.Session.Should().BeSameAs(session);
		context.ActingPlayer.Should().BeSameAs(actingPlayer);
		context.SourceRole.Should().Be(MainRoleType.Seer);
		context.SourcePower.Should().BeSameAs(sourcePower);
		context.PowerInstance.Should().BeSameAs(powerInstance);
		context.PowerInstance.Origin.Should().Be(instanceOrigin);
		context.OneUseResource.Should().BeSameAs(resource);
		context.AvailabilityResult.Should().BeSameAs(expectedResult);
		policy.ObservedAttempts.Should().ContainSingle().Which.Should().BeSameAs(attempt);
	}

	[Theory]
	[InlineData((int)RolePowerCategory.Chosen, (int)RolePowerInstanceOrigin.Native)]
	[InlineData((int)RolePowerCategory.Automatic, (int)RolePowerInstanceOrigin.Swapped)]
	[InlineData((int)RolePowerCategory.Reactive, (int)RolePowerInstanceOrigin.Borrowed)]
	[InlineData((int)RolePowerCategory.Passive, (int)RolePowerInstanceOrigin.Native)]
	[InlineData((int)RolePowerCategory.Recognition, (int)RolePowerInstanceOrigin.Swapped)]
	[InlineData((int)RolePowerCategory.Communication, (int)RolePowerInstanceOrigin.Borrowed)]
	public void Evaluate_ActiveVillagerSuppression_DeniesEveryVillagerPowerCategory(
		int categoryValue,
		int instanceOriginValue)
	{
		var suppression = CreateSuppressionFact();
		var session = new TestGameSession([suppression]);
		var attempt = CreateValidAttempt(
			session,
			(RolePowerCategory)categoryValue,
			(RolePowerInstanceOrigin)instanceOriginValue,
			MainRoleType.Seer,
			Faction.Werewolf,
			includeResource: true);
		var next = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var gateway = new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(next));

		GameSessionQueries.GetVillagerRolePowerSuppression(session)
			.Should().BeSameAs(suppression);
		GameSessionQueries.IsVillagerRolePowerSuppressionActive(session)
			.Should().BeTrue();
		var context = gateway.Evaluate(attempt);

		context.AvailabilityResult
			.Should().BeSameAs(RolePowerAvailabilityResult.Denied);
		context.ActingPlayer.State.FactionBeneficiary.Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		context.PowerInstance.Origin.Should().Be(
			(RolePowerInstanceOrigin)instanceOriginValue);
		context.OneUseResource.Should().BeSameAs(attempt.OneUseResource);
		next.ObservedAttempts.Should().BeEmpty();
	}

	[Fact]
	public void Evaluate_ActiveVillagerSuppression_DeniesFreshSwappedElderWithoutConsumingResource()
	{
		var suppression = CreateSuppressionFact();
		var session = new TestGameSession([suppression]);
		var historyBeforeAttempt = session.GameHistoryLog.ToArray();
		var attempt = CreateValidAttempt(
			session,
			RolePowerCategory.Reactive,
			RolePowerInstanceOrigin.Swapped,
			MainRoleType.Elder,
			Faction.Villager,
			includeResource: false);
		var next = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var gateway = new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(next));

		var context = gateway.Evaluate(attempt);

		context.SourceRole.Should().Be(MainRoleType.Elder);
		context.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Swapped);
		context.AvailabilityResult.Should()
			.BeSameAs(RolePowerAvailabilityResult.Denied);
		context.OneUseResource.Should().BeNull();
		session.GameHistoryLog.Should().Equal(historyBeforeAttempt);
		next.ObservedAttempts.Should().BeEmpty();
	}

	[Fact]
	public void Evaluate_ActiveVillagerSuppression_NonVillagerSourceDelegatesRegardlessOfBeneficiary()
	{
		var session = new TestGameSession([CreateSuppressionFact()]);
		var attempt = CreateValidAttempt(
			session,
			RolePowerCategory.Chosen,
			RolePowerInstanceOrigin.Swapped,
			MainRoleType.SimpleWerewolf,
			Faction.Villager,
			includeResource: true);
		var delegatedResult = RolePowerAvailabilityResult.Denied;
		var next = new RecordingPolicy(delegatedResult);
		var gateway = new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(next));

		var context = gateway.Evaluate(attempt);

		context.AvailabilityResult.Should().BeSameAs(delegatedResult);
		context.SourceRole.Should().Be(MainRoleType.SimpleWerewolf);
		context.ActingPlayer.State.FactionBeneficiary.Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
		context.OneUseResource.Should().BeSameAs(attempt.OneUseResource);
		next.ObservedAttempts.Should().ContainSingle().Which.Should().BeSameAs(attempt);
	}

	[Fact]
	public void Evaluate_WithoutSuppression_VillagerSourceDelegates()
	{
		var attempt = CreateValidAttempt();
		var delegatedResult = RolePowerAvailabilityResult.Denied;
		var next = new RecordingPolicy(delegatedResult);
		var gateway = new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(next));

		var context = gateway.Evaluate(attempt);

		context.AvailabilityResult.Should().BeSameAs(delegatedResult);
		next.ObservedAttempts.Should().ContainSingle().Which.Should().BeSameAs(attempt);
	}

	[Fact]
	public void GameLogEntryConverter_VillagerSuppressionFact_PreservesTypedSessionFact()
	{
		GameLogEntryBase suppression = CreateSuppressionFact();
		var options = new JsonSerializerOptions
		{
			Converters =
			{
				new GameLogEntryConverter(),
				new JsonStringEnumConverter()
			}
		};

		var json = JsonSerializer.Serialize(suppression, options);
		var restored = JsonSerializer.Deserialize<GameLogEntryBase>(json, options);

		restored.Should().Be(suppression);
	}

	[Fact]
	public void GameLogEntryConverter_VillagerSuppressionAcknowledgment_PreservesCorrelationOnly()
	{
		var suppression = CreateSuppressionFact();
		GameLogEntryBase acknowledgment = CreateSuppressionAcknowledgment(
			suppression.AnnouncementInstructionId);
		var options = new JsonSerializerOptions
		{
			Converters =
			{
				new GameLogEntryConverter(),
				new JsonStringEnumConverter()
			}
		};

		var json = JsonSerializer.Serialize(acknowledgment, options);
		var restored = JsonSerializer.Deserialize<GameLogEntryBase>(json, options);
		var session = new TestGameSession([suppression, acknowledgment]);

		restored.Should().Be(acknowledgment);
		GameSessionQueries
			.IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
				session,
				suppression.AnnouncementInstructionId)
			.Should().BeTrue();
		GameSessionQueries
			.IsVillagerRolePowerSuppressionAnnouncementAcknowledged(
				session,
				Guid.Parse("40000000-0000-0000-0000-000000000002"))
			.Should().BeFalse();
	}

	[Fact]
	public void Evaluate_InvalidInstanceOrResourceRelationship_RejectsBeforePolicyEvaluation()
	{
		var validAttempt = CreateValidAttempt();
		var unrelatedPower = new RolePowerDefinition(
			new RolePowerIdentifier("unrelated-power"),
			RolePowerCategory.Chosen);
		var differentIdentityInstance = validAttempt.PowerInstance with
		{
			Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
		};
		var collidingIdentityInstance = validAttempt.PowerInstance with
		{
			Origin = RolePowerInstanceOrigin.Borrowed,
		};
		var attempts = new[]
		{
			validAttempt with { SourceRole = MainRoleType.Witch },
			validAttempt with { SourcePower = unrelatedPower },
			validAttempt with
			{
				OneUseResource = new OneUseRolePowerResource(
					Guid.Parse("30000000-0000-0000-0000-000000000002"),
					differentIdentityInstance),
			},
			validAttempt with
			{
				OneUseResource = new OneUseRolePowerResource(
					Guid.Parse("30000000-0000-0000-0000-000000000003"),
					collidingIdentityInstance),
			},
		};
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var gateway = new RolePowerAvailabilityGateway(policy);

		foreach (var attempt in attempts)
		{
			var act = () => gateway.Evaluate(attempt);

			act.Should().Throw<ArgumentException>();
		}

		policy.ObservedAttempts.Should().BeEmpty();
	}

	[Fact]
	public void CreateNative_SameGrantAfterRuntimeReconstruction_ReturnsStableConcreteIdentity()
	{
		var actorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
		var originalActor = new TestPlayer(actorId, "Seer", MainRoleType.Seer);
		var reconstructedActor = new TestPlayer(actorId, "Seer", MainRoleType.Seer);
		var sourcePower = new RolePowerDefinition(
			new RolePowerIdentifier("seer-werewolf-detection"),
			RolePowerCategory.Chosen);

		var original = RolePowerInstance.CreateNative(
			originalActor,
			MainRoleType.Seer,
			sourcePower);
		var reconstructed = RolePowerInstance.CreateNative(
			reconstructedActor,
			MainRoleType.Seer,
			sourcePower);

		original.Should().Be(reconstructed);
		original.Id.Should().Be(actorId);
	}

	[Fact]
	public void CreateBorrowed_LivingActorAndMatchingSeerActivation_UsesActivationQualifiedIdentity()
	{
		var (session, actor, seerCard) = CreateBorrowedSeerFixture();
		session.TrySpendActorSetupCard(actor.Id, seerCard.Id, out var activation)
			.Should().BeTrue();
		var sourcePower = new RolePowerDefinition(
			new RolePowerIdentifier("seer-werewolf-detection"),
			RolePowerCategory.Chosen);

		var instance = RolePowerInstance.CreateBorrowed(
			session,
			actor,
			MainRoleType.Seer,
			sourcePower);

		instance.Id.Should().Be(activation!.ActivationId);
		instance.SourceRole.Should().Be(MainRoleType.Seer);
		instance.SourcePower.Should().BeSameAs(sourcePower);
		instance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		actor.State.CurrentRole.Should().Be(MainRoleType.Actor);
	}

	[Theory]
	[InlineData(InvalidBorrowedFactoryCase.NoActiveActivation)]
	[InlineData(InvalidBorrowedFactoryCase.ExpiredActivation)]
	[InlineData(InvalidBorrowedFactoryCase.DifferentActingPlayer)]
	[InlineData(InvalidBorrowedFactoryCase.SelectedCardSourceMismatch)]
	[InlineData(InvalidBorrowedFactoryCase.ActorRoleChanged)]
	[InlineData(InvalidBorrowedFactoryCase.ActorDead)]
	public void CreateBorrowed_StaleOrMismatchedActorActivation_Rejects(
		InvalidBorrowedFactoryCase invalidCase)
	{
		var (session, actor, seerCard) = CreateBorrowedSeerFixture();
		IPlayer actingPlayer = actor;
		var sourceRole = MainRoleType.Seer;
		if (invalidCase != InvalidBorrowedFactoryCase.NoActiveActivation)
		{
			session.TrySpendActorSetupCard(actor.Id, seerCard.Id, out _)
				.Should().BeTrue();
		}

		switch (invalidCase)
		{
			case InvalidBorrowedFactoryCase.NoActiveActivation:
				break;
			case InvalidBorrowedFactoryCase.ExpiredActivation:
				session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
				break;
			case InvalidBorrowedFactoryCase.DifferentActingPlayer:
				actingPlayer = session.GetPlayers().Skip(1).First();
				break;
			case InvalidBorrowedFactoryCase.SelectedCardSourceMismatch:
				sourceRole = MainRoleType.Witch;
				break;
			case InvalidBorrowedFactoryCase.ActorRoleChanged:
				session.AssignRole(actor.Id, MainRoleType.SimpleVillager);
				break;
			case InvalidBorrowedFactoryCase.ActorDead:
				session.EliminatePlayer(
					actor.Id,
					EliminationReason.EventElimination);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(invalidCase));
		}

		var sourcePower = new RolePowerDefinition(
			new RolePowerIdentifier("seer-werewolf-detection"),
			RolePowerCategory.Chosen);
		var act = () => RolePowerInstance.CreateBorrowed(
			session,
			actingPlayer,
			sourceRole,
			sourcePower);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*borrowed Role Power activation*");
	}

	[Theory]
	[InlineData(InvalidPostEliminationLineageCase.HunterMissingActiveActivation)]
	[InlineData(InvalidPostEliminationLineageCase.ElderExpiredActivation)]
	[InlineData(InvalidPostEliminationLineageCase.KnightMismatchedSelectedCardSource)]
	public void CreateBorrowedAfterElimination_ValidContextWithMissingStaleOrMismatchedLineage_Rejects(
		InvalidPostEliminationLineageCase invalidCase)
	{
		var fixture = CreateInvalidPostEliminationLineageFixture(invalidCase);
		var historyCountBefore = fixture.Session.GameHistoryLog.Count();
		var act = () => RolePowerInstance.CreateBorrowedAfterElimination(
			fixture.Session,
			fixture.Actor,
			fixture.SourceRole,
			fixture.SourcePower,
			fixture.Context);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*borrowed Role Power activation*");
		fixture.Session.GameHistoryLog.Should().HaveCount(historyCountBefore);
	}

	[Theory]
	[InlineData(InvalidPostEliminationContextCase.HunterOutsideActiveInteractiveReactionBatch)]
	[InlineData(InvalidPostEliminationContextCase.ElderBeforeCascadeCompletion)]
	[InlineData(InvalidPostEliminationContextCase.KnightBeforeCascadeCompletion)]
	public void CreateBorrowedAfterElimination_PrematureCommittedContext_Rejects(
		InvalidPostEliminationContextCase invalidCase)
	{
		var fixture = CreatePrematurePostEliminationContextFixture(invalidCase);
		var historyCountBefore = fixture.Session.GameHistoryLog.Count();
		var act = () => RolePowerInstance.CreateBorrowedAfterElimination(
			fixture.Session,
			fixture.Actor,
			fixture.SourceRole,
			fixture.SourcePower,
			fixture.Context);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*borrowed post-elimination Role Power context*");
		fixture.Session.GameHistoryLog.Should().HaveCount(historyCountBefore);
	}

	[Fact]
	public void Evaluate_PolicyReturnsNull_RejectsIncompleteExecutionContext()
	{
		var gateway = new RolePowerAvailabilityGateway(new NullResultPolicy());

		var act = () => gateway.Evaluate(CreateValidAttempt());

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*availability result*");
	}

	[Fact]
	public void Constructor_NullPolicy_RejectsIncompleteGateway()
	{
		var act = () => new RolePowerAvailabilityGateway(null!);

		act.Should().Throw<ArgumentNullException>()
			.WithParameterName("policy");
	}

	[Fact]
	public void Evaluate_NullAttempt_RejectsBeforePolicyEvaluation()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var gateway = new RolePowerAvailabilityGateway(policy);

		var act = () => gateway.Evaluate(null!);

		act.Should().Throw<ArgumentNullException>()
			.WithParameterName("attempt");
		policy.ObservedAttempts.Should().BeEmpty();
	}

	[Fact]
	public void Evaluate_NullRequiredAttemptValue_RejectsIncompleteExecutionContext()
	{
		var validAttempt = CreateValidAttempt();
		var attempts = new[]
		{
			validAttempt with { Session = null! },
			validAttempt with { ActingPlayer = null! },
			validAttempt with { SourcePower = null! },
			validAttempt with { PowerInstance = null! },
			validAttempt with
			{
				PowerInstance = validAttempt.PowerInstance with { SourcePower = null! },
			},
			validAttempt with
			{
				OneUseResource = new OneUseRolePowerResource(
					Guid.Parse("30000000-0000-0000-0000-000000000004"),
					null!),
			},
		};
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var gateway = new RolePowerAvailabilityGateway(policy);

		foreach (var attempt in attempts)
		{
			var act = () => gateway.Evaluate(attempt);

			act.Should().Throw<ArgumentException>();
		}

		policy.ObservedAttempts.Should().BeEmpty();
	}

	private static RolePowerAttempt CreateValidAttempt(
		IGameSession? session = null,
		RolePowerCategory category = RolePowerCategory.Chosen,
		RolePowerInstanceOrigin instanceOrigin = RolePowerInstanceOrigin.Native,
		MainRoleType sourceRole = MainRoleType.Seer,
		Faction beneficiary = Faction.Villager,
		bool includeResource = false)
	{
		var currentRole = instanceOrigin == RolePowerInstanceOrigin.Borrowed
			? MainRoleType.SimpleVillager
			: sourceRole;
		var actingPlayer = new TestPlayer(
			Guid.Parse("10000000-0000-0000-0000-000000000001"),
			"Player A",
			currentRole,
			FactionBeneficiaryKnowledge.Known(beneficiary));
		var sourcePower = new RolePowerDefinition(
			new RolePowerIdentifier("seer-werewolf-detection"),
			category);
		var powerInstance = new RolePowerInstance(
			Guid.Parse("20000000-0000-0000-0000-000000000001"),
			sourceRole,
			sourcePower,
			instanceOrigin);
		var resource = includeResource
			? new OneUseRolePowerResource(
				Guid.Parse("30000000-0000-0000-0000-000000000001"),
				powerInstance)
			: null;

		return new RolePowerAttempt(
			session ?? new TestGameSession(),
			actingPlayer,
			sourceRole,
			sourcePower,
			powerInstance,
			resource);
	}

	private static VillagerRolePowerSuppressionCommittedLogEntry
		CreateSuppressionFact() => new()
		{
			Timestamp = DateTimeOffset.Parse("2026-08-02T12:00:00+00:00"),
			TurnNumber = 3,
			CurrentPhase = GamePhase.Day,
			AnnouncementInstructionId = Guid.Parse(
				"40000000-0000-0000-0000-000000000001")
		};

	private static VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry
		CreateSuppressionAcknowledgment(Guid announcementInstructionId) => new()
		{
			Timestamp = DateTimeOffset.Parse("2026-08-02T12:01:00+00:00"),
			TurnNumber = 3,
			CurrentPhase = GamePhase.Day,
			AnnouncementInstructionId = announcementInstructionId
		};

	private sealed class RecordingPolicy(RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		public List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return result;
		}
	}

	private sealed class NullResultPolicy : IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) => null!;
	}

	private sealed record TestPlayer(
		Guid Id,
		string Name,
		MainRoleType CurrentRole,
		FactionBeneficiaryKnowledge? Beneficiary = null) : IPlayer
	{
		public IPlayerState State { get; } = new TestPlayerState(
			CurrentRole,
			Beneficiary ?? FactionBeneficiaryKnowledge.Unknown);
	}

	private sealed class TestPlayerState(
		MainRoleType currentRole,
		FactionBeneficiaryKnowledge beneficiary) : IPlayerState
	{
		public MainRoleType? CurrentRole => currentRole;
		public MainRoleType? MainRole => currentRole;
		public MainRoleType? PhysicalCharacterCardRole => currentRole;
		public MainRoleType? ModeratorKnownRole => currentRole;
		public MainRoleType? PubliclyRevealedRole => null;
		public PlayerHealth Health => PlayerHealth.Alive;
		public bool HasVotingRight => true;
		public int DurableVotingPower => 1;
		public FactionBeneficiaryKnowledge FactionBeneficiary => beneficiary;
		public List<StatusEffectTypes> GetActiveStatusEffects() => [];
		public bool HasStatusEffect(StatusEffectTypes effect) => false;
	}

	private sealed class TestGameSession(
		IEnumerable<GameLogEntryBase>? gameHistoryLog = null) : IGameSession
	{
		public IEnumerable<GameLogEntryBase> GameHistoryLog { get; } =
			gameHistoryLog?.ToArray() ?? [];
		public Guid Id { get; } = Guid.NewGuid();
		public int TurnNumber => 3;
		public GamePhase GetCurrentPhase() => GamePhase.Day;
		public IPlayer GetPlayer(Guid playerId) => throw new NotSupportedException();
		public IPlayerState GetPlayerState(Guid playerId) =>
			throw new NotSupportedException();
		public IEnumerable<IPlayer> GetPlayers() => [];
		public FactionBeneficiaryKnowledge GetFactionBeneficiaryKnowledge(
			Guid playerId) => throw new NotSupportedException();
		public FactionAgentKnowledge GetFactionAgentKnowledge(
			Guid playerId,
			Faction faction) => throw new NotSupportedException();
		public bool TryGetKnownFactionAgents(
			Faction faction,
			out IReadOnlyList<IPlayer> agents)
		{
			agents = [];
			return false;
		}
		public Faction RequireKnownFactionBeneficiary(Guid playerId) =>
			throw new NotSupportedException();
		public IReadOnlyList<IPlayer> RequireKnownFactionAgents(Faction faction) =>
			throw new NotSupportedException();
		public int RoleInPlayCount(MainRoleType type) => 0;
		public string Serialize() => throw new NotSupportedException();
	}

	private static BorrowedPostEliminationFactoryFixture
		CreateInvalidPostEliminationLineageFixture(
			InvalidPostEliminationLineageCase invalidCase) => invalidCase switch
		{
			InvalidPostEliminationLineageCase.HunterMissingActiveActivation =>
				CreateHunterPostEliminationFixture(
					spendHunterCard: false,
					createActiveInteractiveReactionBatch: true),
			InvalidPostEliminationLineageCase.ElderExpiredActivation =>
				CreateElderPostEliminationFixture(
					expireActivation: true,
					completeCascade: true),
			InvalidPostEliminationLineageCase.KnightMismatchedSelectedCardSource =>
				CreateKnightPostEliminationFixture(
					MainRoleType.Seer,
					completeCascade: true),
			_ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
		};

	private static BorrowedPostEliminationFactoryFixture
		CreatePrematurePostEliminationContextFixture(
			InvalidPostEliminationContextCase invalidCase) => invalidCase switch
		{
			InvalidPostEliminationContextCase
				.HunterOutsideActiveInteractiveReactionBatch =>
				CreateHunterPostEliminationFixture(
					spendHunterCard: true,
					createActiveInteractiveReactionBatch: false),
			InvalidPostEliminationContextCase.ElderBeforeCascadeCompletion =>
				CreateElderPostEliminationFixture(
					expireActivation: false,
					completeCascade: false),
			InvalidPostEliminationContextCase.KnightBeforeCascadeCompletion =>
				CreateKnightPostEliminationFixture(
					MainRoleType.KnightWithRustySword,
					completeCascade: false),
			_ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
		};

	private static BorrowedPostEliminationFactoryFixture
		CreateHunterPostEliminationFixture(
			bool spendHunterCard,
			bool createActiveInteractiveReactionBatch)
	{
		var (session, actor, hunterCard) = CreateBorrowedActorFixture(
			MainRoleType.Hunter);
		if (spendHunterCard)
		{
			session.TrySpendActorSetupCard(actor.Id, hunterCard.Id, out _)
				.Should().BeTrue();
		}

		session.TransitionMainPhase(GamePhase.Day);
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[actor.Id] = MainRoleType.Actor
		});
		var scopeId = $"RolePowerAvailability:Hunter:{session.TurnNumber}";
		var elimination = new EliminationCascadeElimination(
			actor.Id,
			EliminationReason.EventElimination);
		if (createActiveInteractiveReactionBatch)
		{
			EliminationCascadeRuntimeStore.Configure(
				session,
				[
					new EliminationCascadeReactionBinding(
						new BlockingInteractiveReaction(),
						EliminationCascadeReactionBoundary.Interactive)
				]);
			var cascade = EliminationCascadeStage.CascadeStage(
				PostEliminationFactoryCascadeStage.HunterLineageProbe,
				_ => new EliminationCascadeSeed(
					scopeId,
					session.GameHistoryLog.Count() - 1,
					[
						new EliminationRequest(
							actor.Id,
							EliminationReason.EventElimination)
					]),
				ModeratorInstructionSemantic.AssignDayVoteTargetRole);
			cascade.Execute(
				session,
				new StartGameConfirmationInstruction(session.Id).CreateResponse());
		}
		else
		{
			session.EliminatePlayer(actor.Id, elimination.Reason);
			session.RecordEliminationCascadeBatchResolution(
				scopeId,
				[elimination],
				[elimination]);
		}

		return new BorrowedPostEliminationFactoryFixture(
			session,
			actor,
			MainRoleType.Hunter,
			new RolePowerDefinition(
				new RolePowerIdentifier("hunter-final-shot"),
				RolePowerCategory.Reactive),
			new BorrowedPostEliminationRolePowerContext.HunterFinalShot(
				scopeId,
				[actor.Id]));
	}

	private static BorrowedPostEliminationFactoryFixture
		CreateElderPostEliminationFixture(
			bool expireActivation,
			bool completeCascade)
	{
		var (session, actor, elderCard) = CreateBorrowedActorFixture(
			MainRoleType.Elder);
		session.TrySpendActorSetupCard(actor.Id, elderCard.Id, out _)
			.Should().BeTrue();
		if (expireActivation)
		{
			session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		}

		session.TransitionMainPhase(GamePhase.Day);
		session.PerformDayVote(actor.Id);
		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session)!.Value;
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[actor.Id] = MainRoleType.Actor
		});
		var scopeId = $"Day:{session.TurnNumber}:Vote:{vote.VoteOrdinal}";
		var elimination = new EliminationCascadeElimination(
			actor.Id,
			EliminationReason.DayVote);
		session.EliminatePlayer(actor.Id, elimination.Reason);
		session.RecordEliminationCascadeBatchResolution(
			scopeId,
			[elimination],
			[elimination]);
		if (completeCascade)
		{
			session.RecordEliminationCascadeCompletion(scopeId);
		}

		return new BorrowedPostEliminationFactoryFixture(
			session,
			actor,
			MainRoleType.Elder,
			new RolePowerDefinition(
				new RolePowerIdentifier("elder-village-vote-suppression"),
				RolePowerCategory.Reactive),
			new BorrowedPostEliminationRolePowerContext
				.ElderVillageVoteSuppression(vote.LogIndex, scopeId));
	}

	private static BorrowedPostEliminationFactoryFixture
		CreateKnightPostEliminationFixture(
			MainRoleType selectedCardRole,
			bool completeCascade)
	{
		var (session, actor, selectedCard) = CreateBorrowedActorFixture(
			selectedCardRole);
		session.TrySpendActorSetupCard(actor.Id, selectedCard.Id, out _)
			.Should().BeTrue();
		session.TransitionMainPhase(GamePhase.Dawn);
		session.DetermineDawnVictim(
			actor.Id,
			EliminationReason.WerewolfAttack);
		session.EliminatePlayer(
			actor.Id,
			EliminationReason.WerewolfAttack);
		var eliminationLogIndex = session.GameHistoryLog.Count() - 1;
		var scopeId = $"Dawn:{session.TurnNumber}";
		var elimination = new EliminationCascadeElimination(
			actor.Id,
			EliminationReason.WerewolfAttack);
		session.RecordEliminationCascadeBatchResolution(
			scopeId,
			[elimination],
			[elimination]);
		if (completeCascade)
		{
			session.RecordEliminationCascadeCompletion(scopeId);
		}

		return new BorrowedPostEliminationFactoryFixture(
			session,
			actor,
			MainRoleType.KnightWithRustySword,
			new RolePowerDefinition(
				new RolePowerIdentifier("knight-rusty-sword-disease"),
				RolePowerCategory.Automatic),
			new BorrowedPostEliminationRolePowerContext.KnightRustySwordSchedule(
				eliminationLogIndex,
				scopeId));
	}

	private static (GameSession Session, IPlayer Actor,
		PhysicalCharacterCard SeerCard) CreateBorrowedSeerFixture()
	{
		var (session, actor, card) = CreateBorrowedActorFixture(
			MainRoleType.Seer);
		return (session, actor, card);
	}

	private static (GameSession Session, IPlayer Actor,
		PhysicalCharacterCard SetupCard) CreateBorrowedActorFixture(
			MainRoleType setupCardRole)
	{
		var setupCard = new PhysicalCharacterCard(
			Guid.Parse("30000000-0000-0000-0000-000000000001"),
			setupCardRole);
		var sessionId = Guid.NewGuid();
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			new GameSessionConfig(
				[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
				[
					MainRoleType.Actor,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				],
				new ActorSetupCards(
					version: 1,
					[
						setupCard,
						new PhysicalCharacterCard(
							Guid.Parse("30000000-0000-0000-0000-000000000002"),
							MainRoleType.Cupid),
						new PhysicalCharacterCard(
							Guid.Parse("30000000-0000-0000-0000-000000000003"),
							MainRoleType.Witch)
					])));
		var actor = session.GetPlayers().First();
		session.AssignRole(actor.Id, MainRoleType.Actor);
		return (session, actor, setupCard);
	}

	private sealed class BlockingInteractiveReaction : IEliminationCascadeReaction
	{
		public string ReactionId => "role-power-availability-blocking-probe";

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			EliminationCascadeReactionResult.NeedInput(
				new StartGameConfirmationInstruction(session.Id));
	}

	private sealed record BorrowedPostEliminationFactoryFixture(
		GameSession Session,
		IPlayer Actor,
		MainRoleType SourceRole,
		RolePowerDefinition SourcePower,
		BorrowedPostEliminationRolePowerContext Context);

	private enum PostEliminationFactoryCascadeStage
	{
		HunterLineageProbe
	}

	public enum InvalidBorrowedFactoryCase
	{
		NoActiveActivation,
		ExpiredActivation,
		DifferentActingPlayer,
		SelectedCardSourceMismatch,
		ActorRoleChanged,
		ActorDead
	}

	public enum InvalidPostEliminationLineageCase
	{
		HunterMissingActiveActivation,
		ElderExpiredActivation,
		KnightMismatchedSelectedCardSource
	}

	public enum InvalidPostEliminationContextCase
	{
		HunterOutsideActiveInteractiveReactionBatch,
		ElderBeforeCascadeCompletion,
		KnightBeforeCascadeCompletion
	}
}
