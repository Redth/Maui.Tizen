using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using Xunit;


namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class LayoutOrderingTests
	{
		[Fact]
		public void LogicalIndexAndZOrderedPositionDivergeOnceZIndexIsUsed()
		{
			// The premise behind the Update fix, stated as a fact about the data rather than about
			// the handler.
			//
			// Update receives a LOGICAL index into the layout's child collection. The native
			// collection is ordered by z-index. Those coincide only while every child sits at
			// ZIndex 0 - which is the case in every simple test, and is why indexing the native
			// collection with the logical index looked correct for so long.
			var first = new StubViewInternal { ZIndex = 10 };
			var second = new StubViewInternal { ZIndex = 0 };
			var third = new StubViewInternal { ZIndex = 5 };
			var layout = new StubLayoutInternal(first, second, third);

			var zOrdered = layout.OrderByZIndex().ToArray();

			// Logical index 0 is `first`, but z-ordered position 0 is `second`.
			Assert.Same(first, layout[0]);
			Assert.Same(second, zOrdered[0]);
			Assert.NotSame(layout[0], zOrdered[0]);

			// So replacing logical child 0 by native index 0 would have removed and DISPOSED
			// `second` - a child nobody asked to replace - while `first` stayed on screen with its
			// handler intact. Nothing throws; the layout just shows the wrong thing and leaks.
			Assert.Equal(2, Array.IndexOf(zOrdered, first));
		}

		[Fact]
		public void ZOrderedPositionMatchesLogicalIndexOnlyWhenAllZIndexesAreEqual()
		{
			// The case that made the bug invisible.
			var a = new StubViewInternal { ZIndex = 0 };
			var b = new StubViewInternal { ZIndex = 0 };
			var c = new StubViewInternal { ZIndex = 0 };
			var layout = new StubLayoutInternal(a, b, c);

			var zOrdered = layout.OrderByZIndex().ToArray();

			for (var i = 0; i < 3; i++)
				Assert.Same(layout[i], zOrdered[i]);
		}

		[Fact]
		public void OrderByZIndexIsStableForEqualZIndexes()
		{
			var a = new StubViewInternal { ZIndex = 0 };
			var b = new StubViewInternal { ZIndex = 0 };
			var c = new StubViewInternal { ZIndex = 0 };
			var layout = new StubLayoutInternal(a, b, c);

			Assert.Equal(new IView[] { a, b, c }, layout.OrderByZIndex().ToArray());
		}

		[Fact]
		public void OrderByZIndexSortsAscending()
		{
			var high = new StubViewInternal { ZIndex = 10 };
			var low = new StubViewInternal { ZIndex = -5 };
			var mid = new StubViewInternal { ZIndex = 1 };
			var layout = new StubLayoutInternal(high, low, mid);

			Assert.Equal(new IView[] { low, mid, high }, layout.OrderByZIndex().ToArray());
		}

		[Fact]
		public void GetLayoutHandlerIndexReturnsMinusOneForEmptyLayout() =>
			Assert.Equal(-1, new StubLayoutInternal().GetLayoutHandlerIndex(new StubViewInternal()));

		[Fact]
		public void GetLayoutHandlerIndexReturnsMinusOneForForeignView()
		{
			var layout = new StubLayoutInternal(new StubViewInternal(), new StubViewInternal());

			Assert.Equal(-1, layout.GetLayoutHandlerIndex(new StubViewInternal()));
		}

		[Fact]
		public void GetLayoutHandlerIndexHandlesSingleChild()
		{
			var only = new StubViewInternal();
			var layout = new StubLayoutInternal(only);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(only));
		}

		[Fact]
		public void GetLayoutHandlerIndexRespectsDeclarationOrderForEqualZIndex()
		{
			var a = new StubViewInternal();
			var b = new StubViewInternal();
			var c = new StubViewInternal();
			var layout = new StubLayoutInternal(a, b, c);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(a));
			Assert.Equal(1, layout.GetLayoutHandlerIndex(b));
			Assert.Equal(2, layout.GetLayoutHandlerIndex(c));
		}

		[Fact]
		public void GetLayoutHandlerIndexAccountsForZIndex()
		{
			var back = new StubViewInternal { ZIndex = -1 };
			var middle = new StubViewInternal { ZIndex = 0 };
			var front = new StubViewInternal { ZIndex = 5 };

			// Declaration order deliberately differs from z-order.
			var layout = new StubLayoutInternal(front, middle, back);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(back));
			Assert.Equal(1, layout.GetLayoutHandlerIndex(middle));
			Assert.Equal(2, layout.GetLayoutHandlerIndex(front));
		}

		[Fact]
		public void ExtensionsRejectNullArguments()
		{
			Assert.Throws<ArgumentNullException>(() => ((ILayout)null!).OrderByZIndex());
			Assert.Throws<ArgumentNullException>(() => ((ILayout)null!).GetLayoutHandlerIndex(new StubViewInternal()));
			Assert.Throws<ArgumentNullException>(() => new StubLayoutInternal().GetLayoutHandlerIndex(null!));
		}

		internal class StubLayoutInternal : ILayout
		{
			readonly List<IView> _children;

			public StubLayoutInternal(params IView[] children) => _children = children.ToList();

			public IView this[int index]
			{
				get => _children[index];
				set => _children[index] = value;
			}

			public int Count => _children.Count;

			public bool IsReadOnly => false;

			public bool ClipsToBounds => false;

			public Thickness Padding => Thickness.Zero;

			public bool IgnoreSafeArea => false;

			public virtual void Add(IView item) => _children.Add(item);

			public void Clear() => _children.Clear();

			public bool Contains(IView item) => _children.Contains(item);

			public void CopyTo(IView[] array, int arrayIndex) => _children.CopyTo(array, arrayIndex);

			public IEnumerator<IView> GetEnumerator() => _children.GetEnumerator();

			public int IndexOf(IView item) => _children.IndexOf(item);

			public void Insert(int index, IView item) => _children.Insert(index, item);

			public bool Remove(IView item) => _children.Remove(item);

			public void RemoveAt(int index) => _children.RemoveAt(index);

			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

			public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) => Size.Zero;

			public Size CrossPlatformArrange(Rect bounds) => bounds.Size;

			public IViewHandler? Handler { get; set; }

			IElementHandler? IElement.Handler
			{
				get => Handler;
				set => Handler = value as IViewHandler;
			}

			public IElement? Parent => null;

			public bool IsFocused { get; set; }

			public Visibility Visibility => Visibility.Visible;

			public double Opacity => 1;

			public Paint? Background => null;

			public IShape? Clip => null;

			public IShadow? Shadow => null;

			public bool InputTransparent => false;

			public bool IsEnabled => true;

			public double Width => 0;

			public double Height => 0;

			public double MinimumWidth => 0;

			public double MinimumHeight => 0;

			public double MaximumWidth => double.PositiveInfinity;

			public double MaximumHeight => double.PositiveInfinity;

			public Thickness Margin => Thickness.Zero;

			public Rect Frame { get; set; }

			public FlowDirection FlowDirection => FlowDirection.LeftToRight;

			public LayoutAlignment HorizontalLayoutAlignment => LayoutAlignment.Fill;

			public LayoutAlignment VerticalLayoutAlignment => LayoutAlignment.Fill;

			public Semantics? Semantics => null;

			public string AutomationId => string.Empty;

			public int ZIndex => 0;

			public Size DesiredSize => Size.Zero;

			public double AnchorX => 0.5;

			public double AnchorY => 0.5;

			public double Rotation => 0;

			public double RotationX => 0;

			public double RotationY => 0;

			public double Scale => 1;

			public double ScaleX => 1;

			public double ScaleY => 1;

			public double TranslationX => 0;

			public double TranslationY => 0;

			public Size Arrange(Rect bounds) => bounds.Size;

			public void InvalidateArrange()
			{
			}

			public void InvalidateMeasure()
			{
			}

			public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

			public bool Focus() => false;

			public void Unfocus()
			{
			}
		}

		internal sealed class StubViewInternal : IView
		{
			public IViewHandler? Handler { get; set; }

			IElementHandler? IElement.Handler
			{
				get => Handler;
				set => Handler = value as IViewHandler;
			}

			public IElement? Parent => null;

			public bool IsFocused { get; set; }

			public Visibility Visibility => Visibility.Visible;

			public double Opacity => 1;

			public Paint? Background => null;

			public IShape? Clip => null;

			public IShadow? Shadow => null;

			public bool InputTransparent => false;

			public bool IsEnabled => true;

			public double Width => 0;

			public double Height => 0;

			public double MinimumWidth => 0;

			public double MinimumHeight => 0;

			public double MaximumWidth => double.PositiveInfinity;

			public double MaximumHeight => double.PositiveInfinity;

			public Thickness Margin => Thickness.Zero;

			public Rect Frame { get; set; }

			public FlowDirection FlowDirection => FlowDirection.LeftToRight;

			public LayoutAlignment HorizontalLayoutAlignment => LayoutAlignment.Fill;

			public LayoutAlignment VerticalLayoutAlignment => LayoutAlignment.Fill;

			public Semantics? Semantics => null;

			public string AutomationId => string.Empty;

			public int ZIndex { get; set; }

			public Size DesiredSize => Size.Zero;

			public double AnchorX => 0.5;

			public double AnchorY => 0.5;

			public double Rotation => 0;

			public double RotationX => 0;

			public double RotationY => 0;

			public double Scale => 1;

			public double ScaleX => 1;

			public double ScaleY => 1;

			public double TranslationX => 0;

			public double TranslationY => 0;

			public Size Arrange(Rect bounds) => bounds.Size;

			public void InvalidateArrange()
			{
			}

			public void InvalidateMeasure()
			{
			}

			public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

			public bool Focus() => false;

			public void Unfocus()
			{
			}
		}
	}
}
