namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class PlayerNames
	{
		public const string Alice = "Alice";
		public const string Ana = "Ana";
		public const string Bob = "Bob";
		public const string Bruno = "Bruno";
		public const string Carla = "Carla";
		public const string Catarina = "Catarina";
		public const string Diana = "Diana";
		public const string Eduardo = "Eduardo";
		public const string Eva = "Eva";
		public const string Filipe = "Filipe";
		public const string Lobo = "Lobo";
		public const string GeneratedPlayerPrefix = "Player";

		public static string[] AssignRolesPair => [Alice, Bob];
		public static string[] AssignRolesSingle => [Alice];
		public static string AnaLowercase => Ana.ToLowerInvariant();
		public static string AnaUppercase => Ana.ToUpperInvariant();
		public static string[] DefaultTwo => [Ana, Bruno];
		public static string[] DefaultThree => [Ana, Bruno, Catarina];
		public static string[] DefaultFive => [Ana, Bruno, Catarina, Diana, Eduardo];
		public static string GeneratedPlayer(int index) => $"{GeneratedPlayerPrefix}{index}";
	}
}
