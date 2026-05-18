using System.Xml.Linq;
using FluentAssertions;
using Werewolves.Client.Tests.Helpers;
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

        permissions.Should().Contain(
            "android.permission.VIBRATE",
            ClientTestReferences.AssertionReasons.AndroidVibratePermissionSupportsHaptics);
    }

    private static string AndroidManifestPath() =>
        ClientTestReferences.Paths.ClientPath("Platforms", "Android", "AndroidManifest.xml");
}
