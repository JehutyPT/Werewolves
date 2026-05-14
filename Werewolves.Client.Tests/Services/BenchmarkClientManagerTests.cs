using FluentAssertions;
using Werewolves.Client.Services;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class BenchmarkClientManagerTests
{
	[Fact]
	public async Task RunAsync_RunsBenchmarkAndExposesLastResult()
	{
		var manager = new BenchmarkClientManager();

		var result = await manager.RunAsync(gameCount: 8, degreeOfParallelism: 2);

		result.GameCount.Should().Be(8);
		result.DegreeOfParallelism.Should().Be(2);
		manager.IsRunning.Should().BeFalse();
		manager.LastResult.Should().Be(result);
	}
}
