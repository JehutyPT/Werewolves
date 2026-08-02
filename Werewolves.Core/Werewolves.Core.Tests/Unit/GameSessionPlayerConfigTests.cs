using FluentAssertions;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class GameSessionPlayerConfigTests
{
	[Fact]
	public void Create_WithStableIdentityAndDisplayName_PreservesBothValues()
	{
		var playerId = Guid.Parse("10000000-0000-0000-0000-000000000001");

		var player = new GameSessionPlayerConfig(playerId, "Ana");

		player.Id.Should().Be(playerId);
		player.Name.Should().Be("Ana");
	}

	[Fact]
	public void Create_WithEmptyIdentity_IsRejected()
	{
		var act = () => new GameSessionPlayerConfig(Guid.Empty, "Ana");

		act.Should().Throw<ArgumentException>()
			.WithParameterName("id");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Create_WithoutDisplayName_IsRejected(string? name)
	{
		var act = () => new GameSessionPlayerConfig(
			Guid.Parse("20000000-0000-0000-0000-000000000001"),
			name!);

		act.Should().Throw<ArgumentException>()
			.WithParameterName("name");
	}

	[Fact]
	public void Equality_UsesStableIdentityAndIgnoresDisplayNameMetadata()
	{
		var sharedId = Guid.Parse("30000000-0000-0000-0000-000000000001");
		var sameIdentity = new GameSessionPlayerConfig(sharedId, "Ana");
		var renamedIdentity = new GameSessionPlayerConfig(sharedId, "Beatriz");
		var differentIdentity = new GameSessionPlayerConfig(
			Guid.Parse("30000000-0000-0000-0000-000000000002"),
			"Ana");

		renamedIdentity.Should().Be(sameIdentity);
		renamedIdentity.GetHashCode().Should().Be(sameIdentity.GetHashCode());
		differentIdentity.Should().NotBe(sameIdentity);
	}
}
