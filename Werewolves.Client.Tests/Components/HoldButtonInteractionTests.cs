using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

#pragma warning disable BL0006

namespace Werewolves.Client.Tests.Components;

public class HoldButtonInteractionTests
{
	[Fact]
	public async Task CompletedConfirmationHold_ResetsWhenInstructionChanges()
	{
		using var fixture = new HoldButtonFixture();
		await fixture.RenderAsync();

		var initialButton = fixture.FindHoldButton();
		initialButton.TextContent.Should().Be(ClientStrings.SelectPlayers_SubmitButton);

		await fixture.CompleteHoldAsync(initialButton);

		var updatedZone = fixture.FindHoldZone();
		var updatedButton = fixture.FindHoldButton();
		updatedZone.ClassName.Should().NotContain("is-complete");
		updatedZone.ClassName.Should().NotContain("is-holding");
		updatedButton.TextContent.Should().Be(ClientStrings.SelectPlayers_SubmitButton);
	}

	[Fact]
	public async Task CompletedConfirmationHold_EmitsProductionLongPressPreset()
	{
		using var fixture = new HoldButtonFixture();
		await fixture.RenderAsync();

		await fixture.CompleteHoldAsync(fixture.FindHoldButton());

		fixture.Haptic.LongPresses.Should().Be(7);
		fixture.Haptic.Clicks.Should().Be(0);
	}

	[Fact]
	public async Task ReleaseBeforeRequiredDuration_CancelsPendingLongPressHaptics()
	{
		using var fixture = new HoldButtonFixture();
		await fixture.RenderAsync();

		var button = fixture.FindHoldButton();
		var holdTask = fixture.StartHoldAsync(button);

		await WaitForAsync(
			() => fixture.Haptic.LongPresses >= 1,
			ClientTestReferences.AssertionReasons.ZeroMillisecondLongPressPulseFiresImmediately);
		await fixture.ReleaseHoldAsync(button);
		await holdTask;
		await Task.Delay(450);

		fixture.Haptic.LongPresses.Should().Be(1);
		fixture.Haptic.Clicks.Should().Be(0);
	}

	[Fact]
	public async Task PointerCancelBeforeRequiredDuration_CancelsPendingLongPressHaptics()
	{
		using var fixture = new HoldButtonFixture();
		await fixture.RenderAsync();

		var button = fixture.FindHoldButton();
		var holdTask = fixture.StartHoldAsync(button);

		await WaitForAsync(
			() => fixture.Haptic.LongPresses >= 1,
			ClientTestReferences.AssertionReasons.ZeroMillisecondLongPressPulseFiresImmediately);
		await fixture.CancelHoldAsync(button);
		await holdTask;
		await Task.Delay(450);

		fixture.Haptic.LongPresses.Should().Be(1);
		fixture.Haptic.Clicks.Should().Be(0);
	}

	private static async Task WaitForAsync(Func<bool> condition, string because)
	{
		var deadline = DateTime.UtcNow.AddSeconds(1);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline)
			{
				condition().Should().BeTrue(because);
				return;
			}

