// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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
	public class OwnedHandlerDisposalTests
	{
		[Fact]
		public void LayoutAttemptsEveryChildClearsOwnershipAndDisposesParent()
		{
			using var app = BuildApp();
			var context = new MauiContext(app.Services);
			var first = new Label { Text = "first" };
			var second = new Label { Text = "second" };
			var third = new Label { Text = "third" };
			var firstHandler = Attach(first, context, throwOnFirstDispose: true);
			var secondHandler = Attach(second, context, throwOnFirstDispose: false);
			var thirdHandler = Attach(third, context, throwOnFirstDispose: false);
			var layout = new VerticalStackLayout { first, second, third };
			var parent = new RecordingLayoutHandler();

			((IElementHandler)parent).SetMauiContext(context);
			parent.SetVirtualView(layout);
			var platformView = Assert.IsType<TizenLayoutViewGroup>(((IElementHandler)parent).PlatformView);

			var failure = Assert.Throws<InvalidOperationException>(parent.Dispose);

			Assert.Equal("owned child dispose", failure.Message);
			Assert.Equal(1, firstHandler.DisposeAttempts);
			Assert.Equal(1, secondHandler.DisposeAttempts);
			Assert.Equal(1, thirdHandler.DisposeAttempts);
			Assert.Equal(0, parent.LogicalChildCount);
			Assert.Equal(1, parent.DisconnectCount);
			Assert.True(platformView.IsDisposed);

			Assert.Null(Record.Exception(parent.Dispose));
			Assert.Equal(1, firstHandler.DisposeAttempts);
			Assert.Equal(1, secondHandler.DisposeAttempts);
			Assert.Equal(1, thirdHandler.DisposeAttempts);
			Assert.Equal(1, parent.DisconnectCount);
		}

		[Fact]
		public void ContentViewClearsThrowingContentAndDisposesParent()
		{
			using var app = BuildApp();
			var context = new MauiContext(app.Services);
			var content = new Label { Text = "content" };
			var contentHandler = Attach(content, context, throwOnFirstDispose: true);
			var contentView = new ContentView { Content = content };
			var parent = new RecordingContentViewHandler();

			((IElementHandler)parent).SetMauiContext(context);
			parent.SetVirtualView(contentView);
			var platformView = Assert.IsType<TizenContentViewGroup>(((IElementHandler)parent).PlatformView);
			Assert.True(parent.HasOwnedContent);

			var failure = Assert.Throws<InvalidOperationException>(parent.Dispose);

			Assert.Equal("owned child dispose", failure.Message);
			Assert.Equal(1, contentHandler.DisposeAttempts);
			Assert.False(parent.HasOwnedContent);
			Assert.Equal(1, parent.DisconnectCount);
			Assert.True(platformView.IsDisposed);

			Assert.Null(Record.Exception(parent.Dispose));
			Assert.Equal(1, contentHandler.DisposeAttempts);
			Assert.Equal(1, parent.DisconnectCount);
		}

		static MauiApp BuildApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<ControlsApp>();
			builder.ConfigureTizen();
			builder.ConfigureTizenControls();
			return builder.Build();
		}

		static ThrowingLabelHandler Attach(
			Label label,
			IMauiContext context,
			bool throwOnFirstDispose)
		{
			var handler = new ThrowingLabelHandler(throwOnFirstDispose);
			((IElementHandler)handler).SetMauiContext(context);
			handler.SetVirtualView(label);
			return handler;
		}

		sealed class ThrowingLabelHandler : TizenLabelHandler
		{
			readonly bool _throwOnFirstDispose;

			public ThrowingLabelHandler(bool throwOnFirstDispose) =>
				_throwOnFirstDispose = throwOnFirstDispose;

			public int DisposeAttempts { get; private set; }

			protected override void Dispose(bool disposing)
			{
				DisposeAttempts++;

				try
				{
					base.Dispose(disposing);
				}
				finally
				{
					if (_throwOnFirstDispose && DisposeAttempts == 1)
						throw new InvalidOperationException("owned child dispose");
				}
			}
		}

		sealed class RecordingLayoutHandler : TizenLayoutHandler
		{
			public int DisconnectCount { get; private set; }

			protected override void DisconnectHandler(TizenLayoutViewGroup platformView)
			{
				DisconnectCount++;
				base.DisconnectHandler(platformView);
			}
		}

		sealed class RecordingContentViewHandler : TizenContentViewHandler
		{
			public int DisconnectCount { get; private set; }

			protected override void DisconnectHandler(TizenContentViewGroup platformView)
			{
				DisconnectCount++;
				base.DisconnectHandler(platformView);
			}
		}

		sealed class ControlsApp : Microsoft.Maui.Controls.Application
		{
			protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
				new(new ContentPage());
		}
	}
}
