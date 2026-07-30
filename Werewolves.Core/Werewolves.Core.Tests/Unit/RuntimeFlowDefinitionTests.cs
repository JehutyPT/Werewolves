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
    public void FirstNightActions_ResolveWolfHoundAfterWildChildBeforeBearTamerDefenderAndCollectiveWerewolf()
	{
		var listeners = GameFlowManager.HookListeners[NightMainActionLoop];
		var wolfHoundIndex = ListenerIndex(listeners, WolfHound);

		ListenerIndex(listeners, WildChild).Should().BeLessThan(wolfHoundIndex);
		wolfHoundIndex.Should().BeLessThan(ListenerIndex(listeners, BearTamer));
		wolfHoundIndex.Should().BeLessThan(ListenerIndex(listeners, Defender));
		wolfHoundIndex.Should().BeLessThan(ListenerIndex(listeners, SimpleWerewolf));
	}

    [Fact]
    public void LaterNightWerewolfActions_ResolveAccursedThenWhiteThenBigBadWolf()
    {
        var listeners = GameFlowManager.HookListeners[NightMainActionLoop];

		ListenerIndex(listeners, AccursedWolfFather)
			.Should().BeLessThan(ListenerIndex(listeners, WhiteWerewolf));
			ListenerIndex(listeners, WhiteWerewolf)
				.Should().BeLessThan(ListenerIndex(listeners, BigBadWolf));
			ListenerIndex(listeners, BigBadWolf)
				.Should().BeLessThan(ListenerIndex(listeners, Seer));
    }

    [Fact]
    public void NightInformationActions_ResolveSeerThenWitchThenGypsy()
    {
        var listeners = GameFlowManager.HookListeners[NightMainActionLoop];

		ListenerIndex(listeners, Seer)
			.Should().BeLessThan(ListenerIndex(listeners, Witch));
		ListenerIndex(listeners, Witch)
			.Should().BeLessThan(ListenerIndex(listeners, Gypsy));
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
