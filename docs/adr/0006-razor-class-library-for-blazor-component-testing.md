# Razor Class Library for Blazor component testing

When bUnit is adopted for Blazor component testing, Blazor components will be extracted from the MAUI project (`Werewolves.UI.MobileClient`) into a Razor Class Library (RCL) targeting `net10.0`. The MAUI app references the RCL for rendering. A bUnit test project also references the RCL, giving it direct access to components without depending on platform-specific MAUI targets.

The MAUI project targets `net10.0-android;net10.0-ios;net10.0-maccatalyst`. Test projects cannot reference it directly because the platform-specific TFMs are incompatible with a standard `net10.0` test host. The current client test project works around this by linking individual source files via `<Compile Include>`, which is viable for services but breaks down for Razor components that have cross-dependencies (partials, code-behind, CSS isolation, `_Imports.razor` chains). An RCL targeting `net10.0` eliminates the TFM mismatch entirely: bUnit renders the components in a headless test context with no MAUI workload required, and CI runs them identically to Core.Tests.

## Considered options

- **File linking (`<Compile Include>`)**: extend the current pattern to link `.razor` files into the test project. Rejected because Razor components have build-time dependencies (generated code-behind, CSS isolation, `_Imports.razor` scope) that don't transfer correctly via file linking, and the approach becomes increasingly fragile as the component count grows.
- **Test the MAUI project directly**: reference `Werewolves.UI.MobileClient.csproj` from the bUnit test project. Rejected because the MAUI project multi-targets platform-specific TFMs, and bUnit's test host targets `net10.0`. MSBuild cannot resolve the reference without a compatible TFM, and adding `net10.0` to the MAUI project's target list introduces build complexity and conditional compilation throughout the app.
