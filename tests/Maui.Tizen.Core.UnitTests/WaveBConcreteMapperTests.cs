// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Controls;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(StaticMapperCollection.Name)]
	public class WaveBConcreteMapperTests
	{
		public static IEnumerable<object[]> ViewTypes()
		{
			yield return [typeof(ScrollView)];
			yield return [typeof(Border)];
			yield return [typeof(Image)];
			yield return [typeof(ImageButton)];
			yield return [typeof(GraphicsView)];
			yield return [typeof(RefreshView)];
			yield return [typeof(SwipeView)];
			yield return [typeof(IndicatorView)];
			yield return [typeof(SwipeItemView)];
			yield return [typeof(BoxView)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.Ellipse)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.Line)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.Path)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.Polygon)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.Polyline)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.Rectangle)];
			yield return [typeof(Microsoft.Maui.Controls.Shapes.RoundRectangle)];
		}

		[Theory]
		[MemberData(nameof(ViewTypes))]
		public void ProductionConcreteHandlerExecutesTheTizenViewMapper(Type viewType)
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();

			var factory = app.Services.GetRequiredService<IMauiHandlersFactory>();
			var handler = Assert.IsAssignableFrom<IViewHandler>(factory.GetHandler(viewType));
			var elementHandler = (IElementHandler)handler;
			var view = Assert.IsAssignableFrom<IView>(Activator.CreateInstance(viewType));
			elementHandler.SetMauiContext(new MauiContext(app.Services));
			elementHandler.SetVirtualView((IElement)view);

			var platformView = Assert.IsAssignableFrom<TizenPlatformView>(elementHandler.PlatformView);
			platformView.Applied.Clear();

			foreach (var key in new[]
			{
				nameof(IView.Visibility),
				nameof(IView.IsEnabled),
				nameof(IView.Opacity),
				nameof(IView.Background),
				nameof(IView.Width),
				nameof(IView.Height),
				nameof(IView.MinimumWidth),
				nameof(IView.MinimumHeight),
				nameof(IView.InputTransparent),
				nameof(IView.TranslationX),
				nameof(IView.TranslationY),
				nameof(IView.Scale),
				nameof(IView.ScaleX),
				nameof(IView.ScaleY),
				nameof(IView.Rotation),
				nameof(IView.RotationX),
				nameof(IView.RotationY),
				nameof(IView.AnchorX),
				nameof(IView.AnchorY),
			})
			{
				elementHandler.UpdateValue(key);
			}

			elementHandler.Invoke(nameof(IView.Focus), null);
			elementHandler.Invoke(nameof(IView.InvalidateMeasure), null);

			Assert.Contains(nameof(IView.Visibility), platformView.Applied);
			Assert.Contains(nameof(IView.IsEnabled), platformView.Applied);
			Assert.Contains(nameof(IView.Opacity), platformView.Applied);
			Assert.Contains(nameof(IView.Background), platformView.Applied);
			Assert.Contains(nameof(IView.Width), platformView.Applied);
			Assert.Contains(nameof(IView.Height), platformView.Applied);
			Assert.Contains(nameof(IView.MinimumWidth), platformView.Applied);
			Assert.Contains(nameof(IView.MinimumHeight), platformView.Applied);
			Assert.Contains(nameof(IView.InputTransparent), platformView.Applied);
			Assert.Contains("Transformation", platformView.Applied);
			Assert.Contains(nameof(IView.Focus), platformView.Applied);
			Assert.Contains(nameof(IView.InvalidateMeasure), platformView.Applied);

			(elementHandler as IDisposable)?.Dispose();
		}

		[Fact]
		public void ScrollHandlerConnectDisconnectReconnectUsesCapturedPlatformView()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();

			var factory = app.Services.GetRequiredService<IMauiHandlersFactory>();
			var handler = Assert.IsType<TizenScrollViewHandler>(factory.GetHandler(typeof(ScrollView)));
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(new MauiContext(app.Services));
			elementHandler.SetVirtualView(new ScrollView());

			var first = Assert.IsAssignableFrom<global::Tizen.UIExtensions.NUI.ScrollView>(elementHandler.PlatformView);
			Assert.Equal(1, first.ScrollingSubscriberCount);
			Assert.Equal(1, first.ScrollAnimationEndedSubscriberCount);
			Assert.Equal(1, first.RelayoutSubscriberCount);

			elementHandler.DisconnectHandler();

			Assert.Equal(0, first.ScrollingSubscriberCount);
			Assert.Equal(0, first.ScrollAnimationEndedSubscriberCount);
			Assert.Equal(0, first.RelayoutSubscriberCount);

			elementHandler.SetVirtualView(new ScrollView());
			var second = Assert.IsAssignableFrom<global::Tizen.UIExtensions.NUI.ScrollView>(elementHandler.PlatformView);
			Assert.NotSame(first, second);
			Assert.Equal(1, second.ScrollingSubscriberCount);

			elementHandler.DisconnectHandler();
			Assert.Equal(0, second.ScrollingSubscriberCount);
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
