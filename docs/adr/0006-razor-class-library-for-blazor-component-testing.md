# Razor Class Library for Blazor component testing

The shared Moderator Blazor UI lives in `Werewolves.Client.Shared`, a Razor Class Library (RCL) targeting `net10.0`. The MAUI app (`Werewolves.UI.MobileClient`) references the RCL and renders the shared `Routes` root component through `BlazorWebView`. `Werewolves.Client.Tests` also references the RCL directly, giving bUnit direct access to shared components without depending on platform-specific MAUI targets.

The MAUI project targets `net10.0-android;net10.0-ios;net10.0-maccatalyst`. Test projects cannot reference it directly because the platform-specific TFMs are incompatible with a standard `net10.0` test host. The RCL eliminates the TFM mismatch: bUnit renders the shared components in a headless test context with no MAUI workload required, and CI runs them identically to Core.Tests.

The boundary is host-agnostic. Shared pages, game input views, hold-confirmation helpers, `GameClientManager`, lobby/dashboard projection services, client resources, and shared CSS live in the RCL. Native behavior remains behind contracts: audio playback, asset loading, haptics, wake lock, and session persistence are registered by each host. The mobile host provides the MAUI implementations, including `Plugin.Maui.Audio`, app-package audio loading, `DeviceDisplay`, haptic feedback, and file-backed session storage. Tests provide fake or no-op implementations through `ModeratorComponentTestContext`.

Every host that renders the RCL must register the shared UI contract dependencies before mounting `Routes`: `GameClientManager`, `LobbySetupState`, `GameplayWakeLockController`, `IInstructionAudioPlayback`, `IHapticFeedbackService`, `IScreenWakeLock`, and `IGameSessionSaveStore`. Native hosts bind those contracts to platform adapters; test and browser QA hosts can use fake, no-op, or in-memory implementations.

`Werewolves.Client.BrowserQaHost` is the local browser QA consumer of this boundary. It references the RCL directly, mounts `Routes`, and seeds shared services/Core public APIs for local lobby, dashboard, instruction, and victory inspection without becoming a public deployment target or second maintained gameplay UI.

This keeps the mobile app as the native shell and composition root while giving tests and local QA hosts one shared Moderator UI surface to consume.

## Considered options

- **File linking (`<Compile Include>`)**: extend the current pattern to link `.razor` files into the test project. Rejected because Razor components have build-time dependencies (generated code-behind, CSS isolation, `_Imports.razor` scope) that don't transfer correctly via file linking, and the approach becomes increasingly fragile as the component count grows.
- **Test the MAUI project directly**: reference `Werewolves.UI.MobileClient.csproj` from the bUnit test project. Rejected because the MAUI project multi-targets platform-specific TFMs, and bUnit's test host targets `net10.0`. MSBuild cannot resolve the reference without a compatible TFM, and adding `net10.0` to the MAUI project's target list introduces build complexity and conditional compilation throughout the app.
