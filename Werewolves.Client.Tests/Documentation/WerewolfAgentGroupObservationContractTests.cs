using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Documentation;

public class WerewolfAgentGroupObservationContractTests
{
	[Fact]
	public void WerewolfAgentGroupObservationDocs_DefineExactInitialAllAlivePrivateFailClosedContract()
	{
		var context = ReadRepositoryFile("CONTEXT.md");
		var decision = ReadRepositoryFile(
			"docs",
			"adr",
			"0019-initial-beneficiary-closure-establishes-beneficiary-knowledge.md");
		var moderatorContract = ReadRepositoryFile("docs", "contracts", "moderator-interaction.md");

		context.Should().ContainAll(
			"**Faction Agent Group Observation**:",
			"complete set of Players observed acting together",
			"establishes Faction Agent membership, not exact Roles");

		decision.Should().ContainAll(
			"## Amendment: validate complete initial Agent-group cardinality",
			"initial all-alive Werewolf collective boundary",
			"constrains the private Faction Agent Group Observation to exactly that many Players",
			"neither offers nor accepts this exhaustive observation and commits nothing",
			"fails closed without a Moderator override",
			"exact count is shown only on the Moderator-private instruction");

		moderatorContract.Should().ContainAll(
			"### Faction Agent Group Observation",
			"initial all-alive boundary",
			"complete group of exactly the required number of Players",
			"does not identify exact Roles",
			"neither offers nor accepts the observation and commits nothing",
			"fails closed without a Moderator override",
			"required count appears only in Moderator-private guidance",
			"never appears in public-table copy or public history");
	}

	private static string ReadRepositoryFile(params string[] segments) =>
		File.ReadAllText(Path.Combine([ClientTestReferences.Paths.RepositoryRoot, .. segments]));
}
