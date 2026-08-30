// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class HandlerDisposalTests
	{
		[Fact]
		public void DisconnectAndPlatformDisposalBothRunAndSecondDisposeIsSafe()
		{
			var handler = new ThrowingHandler();
			handler.SetVirtualView(new TestView());
			var platformView = handler.CreatedPlatformView;

			var failure = Assert.Throws<AggregateException>(handler.Dispose);

			Assert.Equal(
				["disconnect cleanup", "platform dispose"],
				Array.ConvertAll(failure.InnerExceptions.ToArray(), exception => exception.Message));
			Assert.Equal(1, handler.DisconnectCount);
			Assert.True(handler.EventDetached);
			Assert.Equal(1, handler.BaseDisconnectCount);
			Assert.Equal(1, platformView.DisposeCount);
			Assert.True(platformView.IsDisposed);

			var second = Record.Exception(handler.Dispose);

			Assert.Null(second);
			Assert.Equal(1, handler.DisconnectCount);
			Assert.Equal(1, handler.BaseDisconnectCount);
			Assert.Equal(1, platformView.DisposeCount);
		}

		[Fact]
		public void ReconnectCompletesNewLifetimeBeforeSurfacingStaleCleanupFailure()
		{
			var handler = new ThrowingReconnectHandler();
			var platformView = new TizenPlatformView();

			var failure = Assert.Throws<InvalidOperationException>(
				() => handler.ReconnectForTest(platformView));

			Assert.Equal("stale cleanup", failure.Message);
			Assert.True(handler.LifetimeReplaced);
			Assert.True(handler.BaseConnected);
			Assert.True(handler.EventAttached);
		}

		sealed class TestView : StubView
		{
		}

		sealed class ThrowingPlatformView : TizenPlatformView
		{
			public int DisposeCount { get; private set; }

			protected override void Dispose(bool disposing)
			{
				DisposeCount++;
				base.Dispose(disposing);
				throw new InvalidOperationException("platform dispose");
			}
		}

		sealed class ThrowingHandler : TizenViewHandler<TestView, ThrowingPlatformView>
		{
			static readonly IPropertyMapper<TestView, IViewHandler> Mapper =
				new PropertyMapper<TestView, IViewHandler>();

			public ThrowingHandler()
				: base(Mapper)
			{
			}

			public ThrowingPlatformView CreatedPlatformView { get; private set; } = null!;

			public int DisconnectCount { get; private set; }

			public int BaseDisconnectCount { get; private set; }

			public bool EventDetached { get; private set; }

			protected override ThrowingPlatformView CreatePlatformView() =>
				CreatedPlatformView = new ThrowingPlatformView();

			protected override void DisconnectHandler(ThrowingPlatformView platformView)
			{
				DisconnectCount++;

				TizenCleanup.Run(
					() =>
					{
						EventDetached = true;
						throw new InvalidOperationException("disconnect cleanup");
					},
					() =>
					{
						BaseDisconnectCount++;
						base.DisconnectHandler(platformView);
					});
			}
		}

		sealed class ThrowingReconnectHandler : TizenViewHandler<TestView, TizenPlatformView>
		{
			static readonly IPropertyMapper<TestView, IViewHandler> Mapper =
				new PropertyMapper<TestView, IViewHandler>();

			public ThrowingReconnectHandler()
				: base(Mapper)
			{
			}

			public bool LifetimeReplaced { get; private set; }

			public bool BaseConnected { get; private set; }

			public bool EventAttached { get; private set; }

			public void ReconnectForTest(TizenPlatformView platformView) => ConnectHandler(platformView);

			protected override TizenPlatformView CreatePlatformView() => new();

			protected override void ConnectHandler(TizenPlatformView platformView)
			{
				TizenCleanup.Run(
					static () => throw new InvalidOperationException("stale cleanup"),
					() => LifetimeReplaced = true,
					() =>
					{
						base.ConnectHandler(platformView);
						BaseConnected = true;
					},
					() => EventAttached = true);
			}
		}
	}
}
