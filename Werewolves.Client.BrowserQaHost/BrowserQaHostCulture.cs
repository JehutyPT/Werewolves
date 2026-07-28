using System.Globalization;
using Werewolves.Client.Resources;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Client.BrowserQaHost;

public static class BrowserQaHostCulture
{
	public const string EnvironmentVariableName = "WEREWOLVES_BROWSER_QA_CULTURE";

	public static readonly CultureInfo PortugueseCulture = CultureInfo.GetCultureInfo("pt-PT");
	public static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

	public static void Use(string? cultureName)
	{
		var culture = string.IsNullOrWhiteSpace(cultureName)
			? PortugueseCulture
			: CultureInfo.GetCultureInfo(cultureName);

		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
		ClientStrings.Culture = culture;
		GameStrings.Culture = culture;
	}

	public static void UsePortuguese() => Use(PortugueseCulture.Name);
}
