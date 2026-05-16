using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Models;
using Xunit;

#pragma warning disable BL0006

namespace Werewolves.Client.Tests.Components;

public class InstructionRendererCollapsibleTests
{
	[Fact]
	public async Task TwoPartInstruction_RendersPublicAndPrivateInstructionBlocks()
	{
		using var fixture = new InstructionRendererFixture(
			new TestInstruction(
				publicAnnouncement: "The village sleeps.\nEveryone closes their eyes.",
				privateInstruction: "Check that nobody is peeking.\nThen show the victim privately."));

		await fixture.RenderAsync();

		var announce = fixture.FindButtonByAriaLabel(ClientStrings.Dashboard_AnnounceLabel);
		var moderator = fixture.FindButtonByAriaLabel(ClientStrings.Dashboard_ModeratorLabel);

		announce.Should().NotBeNull();
		moderator.Should().NotBeNull();
		announce!.ClassName.Should().Contain("ww-instruction-block--announcement").And.Contain("is-expanded");
		moderator!.ClassName.Should().Contain("ww-instruction-block--private").And.NotContain("is-expanded");
		announce.GetAttribute<string>("aria-expanded").Should().Be("true");
		moderator.GetAttribute<string>("aria-expanded").Should().Be("false");
		fixture.VisibleText.Should().Contain("The village sleeps.");
		fixture.VisibleText.Should().Contain("Everyone closes their eyes.");
		fixture.VisibleText.Should().Contain("Check that nobody is peeking. ...");
		fixture.VisibleText.Should().NotContain("Then show the victim privately.");
		fixture.VisibleText.Should().Contain(ClientStrings.Common_TapToExpand);
		fixture.VisibleText.Should().NotContain("Mostrar instrução");
	}

	[Fact]
	public async Task ClickingCollapsedInstructionBlock_ExpandsItAndCollapsesTheOtherBlock()
	{
		using var fixture = new InstructionRendererFixture(
			new TestInstruction(
				publicAnnouncement: "The village sleeps.",
				privateInstruction: "Check that nobody is peeking."));

		await fixture.RenderAsync();

		await fixture.ClickAsync(fixture.FindButtonByAriaLabel(ClientStrings.Dashboard_ModeratorLabel)!);

		var announce = fixture.FindButtonByAriaLabel(ClientStrings.Dashboard_AnnounceLabel)!;
		var moderator = fixture.FindButtonByAriaLabel(ClientStrings.Dashboard_ModeratorLabel)!;

		announce.GetAttribute<string>("aria-expanded").Should().Be("false");
		moderator.GetAttribute<string>("aria-expanded").Should().Be("true");
		announce.ClassName.Should().NotContain("is-expanded");
		moderator.ClassName.Should().Contain("is-expanded");
		fixture.VisibleText.Should().Contain("Check that nobody is peeking.");
		fixture.Haptic.ClickCount.Should().Be(1);
	}

	private sealed record TestInstruction : ModeratorInstruction
	{
		public TestInstruction(string? publicAnnouncement = null, string? privateInstruction = null)
			: base(publicAnnouncement: publicAnnouncement, privateInstruction: privateInstruction)
		{
		}
	}

	private sealed class InstructionRendererFixture : IDisposable
	{
		private readonly ComponentTestRenderer _renderer;
		private readonly int _rootComponentId;
		private readonly ServiceProvider _serviceProvider;

		public InstructionRendererFixture(ModeratorInstruction instruction)
		{
			Haptic = new RecordingHapticFeedbackService();
			_serviceProvider = new ServiceCollection()
				.AddSingleton<IHapticFeedbackService>(Haptic)
				.BuildServiceProvider();
			_renderer = new ComponentTestRenderer(_serviceProvider);
			_rootComponentId = _renderer.AttachRootComponent(new InstructionRendererHost { Instruction = instruction });
		}

		public RecordingHapticFeedbackService Haptic { get; }

		public Task RenderAsync() =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.RenderRootAsync(_rootComponentId));

