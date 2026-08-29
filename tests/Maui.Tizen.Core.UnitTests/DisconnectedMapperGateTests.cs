// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(StaticMapperCollection.Name)]
	public class DisconnectedMapperGateTests
	{
		[Fact]
		public void PropertyAndCommandMappersIgnoreClearedPlatformViews()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var context = new MauiContext(app.Services);

			var scrollView = new ScrollView();
			var scroll = Bind<TizenScrollViewHandler>(app, context, typeof(ScrollView), scrollView);
			var scrollPlatform = Assert.IsAssignableFrom<global::Tizen.UIExtensions.NUI.ScrollView>(scroll.PlatformView);
			((IElementHandler)scroll).DisconnectHandler();
			var orientationCount = scrollPlatform.OrientationUpdateCount;
			var scrollCount = scrollPlatform.ScrollToCount;

			TizenScrollViewHandler.MapOrientation(scroll, scrollView);
			TizenScrollViewHandler.MapRequestScrollTo(
				scroll,
				scrollView,
				RuntimeHelpers.GetUninitializedObject(typeof(ScrollToRequest)));

			Assert.Equal(orientationCount, scrollPlatform.OrientationUpdateCount);
			Assert.Equal(scrollCount, scrollPlatform.ScrollToCount);

			var swipeView = new SwipeView();
			var swipe = Bind<TizenSwipeViewHandler>(app, context, typeof(SwipeView), swipeView);
			var swipePlatform = swipe.PlatformView;
			((IElementHandler)swipe).DisconnectHandler();
			TizenSwipeViewHandler.MapRequestOpen(
				swipe,
				swipeView,
				RuntimeHelpers.GetUninitializedObject(typeof(SwipeViewOpenRequest)));
			Assert.Equal(0, swipePlatform.OpenRequestCount);

			var indicatorView = new IndicatorView();
			var indicator = Bind<TizenIndicatorViewHandler>(app, context, typeof(IndicatorView), indicatorView);
			var indicatorPlatform = indicator.PlatformView;
			((IElementHandler)indicator).DisconnectHandler();
			var countUpdates = indicatorPlatform.UpdateCountCount;
			TizenIndicatorViewHandler.MapCount(indicator, indicatorView);
			Assert.Equal(countUpdates, indicatorPlatform.UpdateCountCount);

			var imageView = new Image();
			var image = Bind<TizenImageHandler>(app, context, typeof(Image), imageView);
			var imagePlatform = image.PlatformView;
			var imageAspectCount = imagePlatform.Applied.Count(entry => entry == "WaveBAspect");
			((IElementHandler)image).DisconnectHandler();
			TizenImageHandler.MapAspect(image, imageView);
			Assert.Equal(imageAspectCount, imagePlatform.Applied.Count(entry => entry == "WaveBAspect"));

			var ellipse = new Microsoft.Maui.Controls.Shapes.Ellipse();
			var shape = Bind<TizenShapeViewHandler>(app, context, typeof(Microsoft.Maui.Controls.Shapes.Ellipse), ellipse);
			var shapePlatform = shape.PlatformView;
			var shapeCount = shapePlatform.Applied.Count(entry => entry == "WaveBShape");
			((IElementHandler)shape).DisconnectHandler();
			TizenShapeViewHandler.MapFill(shape, ellipse);
			Assert.Equal(shapeCount, shapePlatform.Applied.Count(entry => entry == "WaveBShape"));

			var pathView = new Microsoft.Maui.Controls.Shapes.Path();
			var path = Bind<TizenPathHandler>(
				app, context, typeof(Microsoft.Maui.Controls.Shapes.Path), pathView);
			var pathPlatform = path.PlatformView;
			var pathCount = pathPlatform.Applied.Count(entry => entry == "WaveBShapeUpdate");
			((IElementHandler)path).DisconnectHandler();
			TizenPathHandler.MapData(path, pathView);
			Assert.Equal(
				pathCount,
				pathPlatform.Applied.Count(entry => entry == "WaveBShapeUpdate"));
		}

		[Fact]
		public void SwipeMenuInitialAndSubsequentMappingsRunUntilDisconnect()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var context = new MauiContext(app.Services);
			var item = new SwipeItem { Text = "initial" };
			var handler = Bind<TizenSwipeItemMenuItemHandler>(
				app, context, typeof(SwipeItem), item);
			var platform = handler.PlatformView;

			Assert.Contains("WaveBMenuText", platform.Applied);
			var initialCount = platform.Applied.Count(entry => entry == "WaveBMenuText");

			item.Text = "updated";
			var updatedCount = platform.Applied.Count(entry => entry == "WaveBMenuText");
			Assert.True(updatedCount > initialCount);
			TizenSwipeItemMenuItemHandler.MapText(handler, item);

			Assert.Equal(
				updatedCount + 1,
				platform.Applied.Count(entry => entry == "WaveBMenuText"));

			((IElementHandler)handler).DisconnectHandler();
			TizenSwipeItemMenuItemHandler.MapText(handler, item);

			Assert.Equal(
				updatedCount + 1,
				platform.Applied.Count(entry => entry == "WaveBMenuText"));
		}

		[Fact]
		public async Task ImageButtonClearFailureStillReleasesLoaderResultAndBaseDisconnect()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var context = new MauiContext(app.Services);
			var view = new ImageButton();
			var handler = Bind<TizenImageButtonHandler>(
				app, context, typeof(ImageButton), view);
			var platform = handler.PlatformView;
			var loader = (TizenImageLoader<TizenImageSource>)typeof(TizenImageButtonHandler)
				.GetField("_sourceLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
				.GetValue(handler)!;
			var result = new TrackingImageResult(() => platform.ResourceClearAttemptCount);
			var source = new TrackingImageSource();

			await loader.LoadAsync(
				source,
				(_, _) => Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(result),
				action =>
				{
					action();
					return Task.CompletedTask;
				},
				_ => platform.ResourceUrl = "loaded",
				static () => true,
				static () => true);

			platform.ThrowOnResourceClear = true;

			Assert.Throws<InvalidOperationException>(
				() => ((IElementHandler)handler).DisconnectHandler());
			Assert.Equal(1, result.DisposeCount);
			Assert.True(result.ClearWasAttemptedBeforeDispose);
			Assert.Null(((IElementHandler)handler).PlatformView);
		}

		static THandler Bind<THandler>(
			MauiApp app,
			IMauiContext context,
			System.Type viewType,
			IElement view)
			where THandler : class, IElementHandler
		{
			var handler = Assert.IsType<THandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(viewType));
			handler.SetMauiContext(context);
			handler.SetVirtualView(view);
			return handler;
		}

		sealed class ControlsApp : Application
		{
		}

		sealed class TrackingImageSource : IImageSource
		{
			public bool IsEmpty => false;
		}

		sealed class TrackingImageResult : IImageSourceServiceResult<TizenImageSource>
		{
			readonly System.Func<int> _clearAttemptCount;

			public TrackingImageResult(System.Func<int> clearAttemptCount) =>
				_clearAttemptCount = clearAttemptCount;

			public TizenImageSource Value { get; } = new();
			public bool IsResolutionDependent => false;
			public bool IsDisposed => DisposeCount > 0;
			public int DisposeCount { get; private set; }
			public bool ClearWasAttemptedBeforeDispose { get; private set; }
			public void Dispose()
			{
				ClearWasAttemptedBeforeDispose = _clearAttemptCount() > 0;
				DisposeCount++;
			}
		}
	}
}
