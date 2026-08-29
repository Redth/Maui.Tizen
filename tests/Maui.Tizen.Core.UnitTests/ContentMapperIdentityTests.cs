// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(StaticMapperCollection.Name)]
	public class ContentMapperIdentityTests
	{
		public static IEnumerable<object[]> ParentTypes()
		{
			yield return [typeof(Border)];
			yield return [typeof(RefreshView)];
			yield return [typeof(ScrollView)];
			yield return [typeof(SwipeItemView)];
		}

		[Theory]
		[MemberData(nameof(ParentTypes))]
		public void RepeatedContentMappingIsANoOpAndReconnectCreatesFreshChild(Type parentType)
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var context = new MauiContext(app.Services);
			var child = new ContentView();
			var parent = CreateParent(parentType, child);
			var parentHandler = app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(parentType)
				?? throw new InvalidOperationException($"No handler registered for {parentType.Name}.");

			parentHandler.SetMauiContext(context);
			parentHandler.SetVirtualView((IElement)parent);

			var firstChildHandler = child.Handler;
			var firstChildPlatform = Assert.IsType<TizenContentViewGroup>(firstChildHandler?.PlatformView);

			parentHandler.UpdateValue(nameof(IContentView.Content));

			Assert.Same(firstChildHandler, child.Handler);
			Assert.Same(firstChildPlatform, child.Handler?.PlatformView);
			Assert.False(firstChildPlatform.IsDisposed);

			parentHandler.DisconnectHandler();

			Assert.True(firstChildPlatform.IsDisposed);

			parentHandler.SetVirtualView((IElement)parent);

			var secondChildHandler = child.Handler;
			var secondChildPlatform = Assert.IsType<TizenContentViewGroup>(secondChildHandler?.PlatformView);
			Assert.NotSame(firstChildHandler, secondChildHandler);
			Assert.NotSame(firstChildPlatform, secondChildPlatform);
			Assert.False(secondChildPlatform.IsDisposed);

			parentHandler.DisconnectHandler();
		}

		[Fact]
		public void ReentrantBorderReplacementKeepsNewestContentAndDisposesPreparedIntermediate()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var context = new MauiContext(app.Services);
			var border = new Border();
			var handler = Assert.IsType<TizenBorderHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(Border)));
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(context);

			var a = new ContentView();
			var b = new ContentView();
			var c = new ContentView();
			var bHandler = new CallbackHandler();
			var cHandler = new CallbackHandler();
			var aHandler = new CallbackHandler(() =>
			{
				border.Content = c;
				elementHandler.UpdateValue(nameof(IContentView.Content));
			});
			a.Handler = aHandler;
			b.Handler = bHandler;
			c.Handler = cHandler;
			border.Content = a;
			elementHandler.SetVirtualView(border);

			border.Content = b;
			elementHandler.UpdateValue(nameof(IContentView.Content));

			var platform = Assert.IsType<TizenContentViewGroup>(elementHandler.PlatformView);
			Assert.Single(platform.Children);
			Assert.Same(cHandler.View, platform.Children[0]);
			Assert.Equal(1, aHandler.DisposeCount);
			Assert.Equal(1, bHandler.DisposeCount);
			Assert.Equal(0, cHandler.DisposeCount);
			Assert.True(bHandler.View.IsDisposed);

			elementHandler.DisconnectHandler();
			Assert.Equal(1, cHandler.DisposeCount);
		}

		static IView CreateParent(Type parentType, View child) =>
			parentType.Name switch
			{
				nameof(Border) => new Border { Content = child },
				nameof(RefreshView) => new RefreshView { Content = child },
				nameof(ScrollView) => new ScrollView { Content = child },
				nameof(SwipeItemView) => new SwipeItemView { Content = child },
				_ => throw new ArgumentOutOfRangeException(nameof(parentType)),
			};

		sealed class ControlsApp : Application
		{
		}

		sealed class CallbackHandler : ITizenPlatformViewHandler
		{
			readonly Action? _onDispose;

			public CallbackHandler(Action? onDispose = null) => _onDispose = onDispose;

			public TizenPlatformView View { get; } = new();
			public int DisposeCount { get; private set; }
			public TizenNativeView? PlatformView => View;
			public TizenNativeView? ContainerView => null;
			object? IElementHandler.PlatformView => View;
			object? IViewHandler.ContainerView => null;
			public bool HasContainer { get; set; }
			public IMauiContext? MauiContext => null;
			public IElement? VirtualView => null;
			IView? IViewHandler.VirtualView => null;

			public void Dispose()
			{
				DisposeCount++;
				View.Dispose();
				_onDispose?.Invoke();
			}

			public void SetMauiContext(IMauiContext mauiContext) { }
			public void SetVirtualView(IElement view) { }
			public void UpdateValue(string property) { }
			public void Invoke(string command, object? args) { }
			public void DisconnectHandler() { }
			public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
			public void PlatformArrange(Rect frame) { }
		}
	}
}
