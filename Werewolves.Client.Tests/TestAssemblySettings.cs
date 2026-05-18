using System.Globalization;
using System.Runtime.CompilerServices;
using Werewolves.Client.Resources;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Werewolves.Client.Tests;

internal static class TestAssemblySettings
{
	[ModuleInitializer]
	public static void ConfigureCulture()
	{
		var portugueseCulture = CultureInfo.GetCultureInfo("pt-PT");
		CultureInfo.DefaultThreadCurrentCulture = portugueseCulture;
		CultureInfo.DefaultThreadCurrentUICulture = portugueseCulture;
		ClientStrings.Culture = portugueseCulture;
	}
}
