// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(StaticMapperCollection.Name)]
	public class PlatformRebindTests
	{
		public static IEnumerable<object[]> ParentTypes()
		{
			yield return [typeof(ContentView)];
			yield return [typeof(Border)];
			yield return [typeof(ScrollView)];
			yield return [typeof(SwipeView)];
			yield return [typeof(SwipeItemView)];
		}

		[Theory]
		[MemberData(nameof(ParentTypes))]
		public void RetainedPlatformRebindsBeforeSecondVirtualViewMapperPass(Type parentType)
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var handler = app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(parentType)
				?? throw new InvalidOperationException($"No handler registered for {parentType.Name}.");
			handler.SetMauiContext(new MauiContext(app.Services));
			var first = Create(parentType);
			var second = Create(parentType);

			handler.SetVirtualView((IElement)first.Parent);
			var platform = Assert.IsAssignableFrom<TizenPlatformView>(handler.PlatformView);
			var measureRequests = (platform as TizenContentViewGroup)?.NeedMeasureUpdateCount;
			handler.SetVirtualView((IElement)second.Parent);

			Assert.Same(platform, handler.PlatformView);
			Assert.Same(handler, second.Parent.Handler);
			AssertBinding(platform, second.Parent);
			if (measureRequests.HasValue)
				Assert.True(((TizenContentViewGroup)platform).NeedMeasureUpdateCount > measureRequests.Value);

			handler.Invoke(nameof(IView.InvalidateMeasure), null);
			Assert.Contains(nameof(IView.InvalidateMeasure), platform.Applied);

			if (second.Child is not null)
				Assert.NotNull(second.Child.Handler);

			handler.DisconnectHandler();
		}

		static (IView Parent, View? Child) Create(Type type)
		{
			var child = new ContentView();

			return type.Name switch
			{
				nameof(ContentView) => (new ContentView { Content = child }, child),
				nameof(Border) => (new Border { Content = child }, child),
				nameof(ScrollView) => (new ScrollView { Content = child }, child),
				nameof(SwipeView) => (new SwipeView { Content = child }, child),
				nameof(SwipeItemView) => (new SwipeItemView { Content = child }, child),
				_ => throw new ArgumentOutOfRangeException(nameof(type)),
			};
		}

		static void AssertBinding(TizenPlatformView platform, IView expected)
		{
			switch (platform)
			{
				case TizenScrollViewGroup scroll:
					Assert.Same(expected, scroll.BoundView);
					break;
				case TizenSwipeViewGroup swipe:
					Assert.Same(expected, swipe.BoundView);
					break;
				case TizenContentViewGroup content:
					Assert.Same(expected, content.VirtualView);
					break;
				default:
					throw new Xunit.Sdk.XunitException($"Unexpected platform type {platform.GetType().Name}.");
			}
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
