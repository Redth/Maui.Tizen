using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Runtime regressions for the <see cref="IView.ZIndex"/> command chain.
	/// </summary>
	/// <remarks>
	/// The chain has three links and every one of them is easy to break silently:
	/// <list type="number">
	/// <item><description>
	/// MAUI Controls raises <c>Invoke(nameof(IView.ZIndex))</c> on the <b>child's</b> handler.
	/// </description></item>
	/// <item><description>
	/// The child's command mapper forwards to the <b>parent</b> layout as
	/// <c>Invoke(nameof(UpdateZIndex), view)</c> - passing the <see cref="IView"/> itself.
	/// </description></item>
	/// <item><description>
	/// The parent's <c>UpdateZIndex</c> mapper unwraps that <see cref="IView"/> and re-orders.
	/// </description></item>
	/// </list>
	/// Two defects were found here: the child mapper had no <c>ZIndex</c> entry at all, so link 1
	/// resolved nothing; and the parent mapper expected a <c>LayoutHandlerUpdate</c>, so link 3
	/// would have dropped the call even once link 1 worked. Both fail silently - the visual order
	/// simply never changes.
	/// </remarks>
	public class ZIndexCommandTests
	{
		[Fact]
		public void ChildCommandMapperDefinesZIndex() =>
			Assert.NotNull(TizenViewMappers.ViewCommandMapper.GetCommand(nameof(IView.ZIndex)));

		[Fact]
		public void ZIndexCommandReachesTheParentLayoutWithTheChildView()
		{
			// Link 1 -> link 2 -> link 3, end to end, asserting the parent received the CHILD.
			var layout = new RecordingLayout();
			var child = new StubLabel { ZIndex = 5 };
			layout.Add(child);

			var layoutHandler = new RecordingLayoutHandler(layout);
			layout.Handler = layoutHandler;

			var childHandler = new TizenLabelHandler();
			childHandler.SetVirtualView(child);
			child.Handler = childHandler;

			((IElementHandler)childHandler).Invoke(nameof(IView.ZIndex), null);

			Assert.Equal(new object[] { child }, layoutHandler.ReorderedChildren);
		}

		[Fact]
		public void ParentMapperUnwrapsAnIViewNotALayoutHandlerUpdate()
		{
			// Link 3 in isolation. MAUI passes the IView directly; expecting a LayoutHandlerUpdate
			// silently drops every re-order.
			var layout = new RecordingLayout();
			var child = new StubLabel();
			layout.Add(child);

			var handler = new RecordingLayoutHandler(layout);

			TizenLayoutHandler.CommandMapper.Invoke(
				handler, layout, nameof(ITizenLayoutHandler.UpdateZIndex), child);

			Assert.Equal(new object[] { child }, handler.ReorderedChildren);
		}

		[Fact]
		public void ParentMapperIgnoresALayoutHandlerUpdateArgument()
		{
			// Guards the inverse mistake: if someone "helpfully" re-adds LayoutHandlerUpdate
			// support, the IView path must still be the one MAUI actually uses.
			var layout = new RecordingLayout();
			var child = new StubLabel();
			layout.Add(child);

			var handler = new RecordingLayoutHandler(layout);

			TizenLayoutHandler.CommandMapper.Invoke(
				handler, layout, nameof(ITizenLayoutHandler.UpdateZIndex), new LayoutHandlerUpdate(0, child));

			Assert.Empty(handler.ReorderedChildren);
		}

		[Fact]
		public void ZIndexOrderingMatchesTheComputedHandlerIndex()
		{
			// The re-order target itself: GetLayoutHandlerIndex is what decides where the child's
			// platform view lands, so the command chain is only correct if it agrees with it.
			var back = new StubLabel { ZIndex = -1 };
			var middle = new StubLabel { ZIndex = 0 };
			var front = new StubLabel { ZIndex = 10 };

			var layout = new RecordingLayout();
			layout.Add(front);
			layout.Add(middle);
			layout.Add(back);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(back));
			Assert.Equal(1, layout.GetLayoutHandlerIndex(middle));
			Assert.Equal(2, layout.GetLayoutHandlerIndex(front));
		}

		sealed class RecordingLayoutHandler : ITizenLayoutHandler
		{
			readonly ILayout _layout;

			public RecordingLayoutHandler(ILayout layout) => _layout = layout;

			public List<IView> ReorderedChildren { get; } = new();

			public ILayout VirtualView => _layout;

			public TizenLayoutViewGroup PlatformView { get; } = new(null);

			IView? IViewHandler.VirtualView => _layout;

			IElement? IElementHandler.VirtualView => _layout;

			object? IElementHandler.PlatformView => PlatformView;

			public bool HasContainer { get => false; set { } }

			public object? ContainerView => null;

			public IMauiContext? MauiContext => null;

			public void UpdateZIndex(IView view) => ReorderedChildren.Add(view);

			public void Add(IView view)
			{
			}

			public void Remove(IView view)
			{
			}

			public void Clear()
			{
			}

			public void Insert(int index, IView view)
			{
			}

			public void Update(int index, IView view)
			{
			}

			public void SetMauiContext(IMauiContext mauiContext)
			{
			}

			public void SetVirtualView(IElement view)
			{
			}

			public void UpdateValue(string property)
			{
			}

			public void Invoke(string command, object? args) =>
				TizenLayoutHandler.CommandMapper.Invoke(this, _layout, command, args);

			public void DisconnectHandler()
			{
			}

			public Graphics.Size GetDesiredSize(double widthConstraint, double heightConstraint) =>
				Graphics.Size.Zero;

			public void PlatformArrange(Graphics.Rect frame)
			{
			}
		}

		/// <summary>A layout that parents its children, so the ZIndex chain can find it.</summary>
		sealed class RecordingLayout : LayoutOrderingTests.StubLayoutInternal
		{
			public new void Add(IView item)
			{
				// Parenting is what link 2 depends on: MapZIndex looks at view.Parent to find the
				// layout to notify. An unparented child silently no-ops.
				if (item is StubView stub)
					stub.Parent = this;

				base.Add(item);
			}
		}
	}
}
