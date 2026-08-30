using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class ActorBorrowedRolePowersTests
{
	private static readonly RolePowerDefinition SeerPower = new(
		new RolePowerIdentifier("seer-werewolf-detection"),
		RolePowerCategory.Chosen);
	private static readonly RolePowerDefinition DefenderPower = new(
		new RolePowerIdentifier("defender-protection"),
		RolePowerCategory.Chosen);

	[Fact]
	public void Constructor_NullRolePowerDefinition_Rejects()
	{
		var act = () => new ActorBorrowedRolePowerSpec(
			MainRoleType.Seer,
			null!);

		act.Should().Throw<ArgumentNullException>()
			.WithParameterName("sourcePower");
	}

	[Fact]
	public void Constructor_IneligibleActorSetupCardSourceRole_Rejects()
	{
		var act = () => new ActorBorrowedRolePowerSpec(
			MainRoleType.SimpleWerewolf,
			SeerPower);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("sourceRole");
	}

	[Fact]
	public void ResolveActive_MatchingSpentActorSetupCard_DerivesImmutableUse()
	{
		var (session, actor, setupCard, activation) = CreateActiveActorSession(
			MainRoleType.Seer);
		var spec = new ActorBorrowedRolePowerSpec(
			MainRoleType.Seer,
			SeerPower);

		var use = ActorBorrowedRolePowers.ResolveActive(session, spec);

		use.Should().NotBeNull();
		use!.Actor.Should().BeSameAs(actor);
		use.PowerInstance.Should().Be(new RolePowerInstance(
			activation.ActivationId,
			MainRoleType.Seer,
			SeerPower,
			RolePowerInstanceOrigin.Borrowed));
		use.PowerIdentity.Should().Be(new RolePowerInstanceIdentity(
			actor.Id,
			MainRoleType.Seer,
			SeerPower.Identifier.Value,
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed));
		use.ActorSetupCardId.Should().Be(setupCard.Id);
	}

	[Fact]
	public void ResolveActive_NoActiveBorrowedPower_ReturnsNull()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();

		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower));

		use.Should().BeNull();
	}

	[Fact]
	public void ResolveActive_DifferentActiveSourceRole_ReturnsNull()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);

		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Defender,
				DefenderPower));

		use.Should().BeNull();
	}

	[Fact]
	public void ResolveActive_ActorNoLongerHasActorRole_FailsExplicitly()
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.Seer);
		session.AssignRole(actor.Id, MainRoleType.SimpleVillager);

		var act = () => ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower));

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void ResolveActive_ActorNoLongerAlive_FailsExplicitly()
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.Seer);
		session.EliminatePlayer(actor.Id, EliminationReason.EventElimination);

		var act = () => ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower));

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void CreateAttempt_ResolvedUse_CreatesOrdinaryAttemptForSamePowerInstance()
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower));

		var attempt = use!.CreateAttempt();

		attempt.Session.Should().BeSameAs(session);
		attempt.ActingPlayer.Should().BeSameAs(actor);
		attempt.SourceRole.Should().Be(MainRoleType.Seer);
		attempt.SourcePower.Should().BeSameAs(SeerPower);
		attempt.PowerInstance.Should().BeSameAs(use.PowerInstance);
		attempt.OneUseResource.Should().BeNull();
	}

	[Fact]
	public void CreateAttempt_OneUseResourceId_CreatesResourceForSamePowerInstance()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower));
		var resourceId = Guid.Parse("40000000-0000-0000-0000-000000000001");

		var attempt = use!.CreateAttempt(resourceId);

		attempt.OneUseResource.Should().Be(new OneUseRolePowerResource(
			resourceId,
			use.PowerInstance));
		attempt.PowerInstance.Should().BeSameAs(use.PowerInstance);
	}

	[Fact]
	public void Correlates_RealTypedCommitForActiveCapturedUse_ReturnsTrue()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower))!;
		var target = session.GetPlayers().Skip(1).First();
		session.IdentifyRole([target.Id], MainRoleType.SimpleVillager);
		session.CommitActorBorrowedSeerCheck(
			use.PowerIdentity,
			target.Id,
			FactionAgentKnowledge.KnownNonAgent);
		var commit = session.GetActorBorrowedSeerCheckCommits().Single();

		use.Correlates(commit).Should().BeTrue();
	}

	[Fact]
	public void Correlates_StructurallyMalformedCommit_FailsExplicitly()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower))!;
		var target = session.GetPlayers().Skip(1).First();
		session.IdentifyRole([target.Id], MainRoleType.SimpleVillager);
		session.CommitActorBorrowedSeerCheck(
			use.PowerIdentity,
			target.Id,
			FactionAgentKnowledge.KnownNonAgent);
		var malformed = session.GetActorBorrowedSeerCheckCommits().Single() with
		{
			ActorSetupCardId = Guid.Empty
		};

		var act = () => use.Correlates(malformed);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void Correlates_ValidUnrelatedCommitCoordinates_ReturnsFalse()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower))!;
		var target = session.GetPlayers().Skip(1).First();
		session.IdentifyRole([target.Id], MainRoleType.SimpleVillager);
		session.CommitActorBorrowedSeerCheck(
			use.PowerIdentity,
			target.Id,
			FactionAgentKnowledge.KnownNonAgent);
		var commit = session.GetActorBorrowedSeerCheckCommits().Single();
		IActorBorrowedRolePowerCommit[] unrelated =
		[
			commit with
			{
				PowerIdentity = commit.PowerIdentity with
				{
					PowerInstanceId = Guid.Parse(
						"20000000-0000-0000-0000-000000000002")
				}
			},
			commit with
			{
				ActorSetupCardId = Guid.Parse(
					"30000000-0000-0000-0000-000000000004")
			},
			commit with { TurnNumber = commit.TurnNumber + 1 },
			commit with { CurrentPhase = GamePhase.Day }
		];

		unrelated.Should().OnlyContain(commitment =>
			!use.Correlates(commitment));
	}

	[Fact]
	public void Correlates_ExpiredCapturedActivation_ReturnsFalse()
	{
		var (session, _, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower))!;
		var target = session.GetPlayers().Skip(1).First();
		session.IdentifyRole([target.Id], MainRoleType.SimpleVillager);
		session.CommitActorBorrowedSeerCheck(
			use.PowerIdentity,
			target.Id,
			FactionAgentKnowledge.KnownNonAgent);
		var commit = session.GetActorBorrowedSeerCheckCommits().Single();
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();

		use.Correlates(commit).Should().BeFalse();
	}

	[Fact]
	public void Correlates_ReplacedCapturedActivation_ReturnsFalse()
	{
		var (session, actor, _, _) = CreateActiveActorSession(MainRoleType.Seer);
		var use = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Seer, SeerPower))!;
		var target = session.GetPlayers().Skip(1).First();
		session.IdentifyRole([target.Id], MainRoleType.SimpleVillager);
		session.CommitActorBorrowedSeerCheck(
			use.PowerIdentity,
			target.Id,
			FactionAgentKnowledge.KnownNonAgent);
		var commit = session.GetActorBorrowedSeerCheckCommits().Single();
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		var replacementCard = session.GetModeratorRemainingActorSetupCards()
			.Single(card => card.PrintedRole == MainRoleType.Cupid);
		session.TrySpendActorSetupCard(
			actor.Id,
			replacementCard.Id,
			out _).Should().BeTrue();

		use.Correlates(commit).Should().BeFalse();
	}

	private static (GameSession Session, IPlayer Actor,
		PhysicalCharacterCard SetupCard,
		ActorBorrowedRolePowerActivation Activation) CreateActiveActorSession(
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
		session.TrySpendActorSetupCard(actor.Id, setupCard.Id, out var activation)
			.Should().BeTrue();

		return (session, actor, setupCard, activation!);
	}
}
