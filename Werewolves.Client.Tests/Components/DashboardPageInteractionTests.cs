using FluentAssertions;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;
using CssClasses = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css.Classes;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

#pragma warning disable BL0006

namespace Werewolves.Client.Tests.Components;

public class DashboardPageInteractionTests
{
	private static string ActiveDashboardTabClasses => $"{CssClasses.DashboardTab} {CssClasses.DashboardTabActive}";

	[Fact]
	public async Task InitialRender_ShowsHoldConfirmButtonForStartGameInstruction()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var confirmButton = fixture.FindButtonByText(ClientStrings.SelectPlayers_SubmitButton);
		confirmButton.Should().NotBeNull(ClientTestReferences.AssertionReasons.StartGameConfirmationShownDirectly);
		confirmButton!.ClickEventHandlerId.Should().Be(0, ClientTestReferences.AssertionReasons.ConfirmationDoesNotSubmitFromInstantClick);
		confirmButton.HasPointerHandlers.Should().BeTrue(ClientTestReferences.AssertionReasons.ConfirmationUsesPressAndHoldGate);
	}

	[Fact]
	public async Task HoldConfirmButton_AdvancesGameToNextInstruction()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var instructionBefore = fixture.Game.CurrentInstruction;
		instructionBefore.Should().BeOfType<StartGameConfirmationInstruction>();

		var confirmButton = fixture.FindButtonByText(ClientStrings.SelectPlayers_SubmitButton);
		await fixture.CompleteHoldAsync(confirmButton!);

		fixture.Game.CurrentInstruction.Should().NotBeSameAs(instructionBefore,
			ClientTestReferences.AssertionReasons.HoldingConfirmAdvancesInstruction);
	}

	[Fact]
	public async Task HoldConfirmButton_WhenHapticFeedbackFails_KeepsDashboardInteractive()
	{
		using var fixture = new InteractionFixture(new ThrowingHaptic());
		await fixture.RenderAsync();

		var instructionBefore = fixture.Game.CurrentInstruction;
		var confirmButton = fixture.FindButtonByText(ClientStrings.SelectPlayers_SubmitButton);

		await fixture.CompleteHoldAsync(confirmButton!);

		fixture.Game.CurrentInstruction.Should().NotBeSameAs(instructionBefore,
			ClientTestReferences.AssertionReasons.HapticFailureDoesNotBlockProgression);

		var audioToggle = fixture.FindButtonByClass(CssClasses.AudioToggle);
		await fixture.ClickAsync(audioToggle!);

		fixture.Game.IsAudioMuted.Should().BeTrue(
			ClientTestReferences.AssertionReasons.DashboardEventsRemainDispatchableAfterGameAction);

		var rosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		await fixture.ClickAsync(rosterTab!);

		var updatedRosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		updatedRosterTab!.Attributes.Should().Contain(Html.Attributes.Class, ActiveDashboardTabClasses);
	}

	[Fact]
	public async Task MultipleGameSteps_AllRenderWithoutException()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var steps = new List<string>();
		for (var step = 0; step < 20; step++)
		{
			var instruction = fixture.Game.CurrentInstruction;
			if (instruction is null) break;

			var typeName = instruction.GetType().Name;
			steps.Add(ClientTestReferences.FixtureLabels.RenderedInstructionStep(step, typeName));

			var buttons = fixture.FindAllButtons();
			var buttonLabels = string.Join(", ",
				buttons.Select(b => $"'{b.TextContent}' ({b.ClassName})"));

			buttons.Should().NotBeEmpty(
				ClientTestReferences.AssertionReasons.StepRendersButtons(step, typeName, buttonLabels));

			if (instruction is ConfirmationInstruction)
			{
				var confirm = fixture.FindButtonByText(ClientStrings.SelectPlayers_SubmitButton)
					?? fixture.FindButtonByClass(CssClasses.HoldButton);

				if (confirm is not null)
				{
					await fixture.CompleteHoldAsync(confirm);
				}
				else
				{
					break;
				}
			}
			else
			{
				break;
			}
		}

		steps.Should().HaveCountGreaterThan(1,
			ClientTestReferences.AssertionReasons.GameAdvancedThroughMultipleInstructions);
	}

	[Fact]
	public async Task ClickTabButton_SwitchesActiveTab()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var rosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		rosterTab.Should().NotBeNull(ClientTestReferences.AssertionReasons.RosterTabRendered);
		rosterTab!.ClickEventHandlerId.Should().NotBe(0, ClientTestReferences.AssertionReasons.RosterTabHasClickHandler);

		await fixture.ClickAsync(rosterTab);

		var updatedRosterTab = fixture.FindButtonByText(ClientStrings.Dashboard_TabRoster);
		updatedRosterTab!.Attributes.Should().Contain(Html.Attributes.Class, ActiveDashboardTabClasses);
	}

	[Fact]
	public async Task AllVisibleButtons_HaveRegisteredClickHandlers()
	{
		using var fixture = new InteractionFixture();
		await fixture.RenderAsync();

		var buttons = fixture.FindAllButtons();
		buttons.Should().NotBeEmpty(ClientTestReferences.AssertionReasons.DashboardRendersButtons);

		foreach (var button in buttons)
		{
			var hasAnyHandler = button.ClickEventHandlerId != 0
				|| button.HasPointerHandlers
				|| button.IsDisabled;

			hasAnyHandler.Should().BeTrue(
				ClientTestReferences.AssertionReasons.ButtonHasHandlersOrIsDisabled(button.TextContent, button.ClassName));
		}
	}

	private sealed class InteractionFixture : IDisposable
	{
		private readonly InteractionTestRenderer _renderer;
		private readonly int _rootComponentId;
		private readonly ServiceProvider _serviceProvider;

		public InteractionFixture(IHapticFeedbackService? hapticFeedback = null)
		{
			Game = new GameClientManager(
				new GameService(),
				new NoOpAudioPlayback(),
				new InMemoryStore());
			Game.StartGame(
				PlayerNames.DefaultFive,
				[
					MainRoleType.SimpleWerewolf,
					MainRoleType.Seer,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				]);

			var services = new ServiceCollection();
			services.AddSingleton(Game);
			services.AddSingleton(hapticFeedback ?? new NoOpHaptic());
			_serviceProvider = services.BuildServiceProvider();

			_renderer = new InteractionTestRenderer(_serviceProvider);
			_rootComponentId = _renderer.AttachRootComponent(typeof(DashboardPage));
		}

		public GameClientManager Game { get; }

		public Task RenderAsync() =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.RenderRootAsync(_rootComponentId));

		public Task ClickAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchClickAsync(button.ClickEventHandlerId));

		public Task CompleteHoldAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchPointerDownAsync(button.PointerDownEventHandlerId));

		public ButtonSnapshot? FindButtonByText(string text) =>
			FindAllButtons().FirstOrDefault(b =>
				b.TextContent.Equals(text, StringComparison.OrdinalIgnoreCase));

		public ButtonSnapshot? FindButtonByClass(string className) =>
			FindAllButtons().FirstOrDefault(b =>
				b.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className));

		public List<ButtonSnapshot> FindAllButtons()
		{
			var buttons = new List<ButtonSnapshot>();
			foreach (var componentId in EnumerateComponentIds(_rootComponentId))
			{
				var frames = _renderer.GetFrames(componentId);
				for (var index = 0; index < frames.Count; index++)
				{
					var frame = frames.Array[index];
					if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != Html.Elements.Button)
						continue;

					buttons.Add(CreateButtonSnapshot(frames, index));
				}
			}
			return buttons;
		}

		public void Dispose()
		{
			_renderer.Dispose();
			_serviceProvider.Dispose();
		}

		private IEnumerable<int> EnumerateComponentIds(int componentId)
		{
			yield return componentId;
			var frames = _renderer.GetFrames(componentId);
			for (var index = 0; index < frames.Count; index++)
			{
				var frame = frames.Array[index];
				if (frame.FrameType != RenderTreeFrameType.Component)
					continue;
				foreach (var child in EnumerateComponentIds(frame.ComponentId))
					yield return child;
			}
		}

		private static ButtonSnapshot CreateButtonSnapshot(ArrayRange<RenderTreeFrame> frames, int elementIndex)
		{
			var element = frames.Array[elementIndex];
			var attributes = new Dictionary<string, object?>();
			var text = new List<string>();
			var clickHandlerId = 0UL;
			var pointerDownHandlerId = 0UL;
			var hasPointerHandlers = false;
			var isDisabled = false;
			var endIndex = elementIndex + element.ElementSubtreeLength;
			var collectingButtonAttributes = true;

			for (var index = elementIndex + 1; index < endIndex; index++)
			{
				var frame = frames.Array[index];
				switch (frame.FrameType)
				{
					case RenderTreeFrameType.Attribute:
						if (collectingButtonAttributes)
						{
							attributes[frame.AttributeName] = frame.AttributeValue;
							if (frame.AttributeName == Html.Events.Click)
								clickHandlerId = frame.AttributeEventHandlerId;
							if (frame.AttributeName == Html.Events.PointerDown)
								pointerDownHandlerId = frame.AttributeEventHandlerId;
							if (frame.AttributeName is Html.Events.PointerDown or Html.Events.PointerUp)
								hasPointerHandlers = true;
							if (frame.AttributeName == Html.Attributes.Disabled && frame.AttributeValue is true)
								isDisabled = true;
						}
						break;
					case RenderTreeFrameType.Text:
						collectingButtonAttributes = false;
						text.Add(frame.TextContent);
						break;
					default:
						collectingButtonAttributes = false;
						break;
				}
			}

			var className = attributes.TryGetValue(Html.Attributes.Class, out var cls) && cls is string s ? s : "";
			return new ButtonSnapshot(
				className,
				string.Concat(text),
				clickHandlerId,
				pointerDownHandlerId,
				hasPointerHandlers,
				isDisabled,
				attributes);
		}
	}

	internal sealed record ButtonSnapshot(
		string ClassName,
		string TextContent,
		ulong ClickEventHandlerId,
		ulong PointerDownEventHandlerId,
		bool HasPointerHandlers,
		bool IsDisabled,
		IReadOnlyDictionary<string, object?> Attributes);

	private sealed class InteractionTestRenderer(IServiceProvider serviceProvider)
		: Renderer(serviceProvider, NullLoggerFactory.Instance)
	{
		public override Microsoft.AspNetCore.Components.Dispatcher Dispatcher { get; } =
			Microsoft.AspNetCore.Components.Dispatcher.CreateDefault();

		public int AttachRootComponent(Type componentType)
		{
			var component = InstantiateComponent(componentType);
			return AssignRootComponentId(component);
		}

		public Task RenderRootAsync(int componentId) => RenderRootComponentAsync(componentId);

		public ArrayRange<RenderTreeFrame> GetFrames(int componentId) =>
			GetCurrentRenderTreeFrames(componentId);

		public Task DispatchClickAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new MouseEventArgs());

		public Task DispatchPointerDownAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new Microsoft.AspNetCore.Components.Web.PointerEventArgs());

		protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

		protected override void HandleException(Exception exception)
		{
			throw new InvalidOperationException(
				ClientTestReferences.ExceptionMessages.ComponentRenderOrDispatchFailure("dashboard"), exception);
		}
	}

	private sealed class NoOpHaptic : IHapticFeedbackService
	{
		public void Click() { }
		public void LongPress() { }
	}

	private sealed class ThrowingHaptic : IHapticFeedbackService
	{
		public void Click() => throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.SyntheticHapticFailure);
		public void LongPress() => throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.SyntheticHapticFailure);
	}

	private sealed class NoOpAudioPlayback : IInstructionAudioPlayback
	{
		public bool IsMuted { get; private set; }
		public Task ReconcileAsync(ModeratorInstruction? instruction, CancellationToken ct = default) => Task.CompletedTask;
		public Task SetMutedAsync(bool muted, ModeratorInstruction? instruction, CancellationToken ct = default)
		{
			IsMuted = muted;
			return Task.CompletedTask;
		}
	}

	private sealed class InMemoryStore : IGameSessionSaveStore
	{
		private string? _data;
		public string? Load() => _data;
		public void Save(string data) => _data = data;
		public void Clear() => _data = null;
	}
}
