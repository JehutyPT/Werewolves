using System.Globalization;
using Werewolves.Client.Resources;

namespace Werewolves.Client.BrowserQaHost;

public static class BrowserQaHostCulture
{
	public static readonly CultureInfo PortugueseCulture = CultureInfo.GetCultureInfo("pt-PT");

	public static void UsePortuguese()
	{
		CultureInfo.DefaultThreadCurrentCulture = PortugueseCulture;
		CultureInfo.DefaultThreadCurrentUICulture = PortugueseCulture;
		ClientStrings.Culture = PortugueseCulture;
	}
}