		public Task ClickAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchClickAsync(button.ClickEventHandlerId));

		public ButtonSnapshot? FindButtonByAriaLabel(string label) =>
			FindAllButtons().SingleOrDefault(button =>
				button.Attributes.TryGetValue("aria-label", out var value) &&
				value as string == label);

		public string VisibleText => string.Concat(VisibleTextItems);

		private List<string> VisibleTextItems =>
			EnumerateComponentIds(_rootComponentId)
				.SelectMany(componentId => _renderer.GetFrames(componentId).Array
					.Take(_renderer.GetFrames(componentId).Count)
					.Where(frame => frame.FrameType == RenderTreeFrameType.Text)
					.Select(frame => frame.TextContent))
				.ToList();

		private List<ButtonSnapshot> FindAllButtons()
		{
			var buttons = new List<ButtonSnapshot>();
			foreach (var componentId in EnumerateComponentIds(_rootComponentId))
			{
				var frames = _renderer.GetFrames(componentId);
				for (var index = 0; index < frames.Count; index++)
				{
					var frame = frames.Array[index];
					if (frame.FrameType == RenderTreeFrameType.Element && frame.ElementName == "button")
					{
						buttons.Add(CreateButtonSnapshot(frames, index));
					}
				}
			}

			return buttons;
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

		private static ButtonSnapshot CreateButtonSnapshot(ArrayRange<RenderTreeFrame> frames, int elementIndex)
		{
			var element = frames.Array[elementIndex];
			var attributes = new Dictionary<string, object?>();
			var text = new List<string>();
			var clickHandlerId = 0UL;
			var endIndex = elementIndex + element.ElementSubtreeLength;
			var collectingButtonAttributes = true;

			for (var index = elementIndex + 1; index < endIndex; index++)
			{
				var frame = frames.Array[index];
				if (frame.FrameType == RenderTreeFrameType.Attribute)
				{
					if (collectingButtonAttributes)
					{
						attributes[frame.AttributeName] = frame.AttributeValue;
						if (frame.AttributeName == "onclick")
						{
							clickHandlerId = frame.AttributeEventHandlerId;
						}
					}
				}
				else if (frame.FrameType == RenderTreeFrameType.Text)
				{
					collectingButtonAttributes = false;
					text.Add(frame.TextContent);
				}
				else
				{
					collectingButtonAttributes = false;
				}
			}

			var className = attributes.TryGetValue("class", out var cls) && cls is string s ? s : "";
			return new ButtonSnapshot(className, string.Concat(text), clickHandlerId, attributes);
		}

		public void Dispose()
		{
			_renderer.Dispose();
			_serviceProvider.Dispose();
		}
	}

	private sealed class InstructionRendererHost : ComponentBase
	{
		public ModeratorInstruction Instruction { get; init; } = default!;

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<InstructionRenderer>(0);
			builder.AddAttribute(1, nameof(InstructionRenderer.Instruction), Instruction);
			builder.AddAttribute(2, nameof(InstructionRenderer.OnResponse),
				EventCallback.Factory.Create<ModeratorResponse>(this, _ => { }));
			builder.CloseComponent();
		}
	}

	private sealed record ButtonSnapshot(
		string ClassName,
		string TextContent,
		ulong ClickEventHandlerId,
		IReadOnlyDictionary<string, object?> Attributes)
	{
		public T? GetAttribute<T>(string name) =>
			Attributes.TryGetValue(name, out var value) && value is T typed ? typed : default;
	}

	private sealed class ComponentTestRenderer(IServiceProvider serviceProvider)
		: Renderer(serviceProvider, NullLoggerFactory.Instance)
	{
		public override Microsoft.AspNetCore.Components.Dispatcher Dispatcher { get; } =
			Microsoft.AspNetCore.Components.Dispatcher.CreateDefault();

		public int AttachRootComponent(IComponent component) => AssignRootComponentId(component);

		public Task RenderRootAsync(int componentId) => RenderRootComponentAsync(componentId);

		public ArrayRange<RenderTreeFrame> GetFrames(int componentId) =>
			GetCurrentRenderTreeFrames(componentId);

		public Task DispatchClickAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new MouseEventArgs());

		protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

		protected override void HandleException(Exception exception)
		{
			throw new InvalidOperationException(
				"Unhandled exception during InstructionRenderer rendering or event dispatch.", exception);
		}
	}

	public sealed class RecordingHapticFeedbackService : IHapticFeedbackService
	{
		public int ClickCount { get; private set; }
		public int LongPressCount { get; private set; }

		public void Click()
		{
			ClickCount++;
		}

		public void LongPress()
		{
			LongPressCount++;
		}
	}
}
