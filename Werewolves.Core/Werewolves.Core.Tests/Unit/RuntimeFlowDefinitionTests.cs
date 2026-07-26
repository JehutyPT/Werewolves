using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;
using static Werewolves.Core.StateModels.Enums.GameHook;
using static Werewolves.Core.StateModels.Enums.MainRoleType;
using static Werewolves.Core.StateModels.Models.ListenerIdentifier;

namespace Werewolves.Core.Tests.Unit;

public class RuntimeFlowDefinitionTests
{
    [Fact]
	public void NightActions_ResolveCollectiveWerewolfImmediatelyBeforeFox()
	{
		var listeners = GameFlowManager.HookListeners[NightMainActionLoop];
		var collectiveWerewolfIndex = ListenerIndex(listeners, SimpleWerewolf);

		collectiveWerewolfIndex.Should().Be(ListenerIndex(listeners, Fox) - 1);
		ListenerIndex(listeners, WildChild).Should().BeLessThan(collectiveWerewolfIndex);
	}

    [Fact]
    public void FirstNightActions_ResolveWolfHoundBeforeBearTamerAndDefender()
	{
		var listeners = GameFlowManager.HookListeners[NightMainActionLoop];
		var wolfHoundIndex = ListenerIndex(listeners, WolfHound);

		wolfHoundIndex.Should().BeLessThan(ListenerIndex(listeners, BearTamer));
		wolfHoundIndex.Should().BeLessThan(ListenerIndex(listeners, Defender));
	}

    [Fact]
    public void LaterNightWerewolfActions_ResolveAccursedThenWhiteThenBigBadWolf()
    {
        var listeners = GameFlowManager.HookListeners[NightMainActionLoop];

		ListenerIndex(listeners, AccursedWolfFather)
			.Should().BeLessThan(ListenerIndex(listeners, WhiteWerewolf));
		ListenerIndex(listeners, WhiteWerewolf)
			.Should().BeLessThan(ListenerIndex(listeners, BigBadWolf));
    }

    [Fact]
    public void NightInformationActions_ResolveSeerBeforeWitch()
    {
        var listeners = GameFlowManager.HookListeners[NightMainActionLoop];

		ListenerIndex(listeners, Seer)
			.Should().BeLessThan(ListenerIndex(listeners, Witch));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VoteOutcome_WithEliminationOrRepeat_RoutesThroughEliminationCascade(
        bool shouldVoteRepeat,
        bool hasPlayerElimination)
    {
        var nextSubPhase = GameFlowManager.ChoosePostVoteOutcomeSubPhase(
            shouldVoteRepeat,
            hasPlayerElimination);

        nextSubPhase.Should().Be(DaySubPhases.ProcessVoteEliminationCascade);
    }

    [Fact]
    public void VoteOutcome_WithoutEliminationOrRepeat_RoutesToFinalize()
    {
        var nextSubPhase = GameFlowManager.ChoosePostVoteOutcomeSubPhase(
            shouldVoteRepeat: false,
            hasPlayerElimination: false);

        nextSubPhase.Should().Be(DaySubPhases.Finalize);
    }

    [Theory]
    [InlineData(true, DaySubPhases.DetermineVoteType)]
    [InlineData(false, DaySubPhases.Finalize)]
    public void VoteRepeatDecision_IsMadeOnlyAfterEliminationCascade(
        bool shouldVoteRepeat,
        DaySubPhases expectedSubPhase)
	{
		var nextSubPhase = GameFlowManager.ChoosePostVoteEliminationCascadeSubPhase(shouldVoteRepeat);

		nextSubPhase.Should().Be(expectedSubPhase);
	}

	private static int ListenerIndex(
		List<ListenerIdentifier> listeners,
		MainRoleType role)
	{
		var index = listeners.IndexOf(Listener(role));
		index.Should().BeGreaterThanOrEqualTo(0, $"{role} is a named runtime-order participant");
		return index;
	}
}