			await Task.Delay(10);
		}
	}

	private sealed class HoldButtonFixture : IDisposable
	{
		private readonly ComponentTestRenderer _renderer;
		private readonly int _rootComponentId;
		private readonly ServiceProvider _serviceProvider;

		public HoldButtonFixture()
		{
			var services = new ServiceCollection();
			services.AddSingleton<IHapticFeedbackService>(Haptic);
			_serviceProvider = services.BuildServiceProvider();

			_renderer = new ComponentTestRenderer(_serviceProvider);
			_rootComponentId = _renderer.AttachRootComponent(new HoldButtonHost());
		}

		public RecordingHapticFeedbackService Haptic { get; } = new();

		public Task RenderAsync() =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.RenderRootAsync(_rootComponentId));

		public Task StartHoldAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchPointerDownAsync(button.PointerDownEventHandlerId));

		public Task CompleteHoldAsync(ButtonSnapshot button) =>
			StartHoldAsync(button);

		public Task ReleaseHoldAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchPointerUpAsync(button.PointerUpEventHandlerId));

		public Task CancelHoldAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchPointerCancelAsync(button.PointerCancelEventHandlerId));

		public ButtonSnapshot FindHoldButton() =>
			FindAllButtons().Single(button => button.ClassName.Contains("ww-btn-hold", StringComparison.Ordinal));

		public ElementSnapshot FindHoldZone() =>
			FindAllElements()
				.Single(element => element.ClassName.Contains("ww-hold-zone", StringComparison.Ordinal));

		private List<ButtonSnapshot> FindAllButtons() =>
			FindAllElements()
				.Where(element => element.ElementName == "button")
				.Select(element => new ButtonSnapshot(
					element.ClassName,
					element.TextContent,
					element.PointerDownEventHandlerId,
					element.PointerUpEventHandlerId,
					element.PointerCancelEventHandlerId))
				.ToList();

		private List<ElementSnapshot> FindAllElements()
		{
			var elements = new List<ElementSnapshot>();
			foreach (var componentId in EnumerateComponentIds(_rootComponentId))
			{
				var frames = _renderer.GetFrames(componentId);
				for (var index = 0; index < frames.Count; index++)
				{
					var frame = frames.Array[index];
					if (frame.FrameType != RenderTreeFrameType.Element)
					{
						continue;
					}

					elements.Add(CreateElementSnapshot(frames, index));
				}
			}

			return elements;
		}

		private IEnumerable<int> EnumerateComponentIds(int componentId)
		{
			yield return componentId;
			var frames = _renderer.GetFrames(componentId);
			for (var index = 0; index < frames.Count; index++)
			{
				var frame = frames.Array[index];
				if (frame.FrameType != RenderTreeFrameType.Component)
				{
					continue;
				}

				foreach (var child in EnumerateComponentIds(frame.ComponentId))
				{
					yield return child;
				}
			}
		}

		private static ElementSnapshot CreateElementSnapshot(ArrayRange<RenderTreeFrame> frames, int elementIndex)
		{
			var element = frames.Array[elementIndex];
			var attributes = new Dictionary<string, object?>();
			var text = new List<string>();
			var pointerDownHandlerId = 0UL;
			var pointerUpHandlerId = 0UL;
			var pointerCancelHandlerId = 0UL;
			var endIndex = elementIndex + element.ElementSubtreeLength;
			var collectingElementAttributes = true;

			for (var index = elementIndex + 1; index < endIndex; index++)
			{
				var frame = frames.Array[index];
				switch (frame.FrameType)
				{
					case RenderTreeFrameType.Attribute:
						if (collectingElementAttributes)
						{
							attributes[frame.AttributeName] = frame.AttributeValue;
							if (frame.AttributeName == "onpointerdown")
							{
								pointerDownHandlerId = frame.AttributeEventHandlerId;
							}
							if (frame.AttributeName == "onpointerup")
							{
								pointerUpHandlerId = frame.AttributeEventHandlerId;
							}
							if (frame.AttributeName == "onpointercancel")
							{
								pointerCancelHandlerId = frame.AttributeEventHandlerId;
							}
						}
						break;
					case RenderTreeFrameType.Text:
						collectingElementAttributes = false;
						text.Add(frame.TextContent);
						break;
					default:
						collectingElementAttributes = false;
						break;
				}
			}

			var className = attributes.TryGetValue("class", out var cls) && cls is string s ? s : "";
			return new ElementSnapshot(
				element.ElementName,
				className,
				string.Concat(text),
				pointerDownHandlerId,
				pointerUpHandlerId,
				pointerCancelHandlerId,
				attributes);
		}

		public void Dispose()
		{
			_renderer.Dispose();
			_serviceProvider.Dispose();
		}
	}

	private sealed class HoldButtonHost : ComponentBase
	{
		private ModeratorInstruction _instruction = new StartGameConfirmationInstruction(Guid.NewGuid());

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<InstructionRenderer>(0);
			builder.AddAttribute(1, nameof(InstructionRenderer.Instruction), _instruction);
			builder.AddAttribute(2, nameof(InstructionRenderer.Roster), Array.Empty<DashboardRosterEntry>());
			builder.AddAttribute(3, nameof(InstructionRenderer.OnResponse),
				EventCallback.Factory.Create<ModeratorResponse>(this, Complete));
			builder.CloseComponent();
		}

		private void Complete(ModeratorResponse _)
		{
			_instruction = new FinishedGameConfirmationInstruction(GameStrings.VictoryConditionAllWerewolvesEliminated);
		}
	}

	private sealed record ElementSnapshot(
		string ElementName,
		string ClassName,
		string TextContent,
		ulong PointerDownEventHandlerId,
		ulong PointerUpEventHandlerId,
		ulong PointerCancelEventHandlerId,
		IReadOnlyDictionary<string, object?> Attributes);

	private sealed record ButtonSnapshot(
		string ClassName,
		string TextContent,
		ulong PointerDownEventHandlerId,
		ulong PointerUpEventHandlerId,
		ulong PointerCancelEventHandlerId);

	private sealed class ComponentTestRenderer(IServiceProvider serviceProvider)
		: Renderer(serviceProvider, NullLoggerFactory.Instance)
	{
		public override Microsoft.AspNetCore.Components.Dispatcher Dispatcher { get; } =
			Microsoft.AspNetCore.Components.Dispatcher.CreateDefault();

		public int AttachRootComponent(IComponent component) => AssignRootComponentId(component);

		public Task RenderRootAsync(int componentId) => RenderRootComponentAsync(componentId);

		public ArrayRange<RenderTreeFrame> GetFrames(int componentId) =>
			GetCurrentRenderTreeFrames(componentId);

		public Task DispatchPointerDownAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new Microsoft.AspNetCore.Components.Web.PointerEventArgs());

		public Task DispatchPointerUpAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new Microsoft.AspNetCore.Components.Web.PointerEventArgs());

		public Task DispatchPointerCancelAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new Microsoft.AspNetCore.Components.Web.PointerEventArgs());

		protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

		protected override void HandleException(Exception exception)
		{
			throw new InvalidOperationException(
				ClientTestReferences.ExceptionMessages.ComponentRenderOrDispatchFailure("HoldButton"), exception);
		}
	}

	public sealed class RecordingHapticFeedbackService : IHapticFeedbackService
	{
		private int _clicks;
		private int _longPresses;

		public int Clicks => Volatile.Read(ref _clicks);
		public int LongPresses => Volatile.Read(ref _longPresses);

		public void Click() => Interlocked.Increment(ref _clicks);
		public void LongPress() => Interlocked.Increment(ref _longPresses);
	}
}
