using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Werewolves.Client.Resources;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Resources;

public class LocalizationPolicyTests
{
	private static readonly Regex CSharpStringLiteralPattern = new(
		"(@?)\"((?:\"\"|\\\\.|[^\"\\\\])*)\"",
		RegexOptions.Compiled);

	private static readonly Regex FormatPlaceholderPattern = new(
		@"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}",
		RegexOptions.Compiled);

	[Fact]
	public void TestProjects_DoNotHardcodeLocalizedProductionCopy()
	{
		var resources = LoadLocalizedResourceValues();
		var resourcesByValue = resources
			.GroupBy(resource => resource.Value)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
		var resourcesByKey = resources
			.GroupBy(resource => resource.Key)
			.ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

		var violations = EnumeratePolicyTestFiles()
			.SelectMany(file => FindStringLiterals(file)
				.SelectMany(literal => FindResourceLiteralViolations(file, literal, resources, resourcesByValue, resourcesByKey)))
			.Distinct()
			.ToArray();

		violations.Should().BeEmpty(
			ClientTestReferences.AssertionReasons.TestProjectsUseProductionLocalizationContracts);
	}

	private static IEnumerable<string> EnumeratePolicyTestFiles()
	{
		var testRoots = new[]
		{
			Path.Combine(RepositoryRoot, "Werewolves.Client.Tests"),
			Path.Combine(RepositoryRoot, "Werewolves.Core", "Werewolves.Core.Tests")
		};

		return testRoots
			.Where(Directory.Exists)
			.SelectMany(testRoot => Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
			.Where(file => !IsBuildOutputPath(file));
	}

	private static IEnumerable<LocalizationViolation> FindResourceLiteralViolations(
		string file,
		CSharpStringLiteral literal,
		IReadOnlyList<LocalizedResourceValue> resources,
		IReadOnlyDictionary<string, LocalizedResourceValue[]> resourcesByValue,
		IReadOnlyDictionary<string, string> resourcesByKey)
	{
		if (resourcesByValue.TryGetValue(literal.Value, out var exactMatches))
		{
			foreach (var match in exactMatches)
			{
				yield return new LocalizationViolation(RelativePath(file), literal.LineNumber, literal.Value, match.Key);
			}
		}

		foreach (var resource in resources.Where(ShouldFlagContainedValue))
		{
			if (!literal.Value.Equals(resource.Value, StringComparison.Ordinal)
				&& literal.Value.Contains(resource.Value, StringComparison.Ordinal))
			{
				yield return new LocalizationViolation(RelativePath(file), literal.LineNumber, literal.Value, resource.Key);
			}
		}

		foreach (var resource in resources.Where(ShouldFlagFormattedValue))
		{
			if (!literal.Value.Equals(resource.Value, StringComparison.Ordinal)
				&& IsFormattedResourceExample(literal.Value, resource.Value))
			{
				yield return new LocalizationViolation(RelativePath(file), literal.LineNumber, literal.Value, resource.Key);
			}
		}

		if (IsPhaseTurnLiteral(literal.Value, resourcesByKey))
		{
			yield return new LocalizationViolation(RelativePath(file), literal.LineNumber, literal.Value, "Dashboard_PhaseTurnFormat");
		}
	}

	private static bool ShouldFlagContainedValue(LocalizedResourceValue resource) =>
		resource.Value.Length >= 5
		&& (resource.Value.Any(character => character > 127)
			|| resource.Value.Contains(' ', StringComparison.Ordinal)
			|| resource.Key.StartsWith(StatusEffectResourceKeyPrefix, StringComparison.Ordinal)
			|| resource.Key.StartsWith("Dashboard_EliminationReason", StringComparison.Ordinal)
			|| resource.Key == "Dashboard_PhaseDawn");

	private static bool ShouldFlagFormattedValue(LocalizedResourceValue resource)
	{
		if (!FormatPlaceholderPattern.IsMatch(resource.Value))
		{
			return false;
		}

		var fixedText = FormatPlaceholderPattern.Replace(resource.Value, string.Empty);
		return fixedText.Length >= 5
			&& (fixedText.Any(character => character > 127)
				|| fixedText.Contains(' ', StringComparison.Ordinal)
				|| resource.Key.StartsWith(StatusEffectResourceKeyPrefix, StringComparison.Ordinal)
				|| resource.Key.StartsWith("Dashboard_EliminationReason", StringComparison.Ordinal)
				|| resource.Key == "Dashboard_PhaseTurnFormat");
	}

	private static string StatusEffectResourceKeyPrefix =>
		ResourceKeyPrefix(nameof(ClientStrings.StatusEffect_Fallback));

	private static string ResourceKeyPrefix(string key)
	{
		var separatorIndex = key.IndexOf('_');
		return separatorIndex < 0 ? key : key[..(separatorIndex + 1)];
	}

	private static bool IsFormattedResourceExample(string value, string resourceValue)
	{
		var patternBuilder = new StringBuilder("^");
		var currentIndex = 0;

		foreach (Match match in FormatPlaceholderPattern.Matches(resourceValue))
		{
			patternBuilder.Append(Regex.Escape(resourceValue[currentIndex..match.Index]));
			patternBuilder.Append(".+");
			currentIndex = match.Index + match.Length;
		}

		patternBuilder.Append(Regex.Escape(resourceValue[currentIndex..]));
		patternBuilder.Append('$');

		return Regex.IsMatch(value, patternBuilder.ToString(), RegexOptions.CultureInvariant);
	}

	private static bool IsPhaseTurnLiteral(string value, IReadOnlyDictionary<string, string> resourcesByKey)
	{
		var phaseKeys = new[]
		{
			"Dashboard_PhaseNight",
			"Dashboard_PhaseDawn",
			"Dashboard_PhaseDay",
			"Dashboard_PhaseFallback"
		};

		foreach (var key in phaseKeys)
		{
			if (!resourcesByKey.TryGetValue(key, out var phase))
			{
				continue;
			}

			if (value.Length > phase.Length + 1
				&& value.StartsWith($"{phase} ", StringComparison.Ordinal)
				&& int.TryParse(value.AsSpan(phase.Length + 1), out _))
			{
				return true;
			}
		}

		return false;
	}

	private static IReadOnlyList<LocalizedResourceValue> LoadLocalizedResourceValues()
	{
		var resourceFiles = new[]
		{
			Path.Combine(RepositoryRoot, "Werewolves.Client.Shared", "Resources", "ClientStrings.resx"),
			Path.Combine(RepositoryRoot, "Werewolves.Client.Shared", "Resources", "ClientStrings.pt-PT.resx"),
			Path.Combine(RepositoryRoot, "Werewolves.Core", "Werewolves.Core.StateModels", "Resources", "GameStrings.pt-PT.resx"),
			Path.Combine(RepositoryRoot, "Werewolves.Core", "Werewolves.Core.StateModels", "Resources", "GameStrings.resx")
		};

		return resourceFiles
			.SelectMany(LoadResourceValues)
			.Where(resource => !string.IsNullOrWhiteSpace(resource.Value)
				&& resource.Value.Length >= 3
				&& resource.Key != "App_Title")
			.DistinctBy(resource => (resource.Key, resource.Value))
			.ToArray();
	}

	private static IEnumerable<LocalizedResourceValue> LoadResourceValues(string file)
	{
		var document = XDocument.Load(file);
		return document.Root!
			.Elements("data")
			.Select(data => new LocalizedResourceValue(
				(string?)data.Attribute("name") ?? string.Empty,
				data.Element("value")?.Value ?? string.Empty));
	}

	private static IEnumerable<CSharpStringLiteral> FindStringLiterals(string file)
	{
		var text = File.ReadAllText(file);
		foreach (Match match in CSharpStringLiteralPattern.Matches(text))
		{
			var isVerbatim = match.Groups[1].Value == "@";
			var rawValue = match.Groups[2].Value;
			var value = isVerbatim
				? rawValue.Replace("\"\"", "\"", StringComparison.Ordinal)
				: Regex.Unescape(rawValue);
			var lineNumber = text.Take(match.Index).Count(character => character == '\n') + 1;

			yield return new CSharpStringLiteral(value, lineNumber);
		}
	}

	private static bool IsBuildOutputPath(string file) =>
		file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
		|| file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

	private static string RelativePath(string file) =>
		Path.GetRelativePath(RepositoryRoot, file);

	private static string RepositoryRoot => ClientTestReferences.Paths.RepositoryRoot;

	private sealed record CSharpStringLiteral(string Value, int LineNumber);
	private sealed record LocalizedResourceValue(string Key, string Value);
	private sealed record LocalizationViolation(string File, int LineNumber, string Literal, string ResourceKey);
}
