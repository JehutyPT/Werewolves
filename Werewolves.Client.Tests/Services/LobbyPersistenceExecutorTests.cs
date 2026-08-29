using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Services;

public sealed class LobbyPersistenceExecutorTests
{
	[Fact]
	public void Execute_MapsKeepClearAndReplaceToTheExistingSaveStore()
	{
		var store = new RecordingSaveStore("prior bytes");
		var acceptedAggregate = CreateAcceptedAggregate();

		LobbyPersistenceExecutor.Execute(
			store,
			new LobbyPersistenceInstruction.Keep());

		store.Load().Should().Be("prior bytes");
		store.SaveCount.Should().Be(0);
		store.ClearCount.Should().Be(0);

		LobbyPersistenceExecutor.Execute(
			store,
			new LobbyPersistenceInstruction.Replace(acceptedAggregate));

		store.SaveCount.Should().Be(1);
		store.ClearCount.Should().Be(0);
		var recovered = LocalRecoveryPayloadCodec.Deserialize(store.Load()!)
			.Should().BeOfType<StagedLobbyRecoveryPayload>()
			.Subject;
		recovered.PlayerRoster.Select(player => player.Id)
			.Should().Equal(acceptedAggregate.PlayerRoster.Select(player => player.Id));

		LobbyPersistenceExecutor.Execute(
			store,
			new LobbyPersistenceInstruction.Clear());

		store.Load().Should().BeNull();
		store.SaveCount.Should().Be(1);
		store.ClearCount.Should().Be(1);
	}

	[Fact]
	public void Execute_WithDisabledStore_AcceptsEveryInstruction()
	{
		var acceptedAggregate = CreateAcceptedAggregate();

		var act = () =>
		{
			LobbyPersistenceExecutor.Execute(
				DisabledGameSessionSaveStore.Instance,
				new LobbyPersistenceInstruction.Keep());
			LobbyPersistenceExecutor.Execute(
				DisabledGameSessionSaveStore.Instance,
				new LobbyPersistenceInstruction.Replace(acceptedAggregate));
			LobbyPersistenceExecutor.Execute(
				DisabledGameSessionSaveStore.Instance,
				new LobbyPersistenceInstruction.Clear());
		};

		act.Should().NotThrow();
	}

	private static LobbySetupAggregate CreateAcceptedAggregate()
	{
		var state = LobbySetupMetadataFixture.StateWithRoles(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		foreach (var playerName in PlayerNames.DefaultFive)
		{
			state.AddPlayer(playerName);
		}
		state.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++)
		{
			state.IncrementRole(MainRoleType.SimpleVillager);
		}
		var roleLockIn = RoleLockIn.CreateFromPrintedRoles(
			version: 1,
			state.PlayerRoster.Count,
			state.GetSelectedRoles());
		return state.Decide(
			new LobbyChange.AcceptImplicitRoleLockIn(
				expectedCurrentVersion: 0,
				roleLockIn))!.NextAggregate;
	}

	private sealed class RecordingSaveStore(string? payload)
		: IGameSessionSaveStore
	{
		private string? _payload = payload;

		public int SaveCount { get; private set; }
		public int ClearCount { get; private set; }

		public string? Load() => _payload;

		public void Save(string serializedSession)
		{
			SaveCount++;
			_payload = serializedSession;
		}

		public void Clear()
		{
			ClearCount++;
			_payload = null;
		}
	}
}
