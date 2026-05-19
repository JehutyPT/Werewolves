namespace Werewolves.Client.Tests.Helpers;

public static partial class ClientTestReferences
{
	public static class Paths
	{
		private const string SolutionFileName = "Werewolves.sln";
		private const string ClientSharedProjectDirectory = "Werewolves.Client.Shared";
		private const string ClientProjectDirectory = "Werewolves.Client";

		public static string RepositoryRoot
		{
			get
			{
				var directory = new DirectoryInfo(AppContext.BaseDirectory);

				while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
				{
					directory = directory.Parent;
				}

				return directory?.FullName
					?? throw new InvalidOperationException(ExceptionMessages.RepositoryRootNotFound);
			}
		}

		public static string RepositoryPath(params string[] relativeSegments)
		{
			var segments = new string[relativeSegments.Length + 1];
			segments[0] = RepositoryRoot;
			Array.Copy(relativeSegments, 0, segments, 1, relativeSegments.Length);

			return Path.Combine(segments);
		}

		public static string ClientPath(params string[] relativeSegments) =>
			RepositoryPathWithProject(ClientProjectDirectory, relativeSegments);

		public static string SharedPath(params string[] relativeSegments) =>
			RepositoryPathWithProject(ClientSharedProjectDirectory, relativeSegments);

		private static string RepositoryPathWithProject(string projectDirectory, string[] relativeSegments)
		{
			var segments = new string[relativeSegments.Length + 2];
			segments[0] = RepositoryRoot;
			segments[1] = projectDirectory;
			Array.Copy(relativeSegments, 0, segments, 2, relativeSegments.Length);

			return Path.Combine(segments);
		}
	}
}
