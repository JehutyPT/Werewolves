namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class Html
	{
		public static class AriaValues
		{
			public const string False = "false";
			public const string True = "true";
		}

		public static class Attributes
		{
			public const string AriaExpanded = "aria-expanded";
			public const string AriaLabel = "aria-label";
			public const string AriaPressed = "aria-pressed";
			public const string Class = "class";
			public const string Disabled = "disabled";
			public const string Title = "title";
			public const string Type = "type";
		}

		public static class AttributeValues
		{
			public const string ButtonType = "button";
		}

		public static class Elements
		{
			public const string Button = "button";
			public const string ListItem = "li";
		}

		public static class Events
		{
			public const string Click = "onclick";
			public const string PointerCancel = "onpointercancel";
			public const string PointerDown = "onpointerdown";
			public const string PointerLeave = "onpointerleave";
			public const string PointerUp = "onpointerup";
		}

		public static class Roles
		{
			public const string Option = "option";
		}

		public static class Selectors
		{
			public static string Button => Elements.Button;

			public static string ButtonWithClass(string className) =>
				$"{Elements.Button}.{className}";

			public static string ElementWithRole(string elementName, string role) =>
				$"{elementName}[role='{role}']";
		}
	}
}
