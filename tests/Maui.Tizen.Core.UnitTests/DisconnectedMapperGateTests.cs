// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Linq;
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
	}
}
