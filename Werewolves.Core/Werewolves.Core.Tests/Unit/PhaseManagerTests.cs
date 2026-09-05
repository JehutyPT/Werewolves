using FluentAssertions;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class PhaseManagerTests
{
    // Malformed declarations cannot be supplied through GameService. Exercise the
    // interpreter with real adapters and public lifecycle state, without owner keys.
    [Fact]
    public void UnregisteredDeclaration_ProducesItsInstructionInTheOwningPhase()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var instruction = start;
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night,
            NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [LogicSubPhaseStage.LogicStage(TestStage.Instruction, (_, _) => instruction),
                 NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)],
                possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])]);

        var result = phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        ReadInstruction(result).Should().BeSameAs(instruction);
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
        session.GameHistoryLog.Should().NotContain(entry => entry is Werewolves.Core.StateModels.Log.PhaseTransitionLogEntry);
    }

    [Fact]
    public void DifferentOwningPhase_RejectsBeforeRunningDeclaredWork()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var phase = new PhaseManager<DawnSubPhases>(
            GamePhase.Dawn, DawnSubPhases.Finalize,
            [new(DawnSubPhases.Finalize,
                [LogicSubPhaseStage.LogicStage(TestStage.Instruction, (_, _) => start),
                 NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Day)],
                possibleNextMainPhaseTransitions: [new(GamePhase.Day)])]);

        var act = () => phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        act.Should().Throw<InvalidOperationException>();
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
    }

    [Fact]
    public void UndeclaredDestination_RejectsWithoutLeavingTheOwningPhase()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Day)],
                possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])]);

        var act = () => phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        act.Should().Throw<InvalidOperationException>();
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
    }

    [Fact]
    public void NavigationToDifferentSubPhaseEnum_RejectsWithoutLeavingTheOwningPhase()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [NavigationSubPhaseStage.NavigationEndStageSilent(DawnSubPhases.Finalize)],
                possibleNextSubPhases: [NightSubPhases.Start])]);

        var act = () => phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        act.Should().Throw<InvalidOperationException>();
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
    }

    [Fact]
    public void MainPhaseExit_PreservesTransitionInstructionAndStopsBeforeEnteredWork()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [NavigationSubPhaseStage.NavigationEndStage(TestStage.Instruction,
                    (_, _) => new MainPhaseHandlerResult(start, GamePhase.Dawn))],
                possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])]);

        var result = phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        ReadInstruction(result).Should().BeSameAs(start);
        session.GetCurrentPhase().Should().Be(GamePhase.Dawn);
        session.TurnNumber.Should().Be(1);
        session.GameHistoryLog.OfType<Werewolves.Core.StateModels.Log.PhaseTransitionLogEntry>()
            .Should().ContainSingle();
    }

    [Fact]
    public void PauseWithoutInstruction_RejectsBeforeRepeatingTheStage()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var stage = new InvalidPauseStage();
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [stage, NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)],
                possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])]);

        var act = () => phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        act.Should().Throw<InvalidOperationException>();
        stage.Attempts.Should().Be(1);
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
    }

    [Fact]
    public void MissingEntryDeclaration_RejectsWithoutInventingNavigation()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var phase = new PhaseManager<NightSubPhases>(GamePhase.Night, NightSubPhases.Start, []);

        var act = () => phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        act.Should().Throw<InvalidOperationException>();
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
    }

    [Fact]
    public void EmptyStageSequence_RejectsWithoutInventingNavigation()
    {
        var act = () => new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start, [new(NightSubPhases.Start, [])]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoAvailableDeclaredStage_RejectsWithoutRepeatingCompletedWork(bool pauseAtHook)
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        builder.StartGame();
        builder.ConfirmGameStart();
        if (pauseAtHook)
        {
            builder.ConfirmNightStart();
        }
        var session = (GameSession)builder.GetGameState()!;
        var pending = builder.GetCurrentInstruction()!;
        // The public lifecycle completed NightStart; it may also have an active
        // hook outside this deliberately incomplete declaration. Neither permits
        // entering the completed stage or inventing a fallback stage.
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [NavigationSubPhaseStage.NavigationEndStage(NightSubPhaseStage.NightStart,
                    (_, _) => new MainPhaseHandlerResult(null, GamePhase.Dawn))],
                possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])]);

        var act = () => phase.ProcessInputAndUpdatePhase(session, new ModeratorResponse
        {
            InstructionId = pending.InstructionId,
            Type = pauseAtHook ? ExpectedInputType.PlayerSelection : ExpectedInputType.Continue
        });

        act.Should().Throw<InvalidOperationException>();
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
        builder.GetCurrentInstruction().Should().BeSameAs(pending);
    }

    [Fact]
    public void MissingNavigationOutcome_RejectsInsteadOfChoosingADestination()
    {
        var builder = GameTestBuilder.Create().WithSimpleGame(5, 1, false);
        var start = builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var phase = new PhaseManager<NightSubPhases>(
            GamePhase.Night, NightSubPhases.Start,
            [new(NightSubPhases.Start,
                [NavigationSubPhaseStage.NavigationEndStage(TestStage.Instruction, (_, _) => null!)],
                possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])]);

        var act = () => phase.ProcessInputAndUpdatePhase(session, start.CreateResponse());

        act.Should().Throw<InvalidOperationException>();
        session.GetCurrentPhase().Should().Be(GamePhase.Night);
    }

    // Production adapters never emit this invalid pause. A bounded malformed
    // adapter exercises protocol rejection without forging execution state.
    private sealed class InvalidPauseStage() : SubPhaseStage(TestStage.Instruction)
    {
        internal int Attempts { get; private set; }

        protected override PhaseHandlerResult InnerExecute(GameSession session, ModeratorResponse input)
        {
            if (++Attempts > 1)
            {
                throw new InvalidOperationException("Invalid pause was repeated.");
            }
            return new StayInSubPhaseHandlerResult(null, StageComplete: false);
        }
    }

    private static Werewolves.Core.StateModels.Models.ModeratorInstruction? ReadInstruction(
        PhaseExecutionResult result) => result switch
        {
            PhaseExecutionResult.InstructionReady ready => ready.Instruction,
            PhaseExecutionResult.PhaseExited exited => exited.TransitionInstruction,
            _ => throw new InvalidOperationException()
        };

    private enum TestStage { Instruction }
}
