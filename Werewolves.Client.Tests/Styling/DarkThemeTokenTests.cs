using System.Text.RegularExpressions;
using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
using Css = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css;
using PlatformChrome = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlatformChrome;
using Xunit;

namespace Werewolves.Client.Tests.Styling;

public class DarkThemeTokenTests
{
	private static readonly Regex ColorLiteralPattern = new(
		@"#[0-9a-f]{3,8}\b|rgba?\([^)]*\)|hsla?\([^)]*\)|\b(?:black|white)\b",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	[Fact]
	public void AppCss_ConsumesColorValuesThroughDesignTokens()
	{
		var literals = FindColorLiterals(SharedPath("wwwroot/css/app.css"));

		literals.Should().BeEmpty();
	}

	[Fact]
	public void RootDocument_UsesDarkThemeTokensBeforePagesRender()
	{
		var appCss = File.ReadAllText(SharedPath("wwwroot/css/app.css"));

		appCss.Should().MatchRegex(Css.RootDocumentDarkThemePattern);
	}

	[Fact]
	public void MauiHost_UsesDarkChromeAcrossSupportedSurfaces()
	{
		File.ReadAllText(ClientPath("App.xaml"))
			.Should().Contain(PlatformChrome.AppBackgroundColorResource);

		File.ReadAllText(ClientPath("App.xaml.cs"))
			.Should().Contain(PlatformChrome.AppDarkThemeAssignment);

		File.ReadAllText(ClientPath("MainPage.xaml"))
			.Should().Contain(PlatformChrome.MainPageBackgroundResource);

		var projectFile = File.ReadAllText(ClientPath("Werewolves.UI.MobileClient.csproj"));
		projectFile.Should().Contain(PlatformChrome.MauiIconDarkBackground);
		projectFile.Should().Contain(PlatformChrome.MauiSplashDarkBackground);

		var androidColors = File.ReadAllText(ClientPath("Platforms/Android/Resources/values/colors.xml"));
		androidColors.Should().Contain(PlatformChrome.AndroidPrimaryColor);
		androidColors.Should().Contain(PlatformChrome.AndroidPrimaryDarkColor);
		androidColors.Should().Contain(PlatformChrome.AndroidAccentColor);

		File.ReadAllText(ClientPath("Platforms/iOS/Info.plist"))
			.Should().Contain(PlatformChrome.PlistUserInterfaceStyleKey)
			.And.Contain(PlatformChrome.PlistDarkStyle);

		File.ReadAllText(ClientPath("Platforms/MacCatalyst/Info.plist"))
			.Should().Contain(PlatformChrome.PlistUserInterfaceStyleKey)
			.And.Contain(PlatformChrome.PlistDarkStyle);
	}

	[Fact]
	public void TextTokens_HaveReadableContrastAgainstDarkSurfaces()
	{
		var tokens = ReadHexTokens();
		var foregrounds = Css.Tokens.ReadableForegrounds;
		var backgrounds = Css.Tokens.DarkSurfaces;

		var failures = foregrounds
			.SelectMany(foreground => backgrounds.Select(background => new
			{
				Foreground = foreground,
				Background = background,
				Ratio = ContrastRatio(tokens[foreground], tokens[background])
			}))
			.Where(result => result.Ratio < Css.MinimumTextContrastRatio)
			.Select(result => $"{result.Foreground} on {result.Background}: {result.Ratio:F2}")
			.ToArray();

		failures.Should().BeEmpty();

		ContrastRatio(tokens[Css.Tokens.Background], tokens[Css.Tokens.Accent])
			.Should().BeGreaterThanOrEqualTo(Css.MinimumTextContrastRatio);
		ContrastRatio(tokens[Css.Tokens.Background], tokens[Css.Tokens.AccentBright])
			.Should().BeGreaterThanOrEqualTo(Css.MinimumTextContrastRatio);
	}

	[Fact]
	public void Pages_DoNotUseInlineColorLiterals()
	{
		var pages = Directory.GetFiles(SharedPath("Components/Pages"), "*.razor");
		pages.Should().NotBeEmpty();

		var inlineColorLiterals = pages
			.SelectMany(FindColorLiterals)
			.ToArray();

		inlineColorLiterals.Should().BeEmpty();
	}

	[Fact]
	public void Pages_RenderInsideDarkShells()
	{
		var pages = Directory.GetFiles(SharedPath("Components/Pages"), "*.razor");
		pages.Should().NotBeEmpty();

		// Deprecated temporary scaffold: replace with ADR-0006/bUnit or browser-host rendered shell checks.
		var pagesWithoutDarkShell = pages
			.Where(path => !Regex.IsMatch(File.ReadAllText(path), Css.PageDarkShellPattern))
			.Select(path => Path.GetRelativePath(ClientTestReferences.Paths.RepositoryRoot, path))
			.ToArray();

		pagesWithoutDarkShell.Should().BeEmpty();
	}

	private static IReadOnlyList<string> FindColorLiterals(string path)
	{
		return File.ReadLines(path)
			.SelectMany((line, index) => ColorLiteralPattern.Matches(line)
				.Select(match => $"{Path.GetRelativePath(ClientTestReferences.Paths.RepositoryRoot, path)}:{index + 1}: {match.Value}"))
			.ToArray();
	}

	private static IReadOnlyDictionary<string, string> ReadHexTokens()
	{
		var designTokens = File.ReadAllText(SharedPath("wwwroot/css/design-tokens.css"));
		var tokenPattern = new Regex(@"^\s*(--ww-[\w-]+):\s*(#[0-9a-f]{6})\s*;", RegexOptions.IgnoreCase | RegexOptions.Multiline);

		return tokenPattern.Matches(designTokens)
			.ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
	}

	private static double ContrastRatio(string foregroundHex, string backgroundHex)
	{
		var foreground = RelativeLuminance(foregroundHex);
		var background = RelativeLuminance(backgroundHex);
		var lighter = Math.Max(foreground, background);
		var darker = Math.Min(foreground, background);

		return (lighter + 0.05) / (darker + 0.05);
	}

	private static double RelativeLuminance(string hex)
	{
		var red = LinearChannel(Convert.ToInt32(hex.Substring(1, 2), 16) / 255d);
		var green = LinearChannel(Convert.ToInt32(hex.Substring(3, 2), 16) / 255d);
		var blue = LinearChannel(Convert.ToInt32(hex.Substring(5, 2), 16) / 255d);

		return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
	}

	private static double LinearChannel(double channel)
	{
		return channel <= 0.03928
			? channel / 12.92
			: Math.Pow((channel + 0.055) / 1.055, 2.4);
	}

	private static string ClientPath(params string[] relativeSegments)
	{
		return ClientTestReferences.Paths.ClientPath(relativeSegments);
	}

	private static string SharedPath(params string[] relativeSegments)
	{
		return ClientTestReferences.Paths.SharedPath(relativeSegments);
	}
}
