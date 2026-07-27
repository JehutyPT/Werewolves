using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
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
		var attempt = new RolePowerAttempt(
			actingPlayer,
			MainRoleType.Seer,
			sourcePower,
			powerInstance,
			resource);

		var context = gateway.Evaluate(attempt);

		context.ActingPlayer.Should().BeSameAs(actingPlayer);
		context.SourceRole.Should().Be(MainRoleType.Seer);
		context.SourcePower.Should().BeSameAs(sourcePower);
		context.PowerInstance.Should().BeSameAs(powerInstance);
		context.PowerInstance.Origin.Should().Be(instanceOrigin);
		context.OneUseResource.Should().BeSameAs(resource);
		context.AvailabilityResult.Should().BeSameAs(expectedResult);
		policy.ObservedAttempts.Should().ContainSingle().Which.Should().BeSameAs(attempt);
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

	private static RolePowerAttempt CreateValidAttempt()
	{
		var actingPlayer = new TestPlayer(
			Guid.Parse("10000000-0000-0000-0000-000000000001"),
			"Seer",
			MainRoleType.Seer);
		var sourcePower = new RolePowerDefinition(
			new RolePowerIdentifier("seer-werewolf-detection"),
			RolePowerCategory.Chosen);
		var powerInstance = new RolePowerInstance(
			Guid.Parse("20000000-0000-0000-0000-000000000001"),
			MainRoleType.Seer,
			sourcePower,
			RolePowerInstanceOrigin.Native);

		return new RolePowerAttempt(
			actingPlayer,
			MainRoleType.Seer,
			sourcePower,
			powerInstance);
	}

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

	private sealed record TestPlayer(Guid Id, string Name, MainRoleType CurrentRole) : IPlayer
	{
		public IPlayerState State { get; } = new TestPlayerState(CurrentRole);
	}

	private sealed class TestPlayerState(MainRoleType currentRole) : IPlayerState
	{
		public MainRoleType? CurrentRole => currentRole;
		public MainRoleType? MainRole => currentRole;
		public MainRoleType? PhysicalCharacterCardRole => currentRole;
		public MainRoleType? ModeratorKnownRole => currentRole;
		public MainRoleType? PubliclyRevealedRole => null;
		public PlayerHealth Health => PlayerHealth.Alive;
		public bool HasVotingRight => true;
		public bool IsImmuneToLynching => false;
		public string? LynchingImmunityAnnouncement => null;
		public List<StatusEffectTypes> GetActiveStatusEffects() => [];
		public bool HasStatusEffect(StatusEffectTypes effect) => false;
	}
}
