// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class ContentOwnershipTests
	{
		sealed class FakeView : IDisposable
		{
			public FakeView(string name) => Name = name;

			public string Name { get; }
			public int DisposeCount { get; private set; }

			public void Dispose() => DisposeCount++;
		}

		sealed class FakeHandler : IDisposable
		{
			readonly bool _throw;

			public FakeHandler(bool @throw = false) => _throw = @throw;

			public int DisposeCount { get; private set; }

			public void Dispose()
			{
				DisposeCount++;
				if (_throw)
					throw new InvalidOperationException("child dispose");
			}
		}

		sealed class ThrowingPlatformHandler : ITizenPlatformViewHandler
		{
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
				throw new InvalidOperationException("child dispose");
			}

			public void SetMauiContext(IMauiContext mauiContext) { }
			public void SetVirtualView(IElement view) { }
			public void UpdateValue(string property) { }
			public void Invoke(string command, object? args) { }
			public void DisconnectHandler() { }
			public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
			public void PlatformArrange(Rect frame) { }
		}

		[Fact]
		public void ContentAReplacedByBThenTeardownDisposesEachOwnerOnce()
		{
			var a = new FakeView("A");
			var b = new FakeView("B");
			var aHandler = new FakeHandler();
			var bHandler = new FakeHandler();
			FakeView? currentView = a;
			FakeHandler? currentHandler = aHandler;
			long generation = 0;
			var detached = new List<string>();

			TizenContentOwnership.Replace(
				ref currentView,
				ref currentHandler,
				ref generation,
				b,
				bHandler,
				view => detached.Add(view.Name),
				static _ => { },
				static () => { });

			Assert.Same(b, currentView);
			Assert.Same(bHandler, currentHandler);
			Assert.Equal(new[] { "A" }, detached);
			Assert.Equal(1, aHandler.DisposeCount);
			Assert.Equal(0, a.DisposeCount);

			TizenContentOwnership.Clear(
				ref currentView,
				ref currentHandler,
				ref generation,
				view => detached.Add(view.Name),
				static () => { });

			Assert.Null(currentView);
			Assert.Null(currentHandler);
			Assert.Equal(new[] { "A", "B" }, detached);
			Assert.Equal(1, bHandler.DisposeCount);
			Assert.Equal(0, b.DisposeCount);
		}

		[Fact]
		public void IdenticalViewAndHandlerReplacementIsACentralNoOp()
		{
			var view = new FakeView("same");
			var handler = new FakeHandler();
			FakeView? currentView = view;
			FakeHandler? currentHandler = handler;
			long generation = 0;
			var callbacks = 0;

			var changed = TizenContentOwnership.Replace(
				ref currentView,
				ref currentHandler,
				ref generation,
				view,
				handler,
				_ => callbacks++,
				_ => callbacks++,
				() => callbacks++);

			Assert.False(changed);
			Assert.Equal(0, generation);
			Assert.Equal(0, callbacks);
			Assert.Equal(0, handler.DisposeCount);
			Assert.Same(view, currentView);
			Assert.Same(handler, currentHandler);
		}

		[Fact]
		public void UnownedPlaceholderIsDisposedExactlyOnce()
		{
			var placeholder = new FakeView("placeholder");
			FakeView? currentView = placeholder;
			FakeHandler? currentHandler = null;
			long generation = 0;

			TizenContentOwnership.Clear(
				ref currentView,
				ref currentHandler,
				ref generation,
				static _ => { },
				static () => { });
			TizenContentOwnership.Clear(
				ref currentView,
				ref currentHandler,
				ref generation,
				static _ => { },
				static () => { });

			Assert.Equal(1, placeholder.DisposeCount);
		}

		[Fact]
		public void ThrowingChildCannotSkipReplacementOrCauseReentryDoubleDispose()
		{
			var oldView = new FakeView("old");
			var oldHandler = new FakeHandler(@throw: true);
			var replacement = new FakeView("replacement");
			var replacementHandler = new FakeHandler();
			FakeView? currentView = oldView;
			FakeHandler? currentHandler = oldHandler;
			long generation = 0;
			var detached = 0;
			var cancelled = 0;

			Assert.ThrowsAny<Exception>(() =>
				TizenContentOwnership.Replace(
					ref currentView,
					ref currentHandler,
					ref generation,
					replacement,
					replacementHandler,
					_ => detached++,
					static _ => { },
					() => cancelled++));

			Assert.Same(replacement, currentView);
			Assert.Same(replacementHandler, currentHandler);
			Assert.Equal(1, detached);
			Assert.Equal(1, cancelled);
			Assert.Equal(1, oldHandler.DisposeCount);

			TizenContentOwnership.Clear(
				ref currentView,
				ref currentHandler,
				ref generation,
				static _ => { },
				static () => { });

			Assert.Equal(1, oldHandler.DisposeCount);
			Assert.Equal(1, replacementHandler.DisposeCount);
		}

		[Fact]
		public void InvalidatedGenerationRejectsLateAnimationCallbacks()
		{
			var generation = new TizenCallbackGeneration();
			var view = new FakeView("current");
			var token = generation.Current;

			Assert.True(generation.IsCurrent(token, view, view));

			generation.Invalidate();

			Assert.False(generation.IsCurrent(token, view, view));
		}

		[Fact]
		public void ReentrantCleanupObservesAnEmptyOwnershipSlot()
		{
			var view = new FakeView("owned");
			var handler = new FakeHandler();
			FakeView? currentView = view;
			FakeHandler? currentHandler = handler;
			long generation = 0;
			var reentered = false;

			TizenContentOwnership.Clear(
				ref currentView,
				ref currentHandler,
				ref generation,
				_ =>
				{
					if (reentered)
						return;

					reentered = true;
					TizenContentOwnership.Clear(
						ref currentView,
						ref currentHandler,
						ref generation,
						static _ => { },
						static () => { });
				},
				static () => { });

			Assert.True(reentered);
			Assert.Equal(1, handler.DisposeCount);
			Assert.Null(currentView);
			Assert.Null(currentHandler);
		}

		[Fact]
		public void ThrowingBorderChildCannotSkipParentCleanupOrReenter()
		{
			var handler = new TizenBorderHandler();
			var context = new MauiContext(new Microsoft.Extensions.DependencyInjection.ServiceCollection()
				.AddLogging()
				.BuildServiceProvider());
			((IElementHandler)handler).SetMauiContext(context);
			((IElementHandler)handler).SetVirtualView(new Microsoft.Maui.Controls.Border());

			var platformView = Assert.IsType<TizenContentViewGroup>(((IElementHandler)handler).PlatformView);
			var child = new ThrowingPlatformHandler();
			platformView.Children.Add(child.View);

			typeof(TizenBorderHandler)
				.GetField("_contentView", BindingFlags.Instance | BindingFlags.NonPublic)!
				.SetValue(handler, child.View);
			typeof(TizenBorderHandler)
				.GetField("_contentHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
				.SetValue(handler, child);

			Assert.Throws<InvalidOperationException>(handler.Dispose);

			Assert.Equal(1, child.DisposeCount);
			Assert.True(platformView.IsDisposed);
			Assert.Empty(platformView.Children);

			handler.Dispose();
			Assert.Equal(1, child.DisposeCount);
		}
	}
}
