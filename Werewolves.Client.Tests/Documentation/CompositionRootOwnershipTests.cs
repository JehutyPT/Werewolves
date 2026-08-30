using System.Text.RegularExpressions;
using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Documentation;

public sealed class CompositionRootOwnershipTests
{
	private static readonly string[] CommonGraphServiceNames =
	[
		"GameService",
		"LobbySetupMetadata",
		"LobbySetupState",
		"LobbyEvaluationCoordinator",
		"GameClientManager"
	];

	[Fact]
	public void CompositionRoots_DelegateSessionAndLobbyGraphToSharedModule()
	{
		AssertRootDelegation(
			ClientTestReferences.Paths.RepositoryPath("Werewolves.Client", "MauiProgram.cs"),
			"Singleton",
			"CreateMauiLobbySetupState");
		AssertRootDelegation(
			ClientTestReferences.Paths.RepositoryPath(
				"Werewolves.Client.BrowserQaHost",
				"BrowserQaHostServiceCollectionExtensions.cs"),
			"Scoped",
			"CreateSeededLobby");
		AssertRootDelegation(
			ClientTestReferences.Paths.RepositoryPath(
				"Werewolves.Client.Tests",
				"Helpers",
				"ModeratorComponentTestContext.cs"),
			"Singleton",
			"metadata=>newLobbySetupState(metadata)");

		var mauiSource = File.ReadAllText(ClientTestReferences.Paths.RepositoryPath(
			"Werewolves.Client",
			"MauiProgram.cs"));
		var factoryBody = ExtractMethodBody(mauiSource, "CreateMauiLobbySetupState");
		var debugStart = factoryBody.IndexOf("#if DEBUG", StringComparison.Ordinal);
		var debugEnd = factoryBody.IndexOf("#endif", StringComparison.Ordinal);

		debugStart.Should().BeGreaterThanOrEqualTo(0);
		debugEnd.Should().BeGreaterThan(debugStart);
		Compact(factoryBody).Should().Contain("newLobbySetupState(metadata)");
		factoryBody[debugStart..debugEnd].Should().Contain("state.AddPlayer(");
	}

	private static void AssertRootDelegation(
		string path,
		string expectedLifetime,
		string expectedFactory)
	{
		var source = File.ReadAllText(path);
		var invocation = FindInvocations(
			source,
			"AddModeratorSessionAndLobbyServices").Should().ContainSingle().Subject;
		var arguments = SplitTopLevelArguments(invocation);

		arguments.Should().HaveCount(2);
		Compact(arguments[0]).Should().Be($"ServiceLifetime.{expectedLifetime}");
		Compact(arguments[1]).Should().Be(expectedFactory);
		FindDirectCommonRegistrations(source).Should().BeEmpty();
	}

	private static IReadOnlyList<string> FindInvocations(string source, string methodName)
	{
		var invocations = new List<string>();
		var searchStart = 0;
		while (searchStart < source.Length)
		{
			var methodIndex = source.IndexOf(methodName, searchStart, StringComparison.Ordinal);
			if (methodIndex < 0)
			{
				break;
			}

			var openParenthesis = methodIndex + methodName.Length;
			while (openParenthesis < source.Length && char.IsWhiteSpace(source[openParenthesis]))
			{
				openParenthesis++;
			}
			if (openParenthesis < source.Length && source[openParenthesis] == '(')
			{
				var closeParenthesis = FindMatchingDelimiter(
					source,
					openParenthesis,
					'(',
					')');
				invocations.Add(source[(openParenthesis + 1)..closeParenthesis]);
				searchStart = closeParenthesis + 1;
				continue;
			}

			searchStart = methodIndex + methodName.Length;
		}

		return invocations;
	}

	private static IReadOnlyList<string> SplitTopLevelArguments(string arguments)
	{
		var result = new List<string>();
		var start = 0;
		var parentheses = 0;
		var brackets = 0;
		var braces = 0;
		for (var index = 0; index < arguments.Length; index++)
		{
			switch (arguments[index])
			{
				case '(':
					parentheses++;
					break;
				case ')':
					parentheses--;
					break;
				case '[':
					brackets++;
					break;
				case ']':
					brackets--;
					break;
				case '{':
					braces++;
					break;
				case '}':
					braces--;
					break;
				case ',' when parentheses == 0 && brackets == 0 && braces == 0:
					result.Add(arguments[start..index]);
					start = index + 1;
					break;
			}
		}
		result.Add(arguments[start..]);
		return result;
	}

	private static IReadOnlyList<string> FindDirectCommonRegistrations(string source)
	{
		var registrations = Regex.Matches(
			source,
			@"\b(?:TryAdd|Add)(?:Singleton|Scoped|Transient)\s*<\s*(?<service>[A-Za-z0-9_.]+)")
			.Select(match => match.Groups["service"].Value.Split('.').Last())
			.Where(CommonGraphServiceNames.Contains)
			.ToArray();
		return registrations;
	}

	private static string ExtractMethodBody(string source, string methodName)
	{
		var methodIndex = source.LastIndexOf(methodName, StringComparison.Ordinal);
		methodIndex.Should().BeGreaterThanOrEqualTo(0);
		var bodyStart = source.IndexOf('{', methodIndex);
		bodyStart.Should().BeGreaterThan(methodIndex);
		var bodyEnd = FindMatchingDelimiter(source, bodyStart, '{', '}');
		return source[(bodyStart + 1)..bodyEnd];
	}

	private static int FindMatchingDelimiter(
		string source,
		int openIndex,
		char open,
		char close)
	{
		var depth = 0;
		for (var index = openIndex; index < source.Length; index++)
		{
			if (source[index] == open)
			{
				depth++;
			}
			else if (source[index] == close && --depth == 0)
			{
				return index;
			}
		}

		throw new InvalidOperationException($"Unbalanced {open}{close} delimiters.");
	}

	private static string Compact(string source) =>
		Regex.Replace(source, @"\s+", string.Empty);
}
