using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
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
}
