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
		public void OrdinaryViewClearsOnTransitionToNull()
		{
			// The mapper must be reached for BOTH the colour and the null, otherwise the reset is
			// silently dropped and the old colour persists.
			var platform = new TizenPlatformView();
			var view = new StubContentView { Background = new SolidPaint(Colors.Red) };
			var handler = new RecordingHandler(platform, view);

			TizenViewMappers.ViewMapper.UpdateProperty(handler, view, nameof(IView.Background));
			Assert.Contains(nameof(IView.Background), platform.Applied);

			platform.Applied.Clear();
			view.Background = null;
			TizenViewMappers.ViewMapper.UpdateProperty(handler, view, nameof(IView.Background));

			Assert.Contains(nameof(IView.Background), platform.Applied);
		}

		[Fact]
		public void PageBackgroundMapperIsDistinctFromTheOrdinaryOne()
		{
			// The page override is what carries clearWhenNull:false. If a refactor ever collapsed
			// the two, pages would go transparent at launch again.
			Assert.NotSame(
				TizenViewMappers.ViewMapper.GetProperty(nameof(IView.Background)),
				TizenPageHandler.PageMapper.GetProperty(nameof(IContentView.Background)));
		}

		[Fact]
		public void PageStillMapsBackgroundWhenNull()
		{
			// Reaching the mapper is required; what it does with a null is the page-specific part
			// (keep the opaque white default) and is asserted by the ref-pack compile of that path.
			var handler = new TizenPageHandler();
			Assert.NotNull(TizenPageHandler.PageMapper.GetProperty(nameof(IContentView.Background)));
			Assert.NotNull(handler);
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
