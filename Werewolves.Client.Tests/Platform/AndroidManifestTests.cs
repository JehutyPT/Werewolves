using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Werewolves.Client.Tests.Platform;

public class AndroidManifestTests
{
    [Fact]
    public void AndroidManifest_DeclaresVibratePermissionForHapticFeedback()
    {
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var manifest = XDocument.Load(AndroidManifestPath());

        var permissions = manifest.Root!
            .Elements("uses-permission")
            .Select(element => (string?)element.Attribute(android + "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name));

        permissions.Should().Contain("android.permission.VIBRATE",
            "MAUI haptic feedback requires the Android VIBRATE permission to produce physical feedback");
    }

    private static string AndroidManifestPath()
    {
        return Path.Combine(RepositoryRoot, "Werewolves.Client", "Platforms", "Android", "AndroidManifest.xml");
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Werewolves.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
        }
    }
}
