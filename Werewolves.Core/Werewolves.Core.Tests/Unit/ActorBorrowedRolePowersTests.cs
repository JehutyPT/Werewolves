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
	private static readonly RolePowerDefinition HunterPower = new(
		new RolePowerIdentifier("hunter-final-shot"),
		RolePowerCategory.Reactive);
	private static readonly RolePowerDefinition ElderSuppressionPower = new(
		new RolePowerIdentifier("elder-village-vote-suppression"),
		RolePowerCategory.Reactive);
	private static readonly RolePowerDefinition KnightDiseasePower = new(
		new RolePowerIdentifier("knight-rusty-sword-disease"),
		RolePowerCategory.Automatic);
	private static readonly RolePowerDefinition ScapegoatPower = new(
		new RolePowerIdentifier("scapegoat-tie-replacement"),
		RolePowerCategory.Automatic);

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
	public void ResolveAfterElimination_HunterFinalShot_DerivesImmutableUse()
	{
		var (session, actor, setupCard, activation) = CreateActiveActorSession(
			MainRoleType.Hunter);
		session.TransitionMainPhase(GamePhase.Day);
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[actor.Id] = MainRoleType.Actor
		});
		var scopeId = $"ActorBorrowedRolePowers:Hunter:{session.TurnNumber}";
		EliminationCascadeRuntimeStore.Configure(
			session,
			[
				new EliminationCascadeReactionBinding(
					new BlockingInteractiveReaction(),
					EliminationCascadeReactionBoundary.Interactive)
			]);
		var cascade = EliminationCascadeStage.CascadeStage(
			PostEliminationCascadeStage.HunterLineageProbe,
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

		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			new ActorBorrowedRolePowerSpec(MainRoleType.Hunter, HunterPower),
			new BorrowedPostEliminationRolePowerContext.HunterFinalShot(
				scopeId,
				[actor.Id]));

		use.Should().NotBeNull();
		use!.Actor.Should().BeSameAs(actor);
		use.PowerInstance.Should().Be(new RolePowerInstance(
			activation.ActivationId,
			MainRoleType.Hunter,
			HunterPower,
			RolePowerInstanceOrigin.Borrowed));
		use.PowerIdentity.Should().Be(new RolePowerInstanceIdentity(
			actor.Id,
			MainRoleType.Hunter,
			HunterPower.Identifier.Value,
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed));
		use.ActorSetupCardId.Should().Be(setupCard.Id);
	}

	[Fact]
	public void ResolveAfterElimination_ScapegoatVoterRestriction_AuthenticatesCommittedParent()
	{
		var (session, actor, activeUse, parent, scopeId) =
			CreateScapegoatPostEliminationSession();

		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Scapegoat,
				ScapegoatPower),
			new BorrowedPostEliminationRolePowerContext
				.ScapegoatVoterRestriction(
					parent.PublicMarkerLogIndex,
					scopeId));

		use.Should().NotBeNull();
		use!.Actor.Should().BeSameAs(actor);
		use.PowerIdentity.Should().Be(activeUse.PowerIdentity);
		use.ActorSetupCardId.Should().Be(activeUse.ActorSetupCardId);
	}

	[Fact]
	public void ResolveAfterElimination_ScapegoatMarkerFromDescendantCommit_FailsExplicitly()
	{
		var (session, _, activeUse, parent, scopeId) =
			CreateScapegoatPostEliminationSession();
		var descendant = CommitScapegoatVoterRestriction(
			session,
			activeUse,
			parent,
			scopeId);

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Scapegoat,
				ScapegoatPower),
			new BorrowedPostEliminationRolePowerContext
				.ScapegoatVoterRestriction(
					descendant.PublicMarkerLogIndex,
					scopeId));

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void ResolveAfterElimination_CommittedScapegoatRestriction_AcceptsExactDescendant()
	{
		var (session, actor, activeUse, parent, scopeId) =
			CreateScapegoatPostEliminationSession();
		var descendant = CommitScapegoatVoterRestriction(
			session,
			activeUse,
			parent,
			scopeId);

		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Scapegoat,
				ScapegoatPower),
			new BorrowedPostEliminationRolePowerContext
				.ScapegoatVoterRestriction(
					parent.PublicMarkerLogIndex,
					scopeId));

		use.Should().NotBeNull();
		use!.Actor.Should().BeSameAs(actor);
		use.PowerIdentity.Should().Be(activeUse.PowerIdentity);
		use.ActorSetupCardId.Should().Be(activeUse.ActorSetupCardId);
		session.GetActorBorrowedScapegoatVoterRestrictionCommits()
			.Should().ContainSingle()
			.Which.Should().Be(descendant);
	}

	[Theory]
	[InlineData(PostEliminationRolePowerCase.Hunter)]
	[InlineData(PostEliminationRolePowerCase.Elder)]
	[InlineData(PostEliminationRolePowerCase.Knight)]
	[InlineData(PostEliminationRolePowerCase.Scapegoat)]
	public void ResolveAfterElimination_ValidContext_ResolvesUse(
		PostEliminationRolePowerCase rolePowerCase)
	{
		var fixture = CreatePostEliminationFixture(rolePowerCase);

		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			fixture.Session,
			fixture.Spec,
			fixture.Context);

		use.Should().NotBeNull();
		use!.Actor.Should().BeSameAs(fixture.Actor);
	}

	[Theory]
	[InlineData(PostEliminationRolePowerCase.Hunter)]
	[InlineData(PostEliminationRolePowerCase.Elder)]
	[InlineData(PostEliminationRolePowerCase.Knight)]
	[InlineData(PostEliminationRolePowerCase.Scapegoat)]
	public void ResolveAfterElimination_ExpiredActivation_ReturnsNull(
		PostEliminationRolePowerCase rolePowerCase)
	{
		var fixture = CreateExpiredActivationFixture(rolePowerCase);

		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			fixture.Session,
			fixture.Spec,
			fixture.Context);

		use.Should().BeNull();
	}

	[Theory]
	[InlineData(PostEliminationRolePowerCase.Hunter)]
	[InlineData(PostEliminationRolePowerCase.Elder)]
	[InlineData(PostEliminationRolePowerCase.Knight)]
	[InlineData(PostEliminationRolePowerCase.Scapegoat)]
	public void ResolveAfterElimination_DifferentActiveSourceRole_ReturnsNull(
		PostEliminationRolePowerCase rolePowerCase)
	{
		var fixture = CreatePostEliminationFixture(rolePowerCase);

		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			fixture.Session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Defender,
				DefenderPower),
			fixture.Context);

		use.Should().BeNull();
	}

	[Theory]
	[InlineData(PostEliminationRolePowerCase.Hunter)]
	[InlineData(PostEliminationRolePowerCase.Elder)]
	[InlineData(PostEliminationRolePowerCase.Knight)]
	[InlineData(PostEliminationRolePowerCase.Scapegoat)]
	public void ResolveAfterElimination_StaleActorRole_FailsExplicitly(
		PostEliminationRolePowerCase rolePowerCase)
	{
		var fixture = CreatePostEliminationFixture(rolePowerCase);
		fixture.Session.AssignRole(
			fixture.Actor.Id,
			MainRoleType.SimpleVillager);

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			fixture.Session,
			fixture.Spec,
			fixture.Context);

		act.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData(PostEliminationRolePowerCase.Hunter)]
	[InlineData(PostEliminationRolePowerCase.Elder)]
	[InlineData(PostEliminationRolePowerCase.Knight)]
	[InlineData(PostEliminationRolePowerCase.Scapegoat)]
	public void ResolveAfterElimination_PrematureContext_FailsExplicitly(
		PostEliminationRolePowerCase rolePowerCase)
	{
		var fixture = CreatePrematurePostEliminationFixture(rolePowerCase);

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			fixture.Session,
			fixture.Spec,
			fixture.Context);

		act.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData(PostEliminationRolePowerCase.Hunter)]
	[InlineData(PostEliminationRolePowerCase.Elder)]
	[InlineData(PostEliminationRolePowerCase.Knight)]
	public void ResolveAfterElimination_MismatchedContext_FailsExplicitly(
		PostEliminationRolePowerCase rolePowerCase)
	{
		var fixture = CreatePostEliminationFixture(rolePowerCase);
		BorrowedPostEliminationRolePowerContext mismatchedContext =
			fixture.Context switch
		{
			BorrowedPostEliminationRolePowerContext.HunterFinalShot hunter =>
				hunter with
				{
					TriggeringPlayerIds =
					[
						fixture.Session.GetPlayers().First(player =>
							player.Id != fixture.Actor.Id).Id
					]
				},
			BorrowedPostEliminationRolePowerContext
				.ElderVillageVoteSuppression elder =>
				elder with { CascadeScopeId = $"{elder.CascadeScopeId}:stale" },
			BorrowedPostEliminationRolePowerContext
				.KnightRustySwordSchedule knight =>
				knight with { CascadeScopeId = $"{knight.CascadeScopeId}:stale" },
			_ => throw new ArgumentOutOfRangeException(nameof(rolePowerCase))
		};

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			fixture.Session,
			fixture.Spec,
			mismatchedContext);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void ResolveAfterElimination_CompletedScapegoatCascade_FailsExplicitly()
	{
		var (session, _, _, parent, scopeId) =
			CreateScapegoatPostEliminationSession();
		session.RecordEliminationCascadeCompletion(scopeId);

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Scapegoat,
				ScapegoatPower),
			new BorrowedPostEliminationRolePowerContext
				.ScapegoatVoterRestriction(
					parent.PublicMarkerLogIndex,
					scopeId));

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void ResolveAfterElimination_CommittedHunterFinalShot_FailsExplicitly()
	{
		var (session, actor, scopeId) =
			CreateHunterPostEliminationSession();
		var spec = new ActorBorrowedRolePowerSpec(
			MainRoleType.Hunter,
			HunterPower);
		var context = new BorrowedPostEliminationRolePowerContext.HunterFinalShot(
			scopeId,
			[actor.Id]);
		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			spec,
			context)!;
		var target = session.GetPlayers().First(player =>
			player.Id != actor.Id &&
			player.State.Health == PlayerHealth.Alive);
		session.CommitActorBorrowedHunterFinalShot(
			use.PowerIdentity,
			scopeId,
			[actor.Id],
			target.Id);

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			spec,
			context);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void ResolveAfterElimination_CommittedElderSuppression_FailsExplicitly()
	{
		var (session, actor, voteLogIndex, scopeId) =
			CreateElderPostEliminationSession();
		var spec = new ActorBorrowedRolePowerSpec(
			MainRoleType.Elder,
			ElderSuppressionPower);
		var context = new BorrowedPostEliminationRolePowerContext
			.ElderVillageVoteSuppression(voteLogIndex, scopeId);
		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			spec,
			context)!;
		session.CommitActorBorrowedElderSuppression(
			use.PowerIdentity,
			voteLogIndex,
			scopeId,
			Guid.NewGuid());

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			spec,
			context);

		act.Should().Throw<InvalidOperationException>();
		actor.State.Health.Should().Be(PlayerHealth.Dead);
	}

	[Fact]
	public void ResolveAfterElimination_CommittedKnightSchedule_FailsExplicitly()
	{
		var (session, actor, eliminationLogIndex, scopeId) =
			CreateKnightPostEliminationSession();
		var spec = new ActorBorrowedRolePowerSpec(
			MainRoleType.KnightWithRustySword,
			KnightDiseasePower);
		var context = new BorrowedPostEliminationRolePowerContext
			.KnightRustySwordSchedule(eliminationLogIndex, scopeId);
		var use = ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			spec,
			context)!;
		var target = session.GetPlayers().First(player =>
			player.Id != actor.Id &&
			player.State.Health == PlayerHealth.Alive);
		session.CommitActorBorrowedKnightRustySwordSchedule(
			use.PowerIdentity,
			target.Id,
			eliminationLogIndex,
			scopeId);

		var act = () => ActorBorrowedRolePowers.ResolveAfterElimination(
			session,
			spec,
			context);

		act.Should().Throw<InvalidOperationException>();
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

	private static (GameSession Session, IPlayer Actor, string ScopeId)
		CreateHunterPostEliminationSession(
			bool createActiveInteractiveReactionBatch = true)
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.Hunter);
		session.TransitionMainPhase(GamePhase.Day);
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[actor.Id] = MainRoleType.Actor
		});
		var scopeId = $"ActorBorrowedRolePowers:Hunter:{session.TurnNumber}";
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
				PostEliminationCascadeStage.HunterLineageProbe,
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
			session.EliminatePlayer(actor.Id, EliminationReason.EventElimination);
			var elimination = new EliminationCascadeElimination(
				actor.Id,
				EliminationReason.EventElimination);
			session.RecordEliminationCascadeBatchResolution(
				scopeId,
				[elimination],
				[elimination]);
		}

		return (session, actor, scopeId);
	}

	private static (GameSession Session, IPlayer Actor, int VoteLogIndex,
		string ScopeId) CreateElderPostEliminationSession(
			bool completeCascade = true)
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.Elder);
		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card =>
				card.Zone == PhysicalCharacterCardZone.DealPool &&
				card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actor.Id,
			actorCard.Card.Id).Should().BeTrue();
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

		return (session, actor, vote.LogIndex, scopeId);
	}

	private static (GameSession Session, IPlayer Actor,
		int EliminationLogIndex, string ScopeId)
		CreateKnightPostEliminationSession(bool completeCascade = true)
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.KnightWithRustySword);
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

		return (session, actor, eliminationLogIndex, scopeId);
	}

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowers.ActorBorrowedRolePowerUse ActiveUse,
		ActorBorrowedScapegoatTieReplacementCommit Parent,
		string ScopeId) CreateScapegoatPostEliminationSession(
			bool recordCascadeBatch = true)
	{
		var (session, actor, _, _) = CreateActiveActorSession(
			MainRoleType.Scapegoat);
		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card =>
				card.Zone == PhysicalCharacterCardZone.DealPool &&
				card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actor.Id,
			actorCard.Card.Id).Should().BeTrue();
		session.TransitionMainPhase(GamePhase.Day);
		session.PerformDayVote(Guid.Empty);
		var vote = GameSessionQueries.GetCurrentDayVoteOutcome(session)!.Value;
		var scopeId = $"Day:{session.TurnNumber}:Vote:{vote.VoteOrdinal}";
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[actor.Id] = MainRoleType.Actor
		});
		var activeUse = ActorBorrowedRolePowers.ResolveActive(
			session,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Scapegoat,
				ScapegoatPower))!;
		session.CommitActorBorrowedScapegoatTieReplacement(
			activeUse.PowerIdentity,
			vote.LogIndex,
			vote.VoteOrdinal,
			scopeId);
		var parent = session.GetActorBorrowedScapegoatTieReplacementCommits()
			.Single();
		session.EliminatePlayer(actor.Id, EliminationReason.EventElimination);
		var elimination = new EliminationCascadeElimination(
			actor.Id,
			EliminationReason.EventElimination);
		if (recordCascadeBatch)
		{
			session.RecordEliminationCascadeBatchResolution(
				scopeId,
				[elimination],
				[elimination]);
		}

		return (session, actor, activeUse, parent, scopeId);
	}

	private static ActorBorrowedScapegoatVoterRestrictionCommit
		CommitScapegoatVoterRestriction(
			GameSession session,
			ActorBorrowedRolePowers.ActorBorrowedRolePowerUse activeUse,
			ActorBorrowedScapegoatTieReplacementCommit parent,
			string scopeId)
	{
		var candidates = session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToArray();
		session.CommitActorBorrowedScapegoatVoterRestriction(
			activeUse.PowerIdentity,
			parent.PublicMarkerLogIndex,
			scopeId,
			candidates,
			[candidates[0]],
			session.TurnNumber + 1,
			Guid.NewGuid());
		return session.GetActorBorrowedScapegoatVoterRestrictionCommits()
			.Single();
	}

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context)
		CreatePostEliminationFixture(PostEliminationRolePowerCase rolePowerCase) =>
			rolePowerCase switch
			{
				PostEliminationRolePowerCase.Hunter => CreateHunterFixture(),
				PostEliminationRolePowerCase.Elder => CreateElderFixture(),
				PostEliminationRolePowerCase.Knight => CreateKnightFixture(),
				PostEliminationRolePowerCase.Scapegoat => CreateScapegoatFixture(),
				_ => throw new ArgumentOutOfRangeException(nameof(rolePowerCase))
			};

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context)
		CreatePrematurePostEliminationFixture(
			PostEliminationRolePowerCase rolePowerCase) => rolePowerCase switch
		{
			PostEliminationRolePowerCase.Hunter => CreateHunterFixture(false),
			PostEliminationRolePowerCase.Elder => CreateElderFixture(false),
			PostEliminationRolePowerCase.Knight => CreateKnightFixture(false),
			PostEliminationRolePowerCase.Scapegoat => CreateScapegoatFixture(false),
			_ => throw new ArgumentOutOfRangeException(nameof(rolePowerCase))
		};

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context)
		CreateExpiredActivationFixture(
			PostEliminationRolePowerCase rolePowerCase)
	{
		var (sourceRole, sourcePower, context) = rolePowerCase switch
		{
			PostEliminationRolePowerCase.Hunter => (
				MainRoleType.Hunter,
				HunterPower,
				(BorrowedPostEliminationRolePowerContext)new
					BorrowedPostEliminationRolePowerContext.HunterFinalShot(
						"expired-hunter",
						[Guid.NewGuid()])),
			PostEliminationRolePowerCase.Elder => (
				MainRoleType.Elder,
				ElderSuppressionPower,
				new BorrowedPostEliminationRolePowerContext
					.ElderVillageVoteSuppression(0, "expired-elder")),
			PostEliminationRolePowerCase.Knight => (
				MainRoleType.KnightWithRustySword,
				KnightDiseasePower,
				new BorrowedPostEliminationRolePowerContext
					.KnightRustySwordSchedule(0, "expired-knight")),
			PostEliminationRolePowerCase.Scapegoat => (
				MainRoleType.Scapegoat,
				ScapegoatPower,
				new BorrowedPostEliminationRolePowerContext
					.ScapegoatVoterRestriction(0, "expired-scapegoat")),
			_ => throw new ArgumentOutOfRangeException(nameof(rolePowerCase))
		};
		var (session, actor, _, _) = CreateActiveActorSession(sourceRole);
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		session.EliminatePlayer(actor.Id, EliminationReason.EventElimination);
		return (
			session,
			actor,
			new ActorBorrowedRolePowerSpec(sourceRole, sourcePower),
			context);
	}

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context) CreateHunterFixture(
			bool createActiveInteractiveReactionBatch = true)
	{
		var (session, actor, scopeId) = CreateHunterPostEliminationSession(
			createActiveInteractiveReactionBatch);
		return (
			session,
			actor,
			new ActorBorrowedRolePowerSpec(MainRoleType.Hunter, HunterPower),
			new BorrowedPostEliminationRolePowerContext.HunterFinalShot(
				scopeId,
				[actor.Id]));
	}

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context) CreateElderFixture(
			bool completeCascade = true)
	{
		var (session, actor, voteLogIndex, scopeId) =
			CreateElderPostEliminationSession(completeCascade);
		return (
			session,
			actor,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Elder,
				ElderSuppressionPower),
			new BorrowedPostEliminationRolePowerContext
				.ElderVillageVoteSuppression(voteLogIndex, scopeId));
	}

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context) CreateKnightFixture(
			bool completeCascade = true)
	{
		var (session, actor, eliminationLogIndex, scopeId) =
			CreateKnightPostEliminationSession(completeCascade);
		return (
			session,
			actor,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.KnightWithRustySword,
				KnightDiseasePower),
			new BorrowedPostEliminationRolePowerContext
				.KnightRustySwordSchedule(eliminationLogIndex, scopeId));
	}

	private static (GameSession Session, IPlayer Actor,
		ActorBorrowedRolePowerSpec Spec,
		BorrowedPostEliminationRolePowerContext Context) CreateScapegoatFixture(
			bool recordCascadeBatch = true)
	{
		var (session, actor, _, parent, scopeId) =
			CreateScapegoatPostEliminationSession(recordCascadeBatch);
		return (
			session,
			actor,
			new ActorBorrowedRolePowerSpec(
				MainRoleType.Scapegoat,
				ScapegoatPower),
			new BorrowedPostEliminationRolePowerContext
				.ScapegoatVoterRestriction(
					parent.PublicMarkerLogIndex,
					scopeId));
	}

	private sealed class BlockingInteractiveReaction : IEliminationCascadeReaction
	{
		public string ReactionId => "actor-borrowed-role-powers-blocking-probe";

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input) =>
			EliminationCascadeReactionResult.NeedInput(
				new StartGameConfirmationInstruction(session.Id));
	}

	private enum PostEliminationCascadeStage
	{
		HunterLineageProbe
	}

	public enum PostEliminationRolePowerCase
	{
		Hunter,
		Elder,
		Knight,
		Scapegoat
	}
}
