namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class PlatformChrome
	{
		public static string AndroidAccentColor =>
			$"<color name=\"colorAccent\">{Css.ColorValues.Accent}</color>";

		public static string AndroidPrimaryColor =>
			$"<color name=\"colorPrimary\">{Css.ColorValues.DarkBackground}</color>";

		public static string AndroidPrimaryDarkColor =>
			$"<color name=\"colorPrimaryDark\">{Css.ColorValues.DarkBackground}</color>";

		public static string AppBackgroundColorResource =>
			$"<Color x:Key=\"WerewolvesBackground\">{Css.ColorValues.DarkBackground}</Color>";

		public const string AppDarkThemeAssignment =
			"UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;";

		public const string MainPageBackgroundResource =
			"BackgroundColor=\"{StaticResource WerewolvesBackground}\"";

		public static string MauiIconDarkBackground =>
			$"MauiIcon Include=\"Resources\\AppIcon\\appicon.svg\" ForegroundFile=\"Resources\\AppIcon\\appiconfg.svg\" Color=\"{Css.ColorValues.DarkBackground}\"";

		public static string MauiSplashDarkBackground =>
			$"MauiSplashScreen Include=\"Resources\\Splash\\splash.svg\" Color=\"{Css.ColorValues.DarkBackground}\"";

		public const string PlistDarkStyle = "<string>Dark</string>";
		public const string PlistUserInterfaceStyleKey = "<key>UIUserInterfaceStyle</key>";
	}
}
