using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Controls;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

// Disposal is observed through the stub platform view's IsDisposed flag. IElementHandler.Dispose
// does NOT null the element's Handler property, so asserting on that would have tested the
// framework's bookkeeping rather than whether this handler disposed the right child.

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Handler-level replacement behaviour for <see cref="TizenLayoutHandler"/>.
	/// </summary>
	/// <remarks>
	/// These drive <c>Update</c> itself. An earlier attempt pinned only the premise - that logical
	/// and z-ordered positions diverge - which demonstrated the hazard without ever exercising the
	/// method that got it wrong, and so could not have caught the bug.
	/// </remarks>
	[Collection(StaticMapperCollection.Name)]
	public class LayoutReplacementTests
	{
		class ControlsApp : Microsoft.Maui.Controls.Application
		{
			protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
				new(new ContentPage());
		}

		static (TizenLayoutHandler Handler, IMauiContext Context) CreateHandler(Microsoft.Maui.Controls.Layout layout)
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<ControlsApp>();
			builder.ConfigureTizen();
			builder.ConfigureTizenControls();

			var app = builder.Build();
			var context = new MauiContext(app.Services);

			var handler = new TizenLayoutHandler();
			((IElementHandler)handler).SetMauiContext(context);
			// Populates the children; adding them again here would double-register them.
			handler.SetVirtualView(layout);

			return (handler, context);
		}

		static TizenLabelView PlatformViewOf(IView view)
		{
			var platform = Assert.IsType<TizenLabelView>(view.Handler?.PlatformView);

			Assert.False(platform.IsDisposed, "The child was already disposed before the test acted.");

			return platform;
		}

		[Fact]
		public void ReplacingAChildWithDifferingZOrderDisposesOnlyTheReplacedChild()
		{
			// The bug, driven through the handler.
			//
			// _children mirrored the NATIVE z-order while Update receives a LOGICAL index, so
			// _children[index] returned an unrelated child as soon as any ZIndex was non-zero -
			// and Update disposed it, leaving the child actually being replaced on screen with its
			// handler intact. Nothing threw.
			//
			// ZIndex values are chosen so logical and z-ordered positions genuinely disagree:
			// logical [first, second, third] orders by z as [second, third, first].
			var first = new Label { ZIndex = 10, Text = "first" };
			var second = new Label { ZIndex = 0, Text = "second" };
			var third = new Label { ZIndex = 5, Text = "third" };

			var layout = new VerticalStackLayout { first, second, third };
			var (handler, _) = CreateHandler(layout);

			var firstView = PlatformViewOf(first);
			var secondView = PlatformViewOf(second);
			var thirdView = PlatformViewOf(third);

			// Replace LOGICAL index 0, which is `first`. Native z-position 0 is `second`.
			var replacement = new Label { ZIndex = 10, Text = "replacement" };
			layout[0] = replacement;
			handler.Update(0, replacement);

			// The replaced child is disposed...
			Assert.True(firstView.IsDisposed, "The replaced child was not disposed.");

			// ...and the bystanders are untouched. Before the fix, `second` - which nobody asked to
			// replace - was the one disposed.
			Assert.False(secondView.IsDisposed, "An unrelated child was disposed.");
			Assert.False(thirdView.IsDisposed, "An unrelated child was disposed.");

			Assert.NotNull(replacement.Handler);
		}

		[Fact]
		public void ReplacingAChildWithUniformZOrderStillWorks()
		{
			// The case that hid the bug: with every ZIndex equal, logical and z-ordered positions
			// coincide and the old code looked correct.
			var a = new Label { Text = "a" };
			var b = new Label { Text = "b" };

			var layout = new VerticalStackLayout { a, b };
			var (handler, _) = CreateHandler(layout);

			var aView = PlatformViewOf(a);
			var bView = PlatformViewOf(b);

			var replacement = new Label { Text = "replacement" };
			layout[0] = replacement;
			handler.Update(0, replacement);

			Assert.True(aView.IsDisposed);
			Assert.False(bView.IsDisposed);
			Assert.NotNull(replacement.Handler);
		}

		[Fact]
		public void ReplacingTheLastLogicalChildDisposesThatChild()
		{
			// Boundary: the highest logical index, with a z-order that places it FIRST natively.
			var first = new Label { ZIndex = 5, Text = "first" };
			var last = new Label { ZIndex = 0, Text = "last" };

			var layout = new VerticalStackLayout { first, last };
			var (handler, _) = CreateHandler(layout);

			var firstView = PlatformViewOf(first);
			var lastView = PlatformViewOf(last);

			var replacement = new Label { ZIndex = 0, Text = "replacement" };
			layout[1] = replacement;
			handler.Update(1, replacement);

			Assert.True(lastView.IsDisposed, "The replaced child was not disposed.");
			Assert.False(firstView.IsDisposed, "An unrelated child was disposed.");
			Assert.NotNull(replacement.Handler);
		}

		[Fact]
		public void ReplacingAChildWithItselfIsANoOp()
		{
			// Guards against the identity check being dropped: disposing and re-realising the same
			// instance would destroy its native view mid-flight.
			var only = new Label { Text = "only" };

			var layout = new VerticalStackLayout { only };
			var (handler, _) = CreateHandler(layout);

			var view = PlatformViewOf(only);
			handler.Update(0, only);

			Assert.False(view.IsDisposed, "Replacing a child with itself disposed its native view.");
		}

		[Fact]
		public void AChildAddedAfterConnectIsTrackedAtItsLogicalPosition()
		{
			// SetVirtualView and Add are separate population paths, and both must put the child at
			// its LOGICAL position. Tests that only build through SetVirtualView leave Add
			// unexercised - mutating Add back to a z-ordered insert passed until this existed.
			//
			// `added` has the LOWEST ZIndex, so it lands FIRST natively while being LAST logically:
			// a z-ordered insert would record it at logical position 0.
			var first = new Label { ZIndex = 10, Text = "first" };
			var layout = new VerticalStackLayout { first };
			var (handler, _) = CreateHandler(layout);

			var added = new Label { ZIndex = 0, Text = "added" };
			layout.Add(added);
			handler.Add(added);

			var firstView = PlatformViewOf(first);
			var addedView = PlatformViewOf(added);

			// Replace LOGICAL index 1, which is `added`.
			var replacement = new Label { ZIndex = 0, Text = "replacement" };
			layout[1] = replacement;
			handler.Update(1, replacement);

			Assert.True(addedView.IsDisposed, "The replaced child was not disposed.");
			Assert.False(firstView.IsDisposed, "An unrelated child was disposed.");
		}
	}
}
