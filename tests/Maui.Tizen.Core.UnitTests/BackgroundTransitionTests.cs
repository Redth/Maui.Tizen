using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the two different null-background behaviours apart.
	/// </summary>
	/// <remarks>
	/// Conflating them causes a visible bug either way, and the two bugs look nothing alike:
	/// <list type="bullet">
	/// <item><description>
	/// Never clearing on null leaves a stale colour on screen after a view's background is reset.
	/// </description></item>
	/// <item><description>
	/// Always clearing on null repaints every page transparent at launch, because a page is created
	/// opaque white and then has the background mapper run over it.
	/// </description></item>
	/// </list>
	/// </remarks>
	public class BackgroundTransitionTests
	{
		[Fact]
		public void OrdinaryViewPassesClearWhenNullTrue()
		{
			// Asserts the DECISION, not merely that a mapper ran. The previous version of this test
			// only checked that the Background key entered a mapper, which stayed true even when
			// clearWhenNull was flipped to false - i.e. it could not fail for the regression it was
			// named after. Verified by mutation.
			var platform = new TizenPlatformView();
			var view = new StubContentView { Background = new SolidPaint(Colors.Red) };
			var handler = new RecordingHandler(platform, view);

			TizenViewMappers.ViewMapper.UpdateProperty(handler, view, nameof(IView.Background));

			Assert.Contains("Background:clearWhenNull=True", platform.Applied);
		}

		[Fact]
		public void OrdinaryViewStillClearsAfterTransitionToNull()
		{
			// The transition itself: colour -> null must still reach the mapper with clearing on,
			// otherwise the stale colour is never removed from the native view.
			var platform = new TizenPlatformView();
			var view = new StubContentView { Background = new SolidPaint(Colors.Red) };
			var handler = new RecordingHandler(platform, view);

			TizenViewMappers.ViewMapper.UpdateProperty(handler, view, nameof(IView.Background));
			platform.Applied.Clear();

			view.Background = null;
			TizenViewMappers.ViewMapper.UpdateProperty(handler, view, nameof(IView.Background));

			Assert.Contains("Background:clearWhenNull=True", platform.Applied);
		}

		[Fact]
		public void PagePassesClearWhenNullFalse()
		{
			// The opposite decision, and the reason the two must not be merged: a page is created
			// opaque white and then has this mapper run over it, so clearing on null would repaint
			// every page transparent at launch.
			// Driven through the page mapper directly: SetVirtualView on a page handler needs a
			// MauiContext and a realised content graph, which is not what this test is about.
			var platform = new TizenPlatformView();
			var page = new StubContentView();
			var handler = new RecordingPageHandler(platform, page);

			TizenPageHandler.PageMapper.UpdateProperty(handler, page, nameof(IContentView.Background));

			Assert.Contains("Background:clearWhenNull=False", platform.Applied);
			Assert.DoesNotContain("Background:clearWhenNull=True", platform.Applied);
		}

		[Fact]
		public void PageBackgroundMapperIsDistinctFromTheOrdinaryOne()
		{
			// If a refactor ever collapsed the two, pages would go transparent at launch again.
			Assert.NotSame(
				TizenViewMappers.ViewMapper.GetProperty(nameof(IView.Background)),
				TizenPageHandler.PageMapper.GetProperty(nameof(IContentView.Background)));
		}

		/// <summary>A page handler whose platform view is observable on the host.</summary>
		sealed class RecordingPageHandler : IPageHandler
		{
			readonly TizenPlatformView _platformView;
			readonly IContentView _virtualView;

			public RecordingPageHandler(TizenPlatformView platformView, IContentView virtualView)
			{
				_platformView = platformView;
				_virtualView = virtualView;
			}

			IContentView IContentViewHandler.VirtualView => _virtualView;

			object IContentViewHandler.PlatformView => _platformView;

			public bool HasContainer { get => false; set { } }

			public object? ContainerView => null;

			IView? IViewHandler.VirtualView => _virtualView;

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

		sealed class RecordingHandler : IViewHandler
		{
			readonly TizenPlatformView _platformView;
			readonly IView _virtualView;

			public RecordingHandler(TizenPlatformView platformView, IView virtualView)
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
