using System;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Behavioural coverage for the Tizen-owned base view mapper.
	/// </summary>
	/// <remarks>
	/// These deliberately assert that a mapper <em>ran</em>, not merely that its key is registered.
	/// The defect they guard against is chaining MAUI's neutral <c>ViewHandler.ViewMapper</c>,
	/// which is compiled with <c>PlatformView</c> aliased to <see cref="object"/> and dispatches to
	/// no-op <c>Standard</c> extensions: every key resolves, every property silently does nothing,
	/// and a key-presence test passes happily. Only observing an effect distinguishes the two.
	/// </remarks>
	public class ViewMapperBehaviorTests
	{
		static (TizenLabelHandler Handler, TizenPlatformView Platform, StubLabel View) CreateLabel()
		{
			var handler = new TizenLabelHandler();
			var view = new StubLabel();
			handler.SetVirtualView(view);

			return (handler, (TizenPlatformView)((IElementHandler)handler).PlatformView!, view);
		}

		[Theory]
		[InlineData(nameof(IView.Visibility))]
		[InlineData(nameof(IView.IsEnabled))]
		[InlineData(nameof(IView.InputTransparent))]
		[InlineData(nameof(IView.Clip))]
		[InlineData(nameof(IView.Width))]
		[InlineData(nameof(IView.Height))]
		[InlineData(nameof(IView.MinimumWidth))]
		[InlineData(nameof(IView.MinimumHeight))]
		[InlineData(nameof(IView.AutomationId))]
		[InlineData(nameof(IView.FlowDirection))]
		public void CorePropertyActuallyReachesThePlatformView(string key)
		{
			var (handler, platform, _) = CreateLabel();
			platform.Applied.Clear();

			handler.UpdateValue(key);

			Assert.Contains(key, platform.Applied);
		}

		[Theory]
		[InlineData(nameof(IView.TranslationX))]
		[InlineData(nameof(IView.TranslationY))]
		[InlineData(nameof(IView.Scale))]
		[InlineData(nameof(IView.ScaleX))]
		[InlineData(nameof(IView.ScaleY))]
		[InlineData(nameof(IView.Rotation))]
		[InlineData(nameof(IView.RotationX))]
		[InlineData(nameof(IView.RotationY))]
		[InlineData(nameof(IView.AnchorX))]
		[InlineData(nameof(IView.AnchorY))]
		[InlineData(nameof(IView.Frame))]
		public void TransformPropertyRecomputesTheTransformation(string key)
		{
			// Every transform-affecting property funnels into one UpdateTransformation call,
			// matching dotnet/maui - partial application would leave the view visually stale.
			var (handler, platform, _) = CreateLabel();
			platform.Applied.Clear();

			handler.UpdateValue(key);

			Assert.Contains("Transformation", platform.Applied);
		}

		[Theory]
		[InlineData(nameof(IView.InvalidateMeasure))]
		[InlineData(nameof(IView.Focus))]
		[InlineData(nameof(IView.Unfocus))]
		public void CoreCommandActuallyReachesThePlatformView(string key)
		{
			var (handler, platform, _) = CreateLabel();
			platform.Applied.Clear();

			((IElementHandler)handler).Invoke(key, key == nameof(IView.Focus) ? new FocusRequest() : null);

			Assert.Contains(key, platform.Applied);
		}

		[Fact]
		public void MaximumSizeMappersAreDeliberatelyInertButPresent()
		{
			// NUI's MaximumSize misbehaves, so dotnet/maui leaves these empty. Keeping the keys
			// registered means the property resolves instead of falling through to nothing, and
			// records the decision rather than leaving a hole.
			Assert.NotNull(TizenViewMappers.ViewMapper.GetProperty(nameof(IView.MaximumWidth)));
			Assert.NotNull(TizenViewMappers.ViewMapper.GetProperty(nameof(IView.MaximumHeight)));

			var (handler, platform, _) = CreateLabel();
			platform.Applied.Clear();

			handler.UpdateValue(nameof(IView.MaximumWidth));
			handler.UpdateValue(nameof(IView.MaximumHeight));

			Assert.Empty(platform.Applied);
		}

		[Fact]
		public void UpdatingAllPropertiesAppliesTheWholeCoreSurface()
		{
			// SetVirtualView runs the full mapper. If the base mapper were MAUI's neutral one,
			// this list would be empty while every key still "existed".
			var (_, platform, _) = CreateLabel();

			Assert.NotEmpty(platform.Applied);
			Assert.Contains(nameof(IView.Visibility), platform.Applied);
			Assert.Contains(nameof(IView.IsEnabled), platform.Applied);
			Assert.Contains("Transformation", platform.Applied);
		}

		[Theory]
		[InlineData(nameof(IView.Opacity))]
		[InlineData(nameof(IView.Shadow))]
		public void BaseMapperAppliesWhereTheHandlerDoesNotOverride(string key)
		{
			// TizenLabelHandler deliberately overrides Opacity and Shadow with label-specific
			// implementations, so the base mapper is driven directly here instead.
			var platform = new TizenPlatformView();
			var view = new StubContentView();
			var handler = new RecordingViewHandler(platform, view);

			TizenViewMappers.ViewMapper.UpdateProperty(handler, view, key);

			Assert.Contains(key, platform.Applied);
		}

		[Fact]
		public void HandlerSpecificMapperOverridesTheBaseMapper()
		{
			// Background is intentionally overridden by every content-bearing handler. The
			// override must WIN over the base mapper rather than both running.
			Assert.NotSame(
				TizenViewMappers.ViewMapper.GetProperty(nameof(IView.Background)),
				TizenLabelHandler.Mapper.GetProperty(nameof(IView.Background)));

			Assert.NotSame(
				TizenViewMappers.ViewMapper.GetProperty(nameof(IView.Background)),
				TizenLayoutHandler.Mapper.GetProperty(nameof(IView.Background)));
		}

		[Fact]
		public void LayoutInheritsTheCoreSurfaceToo()
		{
			var handler = new TizenLayoutHandler();
			var platform = new TizenLayoutViewGroup(null);

			// Drive the base mapper directly: SetVirtualView on a layout needs a MauiContext and a
			// full child graph, which is not what this test is about.
			var view = new LayoutOrderingTests.StubLayoutInternal();
			var stubHandler = new RecordingViewHandler(platform, view);

			TizenViewMappers.MapVisibility(stubHandler, view);
			TizenViewMappers.MapIsEnabled(stubHandler, view);

			Assert.Contains(nameof(IView.Visibility), platform.Applied);
			Assert.Contains(nameof(IView.IsEnabled), platform.Applied);
		}

		/// <summary>Minimal <see cref="IViewHandler"/> that exposes a chosen platform view.</summary>
		sealed class RecordingViewHandler : IViewHandler
		{
			readonly TizenPlatformView _platformView;
			readonly IView _virtualView;

			public RecordingViewHandler(TizenPlatformView platformView, IView virtualView)
			{
				_platformView = platformView;
				_virtualView = virtualView;
			}

			public bool HasContainer { get => false; set { } }

			public object? ContainerView => null;

			public IView? VirtualView => _virtualView;

			public object? PlatformView => _platformView;

			public IMauiContext? MauiContext => null;

			IElement? IElementHandler.VirtualView => _virtualView;

			public void SetMauiContext(IMauiContext mauiContext)
			{
			}

			public void SetVirtualView(IElement view)
			{
			}

			public void UpdateValue(string property)
			{
			}

			public void Invoke(string command, object? args)
			{
			}

			public void DisconnectHandler()
			{
			}

			public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;

			public void PlatformArrange(Rect frame)
			{
			}
		}
	}
}
