using FluentAssertions;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class FoxRoleTests : DiagnosticTestBase
{
	public enum InvalidFoxSelectionCase
	{
		Multiple,
		Dead,
		Unavailable,
		Stale,
		MismatchedType,
		Incomplete
	}

	public FoxRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_IdentifiesImmediatelyAfterCollectiveWerewolvesSleep()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var foxIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));

		foxIdentification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		foxIdentification.RoleIdentification.Should().Be(MainRoleType.Fox);
		foxIdentification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		foxIdentification.SelectablePlayerIds.Should().Contain(fox.Id);
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					foxIdentification.CreateResponse([fox.Id])));
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(fox.Id);
		policy.FoxAttempts.Should().ContainSingle();
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectFoxCenter);
		policy.FoxAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownEmptyLivingWerewolfAgentGroup_BeginsFoxWithoutCollectiveOperation()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var fox = players[1];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup();
		builder.ConfirmGameStart();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(fox.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should()
			.NotContain(entry =>
				entry.ActionType ==
				NightActionType.WerewolfVictimSelection);
		policy.FoxAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownHolder_AvailablePowerEvaluatesOnceBeforeWakeAndOffersEveryLivingCenterOrDecline()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(fox.Id);
		policy.FoxAttempts.Should().ContainSingle();

		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		selection.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.SingleOptional);
		selection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));
		selection.AffectedPlayerIds.Should().Equal(fox.Id);
		selection.EmptySelectionOptionLabel.Should().NotBeNullOrWhiteSpace();
		policy.FoxAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void PerformedCheck_RequiresOnlyLivingAgentFactsAndIgnoresAnEliminatedUnknownPlayer()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		var center = players[4];
		var eliminatedUnknown = players[5];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeEliminatedPlayer(eliminatedUnknown.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		builder.GetGameState()!.GetFactionAgentKnowledge(
				eliminatedUnknown.Id,
				Faction.Werewolf)
			.Should()
			.Be(FactionAgentKnowledge.Unknown);
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([center.Id])));

		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		builder.GetGameState()!.GameHistoryLog
			.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should()
			.ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownHolder_DeniedPowerEvaluatesOnceAndOmitsFoxInstructions()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: false);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.FoxAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownEmptyHolderSet_OmitsFoxWithoutEvaluatingAvailability()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeCurrentRole(fox.Id, MainRoleType.SimpleVillager);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.FoxAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void PerformedCheck_WithWerewolfInNeighborhood_ReturnsPrivateAffirmativeFeedback()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		var center = players[4];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([center.Id])));

		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		feedback.AffectedPlayerIds.Should().Equal(fox.Id);
		var commit = builder.GetGameState()!.GameHistoryLog
			.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should()
			.ContainSingle()
			.Subject;
		commit.ActionType.Should().Be(NightActionType.FoxCheck);
		commit.ActingPlayerId.Should().Be(fox.Id);
		commit.SourceRole.Should().Be(MainRoleType.Fox);
		commit.TargetIds.Should().BeEmpty();
		commit.ToString().Should().NotContain(center.Id.ToString());
		MarkTestCompleted();
	}

	[Fact]
	public void PerformedCheck_WithTwoLivingPlayers_DeduplicatesDirectionalNeighborsAndReturnsAffirmative()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		foreach (var eliminatedPlayer in players.Skip(2))
		{
			builder.ArrangeEliminatedPlayer(eliminatedPlayer.Id);
		}

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					fox.Id));
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var session = builder.GetGameState()!;
		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			fox.Id);

		neighbors.Clockwise.Should().NotBeNull();
		neighbors.Counterclockwise.Should().NotBeNull();
		neighbors.Clockwise!.Id.Should().Be(werewolf.Id);
		neighbors.Counterclockwise!.Id.Should().Be(werewolf.Id);
		new[]
			{
				fox.Id,
				neighbors.Clockwise.Id,
				neighbors.Counterclockwise.Id
			}
			.ToHashSet()
			.Should()
			.BeEquivalentTo([werewolf.Id, fox.Id]);
		selection.SelectablePlayerIds.Should().BeEquivalentTo(
			[werewolf.Id, fox.Id]);

		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([fox.Id])));

		feedback.PrivateInstruction.Should().Be(
			GameStrings.FoxAffirmativeFeedbackInstruction);
		session.GameHistoryLog
			.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should()
			.ContainSingle()
			.Which.TargetIds.Should()
			.BeEmpty();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				CreateResourceIdentity(policy.FoxAttempts.Single()))
			.Should()
			.BeFalse();
		MarkTestCompleted();
	}

	[Fact]
	public void PerformedCheck_WithoutWerewolfInNeighborhood_ReturnsPrivateNegativeFeedbackAndSpendsPower()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var victim = players[2];
		var center = players[3];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					victim.Id));
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([center.Id])));

		feedback.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealFoxResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().Be(
			GameStrings.FoxNegativeFeedbackInstruction);
		feedback.AffectedPlayerIds.Should().Equal(fox.Id);
		var session = builder.GetGameState()!;
		session.GameHistoryLog
			.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should()
			.ContainSingle();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
				session,
				CreateResourceIdentity(policy.FoxAttempts.Single()))
			.Should()
			.BeTrue();
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(feedback.CreateResponse()));
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(fox.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void NegativeCheck_PermanentlyOmitsFoxOnTheNextNightWithoutReevaluatingAvailability()
	{
		var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Fox,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var fox = players[1];
		var firstVictim = players[2];
		var center = players[3];
		var secondVictim = players[4];
		builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					firstVictim.Id));
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var feedback =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([center.Id])));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(feedback.CreateResponse()));
		var finishFirstNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		finishFirstNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);

		builder.Process(finishFirstNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new()
		{
			[firstVictim.Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		var secondNightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteDayPhaseWithTie());
		secondNightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();

		var finishSecondNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					secondVictim.Id));

        finishSecondNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        policy.FoxAttempts.Should().ContainSingle();
        builder.GetGameState()!.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .ContainSingle();
        MarkTestCompleted();
    }

    [Theory]
    [InlineData(InvalidFoxSelectionCase.Multiple)]
    [InlineData(InvalidFoxSelectionCase.Dead)]
    [InlineData(InvalidFoxSelectionCase.Unavailable)]
    [InlineData(InvalidFoxSelectionCase.Stale)]
    [InlineData(InvalidFoxSelectionCase.MismatchedType)]
    [InlineData(InvalidFoxSelectionCase.Incomplete)]
    public void InvalidCenterResponse_IsSideEffectFreeAndKeepsTheSamePendingSelection(
        InvalidFoxSelectionCase invalidCase)
    {
        var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var builder = CreateBuilder()
            .WithRolePowerAvailabilityPolicy(policy)
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Fox,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var fox = players[1];
        var victim = players[2];
        var center = players[4];
        builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
        builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [werewolf.Id],
                    victim.Id));
        var selection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(wake.CreateResponse()));
        ModeratorResponse response = invalidCase switch
        {
            InvalidFoxSelectionCase.Multiple => new()
            {
                InstructionId = selection.InstructionId,
                Type = ExpectedInputType.PlayerSelection,
                SelectedPlayerIds = new HashSet<Guid>
                {
                    center.Id,
                    fox.Id
                }
            },
            InvalidFoxSelectionCase.Unavailable => new()
            {
                InstructionId = selection.InstructionId,
                Type = ExpectedInputType.PlayerSelection,
                SelectedPlayerIds = new HashSet<Guid> { Guid.NewGuid() }
            },
            InvalidFoxSelectionCase.Stale => new()
            {
                InstructionId = Guid.NewGuid(),
                Type = ExpectedInputType.PlayerSelection,
                SelectedPlayerIds = new HashSet<Guid> { center.Id }
            },
            InvalidFoxSelectionCase.MismatchedType => new()
            {
                InstructionId = selection.InstructionId,
                Type = ExpectedInputType.Continue
            },
            InvalidFoxSelectionCase.Incomplete => new()
            {
                InstructionId = selection.InstructionId,
                Type = ExpectedInputType.PlayerSelection
            },
            InvalidFoxSelectionCase.Dead =>
                selection.CreateResponse([center.Id]),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
        };
        if (invalidCase == InvalidFoxSelectionCase.Dead)
        {
            builder.ArrangeEliminatedPlayer(center.Id);
        }

        var session = builder.GetGameState()!;
        var before = session.Serialize();
        var process = () => builder.Process(response);

        process.Should().Throw<InvalidOperationException>();
        session.Serialize().Should().Be(before);
        var pending = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        pending.InstructionId.Should().Be(selection.InstructionId);
        pending.Semantic.Should().Be(
            ModeratorInstructionSemantic.SelectFoxCenter);
        session.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .BeEmpty();
        GameSessionQueries.IsOneUseRolePowerResourceCommitted(
                session,
                CreateResourceIdentity(policy.FoxAttempts.Single()))
            .Should()
            .BeFalse();
        MarkTestCompleted();
    }

    [Fact]
    public void Decline_SkipsFeedbackAndCommitAndPreservesPowerBeforeSleep()
    {
        var policy = new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var builder = CreateBuilder()
            .WithRolePowerAvailabilityPolicy(policy)
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Fox,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var fox = players[1];
        var victim = players[2];
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var identification =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.CompleteWerewolfNightAction(
                    [werewolf.Id],
                    victim.Id));
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    identification.CreateResponse([fox.Id])));
        var selection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(wake.CreateResponse()));

        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(selection.CreateResponse([])));

        sleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);
        sleep.AffectedPlayerIds.Should().Equal(fox.Id);
        var session = builder.GetGameState()!;
        session.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .BeEmpty();
        GameSessionQueries.IsOneUseRolePowerResourceCommitted(
                session,
                CreateResourceIdentity(policy.FoxAttempts.Single()))
            .Should()
            .BeFalse();
        MarkTestCompleted();
    }

    [Fact]
    public void PendingWake_RecoversInFreshServiceWithoutReevaluatingAvailability()
    {
        var initialPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var builder = CreateBuilder()
            .WithRolePowerAvailabilityPolicy(initialPolicy)
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Fox,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var fox = players[1];
        var victim = players[2];
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var foxIdentification =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.CompleteWerewolfNightAction(
                    [werewolf.Id],
                    victim.Id));
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    foxIdentification.CreateResponse([fox.Id])));
        initialPolicy.FoxAttempts.Should().ContainSingle();

        var recoveredPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: false);
        var recoveredService = new GameService(recoveredPolicy);
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredWake =
            InstructionAssert.ExpectType<ConfirmationInstruction>(
                recoveredService.GetCurrentInstruction(recoveredId));

        recoveredWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
        var selection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                recoveredService.ProcessInstruction(
                    recoveredId,
                    recoveredWake.CreateResponse()));

        selection.Semantic.Should().Be(
            ModeratorInstructionSemantic.SelectFoxCenter);
        selection.AffectedPlayerIds.Should().Equal(fox.Id);
        recoveredPolicy.FoxAttempts.Should().BeEmpty();
        recoveredService.GetGameStateView(recoveredId)!.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .BeEmpty();
        MarkTestCompleted();
    }

    [Fact]
    public void PendingCenterSelection_FreshServiceReplaysWithoutCommitResultOrSpend()
    {
        var initialPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var builder = CreateBuilder()
            .WithRolePowerAvailabilityPolicy(initialPolicy)
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Fox,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var fox = players[1];
        var victim = players[2];
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var identification =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.CompleteWerewolfNightAction(
                    [werewolf.Id],
                    victim.Id));
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    identification.CreateResponse([fox.Id])));
        var selection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(wake.CreateResponse()));
        var resourceIdentity =
            CreateResourceIdentity(initialPolicy.FoxAttempts.Single());

        var recoveredPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: false);
        var recoveredService = new GameService(recoveredPolicy);
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredSelection = ReplayFoxWakeToCenterSelection(
            recoveredService,
            recoveredId,
            fox.Id);
        var recoveredSession =
            recoveredService.GetGameStateView(recoveredId)!;

        recoveredSelection.Should().BeEquivalentTo(
            selection,
            options => options.Excluding(
                instruction => instruction.InstructionId));
        recoveredSelection.PublicAnnouncement.Should().BeNull();
        recoveredSelection.Semantic.Should().Be(
            ModeratorInstructionSemantic.SelectFoxCenter);
        recoveredSession.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .BeEmpty();
        GameSessionQueries.IsOneUseRolePowerResourceCommitted(
                recoveredSession,
                resourceIdentity)
            .Should()
            .BeFalse();
        recoveredPolicy.FoxAttempts.Should().BeEmpty();
        MarkTestCompleted();
    }

    [Fact]
    public void DeclineSleepTail_FreshServiceReplaysWithoutCommitResultOrSpend()
    {
        var initialPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var builder = CreateBuilder()
            .WithRolePowerAvailabilityPolicy(initialPolicy)
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Fox,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var fox = players[1];
        var victim = players[2];
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var identification =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.CompleteWerewolfNightAction(
                    [werewolf.Id],
                    victim.Id));
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    identification.CreateResponse([fox.Id])));
        var selection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(wake.CreateResponse()));
        var resourceIdentity =
            CreateResourceIdentity(initialPolicy.FoxAttempts.Single());
        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(selection.CreateResponse([])));

        var recoveredPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: false);
        var recoveredService = new GameService(recoveredPolicy);
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredSelection = ReplayFoxWakeToCenterSelection(
            recoveredService,
            recoveredId,
            fox.Id);
        var recoveredSleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                recoveredService.ProcessInstruction(
                    recoveredId,
                    recoveredSelection.CreateResponse([])));
        var recoveredSession =
            recoveredService.GetGameStateView(recoveredId)!;

        recoveredSleep.Should().BeEquivalentTo(
            sleep,
            options => options.Excluding(
                instruction => instruction.InstructionId));
        recoveredSleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);
        recoveredSession.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .BeEmpty();
        GameSessionQueries.IsOneUseRolePowerResourceCommitted(
                recoveredSession,
                resourceIdentity)
            .Should()
            .BeFalse();
        recoveredPolicy.FoxAttempts.Should().BeEmpty();

        var finishNight =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                recoveredService.ProcessInstruction(
                    recoveredId,
                    recoveredSleep.CreateResponse()));
        finishNight.Semantic.Should().Be(
            ModeratorInstructionSemantic.FinishNightActions);
        recoveredSession.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .BeEmpty();
        MarkTestCompleted();
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Feedback_RecoversInFreshServicesWithoutRecomputationOrDuplicateCommit(
        int centerIndex,
        bool isAffirmative)
    {
        var initialPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var builder = CreateBuilder()
            .WithRolePowerAvailabilityPolicy(initialPolicy)
            .WithPlayers(5)
            .WithRoles(
                MainRoleType.SimpleWerewolf,
                MainRoleType.Fox,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager);
        builder.StartGame();
        var players = builder.GetGameState()!.GetPlayers().ToArray();
        var werewolf = players[0];
        var fox = players[1];
        var victim = players[2];
        var center = players[centerIndex];
        builder.ArrangeKnownRole(fox.Id, MainRoleType.Fox);
        builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var wake =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.CompleteWerewolfNightAction(
                    [werewolf.Id],
                    victim.Id));
        var selection =
            InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
                builder.Process(wake.CreateResponse()));
        var feedback =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                builder.Process(
                    selection.CreateResponse([center.Id])));

        var recoveredPolicy =
            new RecordingFoxAvailabilityPolicy(isAvailable: true);
        var recoveredService = new GameService(recoveredPolicy);
        var recoveredId = recoveredService.RehydrateSession(
            builder.GetGameState()!.Serialize());
        var recoveredFeedback =
            InstructionAssert.ExpectType<ConfirmationInstruction>(
                recoveredService.GetCurrentInstruction(recoveredId));

        recoveredFeedback.Semantic.Should().Be(
            ModeratorInstructionSemantic.RevealFoxResult);
        recoveredFeedback.PublicAnnouncement.Should().BeNull();
        recoveredFeedback.AffectedPlayerIds.Should().Equal(fox.Id);
        recoveredFeedback.PrivateInstruction.Should().Be(
            isAffirmative
                ? GameStrings.FoxAffirmativeFeedbackInstruction
                : GameStrings.FoxNegativeFeedbackInstruction);
        recoveredFeedback.PrivateInstruction.Should().NotContain(
            center.Id.ToString());
        recoveredPolicy.FoxAttempts.Should().BeEmpty();
        var recoveredSession =
            recoveredService.GetGameStateView(recoveredId)!;
        var publicCommit = recoveredSession.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .ContainSingle()
            .Subject;
        publicCommit.TargetIds.Should().BeEmpty();
        var publicHistory = recoveredSession.GameHistoryLog
            .Select(entry => entry.ToString())
            .ToArray();
        publicHistory.Should().NotContain(entry =>
            entry.Contains(center.Id.ToString(), StringComparison.Ordinal));
        publicHistory.Should().NotContain(entry =>
            entry.Contains(
                GameStrings.FoxAffirmativeFeedbackInstruction,
                StringComparison.Ordinal) ||
            entry.Contains(
                GameStrings.FoxNegativeFeedbackInstruction,
                StringComparison.Ordinal));

        var sleep =
            InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
                recoveredService.ProcessInstruction(
                    recoveredId,
                    recoveredFeedback.CreateResponse()));
        sleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);
        sleep.PublicAnnouncement.Should().NotContain(
            center.Id.ToString());
        sleep.PublicAnnouncement.Should().NotContain(
            GameStrings.FoxAffirmativeFeedbackInstruction);
        sleep.PublicAnnouncement.Should().NotContain(
            GameStrings.FoxNegativeFeedbackInstruction);

        var sleepTailService = new GameService(
            new RecordingFoxAvailabilityPolicy(isAvailable: true));
        var sleepTailId = sleepTailService.RehydrateSession(
            recoveredSession.Serialize());
        var recoveredSleep =
            InstructionAssert.ExpectType<ConfirmationInstruction>(
                sleepTailService.GetCurrentInstruction(sleepTailId));
        recoveredSleep.Semantic.Should().Be(
            ModeratorInstructionSemantic.PutRoleToSleep);
        sleepTailService.GetGameStateView(sleepTailId)!.GameHistoryLog
            .OfType<TargetPrivateRolePowerCommittedLogEntry>()
            .Should()
            .ContainSingle();
        MarkTestCompleted();
    }

    private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
        RolePowerAttempt attempt)
    {
        var resource = attempt.OneUseResource
            ?? throw new InvalidOperationException(
                "The Fox availability attempt requires its one-use Resource.");
        return new OneUseRolePowerResourceIdentity(
            attempt.ActingPlayer.Id,
            attempt.SourceRole,
            attempt.SourcePower.Identifier.Value,
            attempt.PowerInstance.Id,
            attempt.PowerInstance.Origin,
            resource.Id);
    }

    private static SelectPlayersInstruction ReplayFoxWakeToCenterSelection(
        GameService service,
        Guid gameId,
        Guid foxId)
    {
        var foxWake =
            InstructionAssert.ExpectType<ConfirmationInstruction>(
                service.GetCurrentInstruction(gameId));
        foxWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
        foxWake.AffectedPlayerIds.Should().Equal(foxId);
        return InstructionAssert
            .ExpectSuccessWithType<SelectPlayersInstruction>(
                service.ProcessInstruction(
                    gameId,
                    foxWake.CreateResponse()));
    }

    private sealed class RecordingFoxAvailabilityPolicy(bool isAvailable)
        : IRolePowerAvailabilityPolicy
    {
        internal List<RolePowerAttempt> FoxAttempts { get; } = [];

        public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
        {
            if (attempt.SourceRole != MainRoleType.Fox)
            {
                return RolePowerAvailabilityResult.Allowed;
            }

            FoxAttempts.Add(attempt);
            return isAvailable
                ? RolePowerAvailabilityResult.Allowed
                : RolePowerAvailabilityResult.Denied;
        }
    }
}
