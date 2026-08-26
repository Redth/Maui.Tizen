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
		public void OrderByZIndexIsStableForEqualZIndexes()
		{
			var a = new StubView { ZIndex = 0 };
			var b = new StubView { ZIndex = 0 };
			var c = new StubView { ZIndex = 0 };
			var layout = new StubLayout(a, b, c);

			Assert.Equal(new IView[] { a, b, c }, layout.OrderByZIndex().ToArray());
		}

		[Fact]
		public void OrderByZIndexSortsAscending()
		{
			var high = new StubView { ZIndex = 10 };
			var low = new StubView { ZIndex = -5 };
			var mid = new StubView { ZIndex = 1 };
			var layout = new StubLayout(high, low, mid);

			Assert.Equal(new IView[] { low, mid, high }, layout.OrderByZIndex().ToArray());
		}

		[Fact]
		public void GetLayoutHandlerIndexReturnsMinusOneForEmptyLayout() =>
			Assert.Equal(-1, new StubLayout().GetLayoutHandlerIndex(new StubView()));

		[Fact]
		public void GetLayoutHandlerIndexReturnsMinusOneForForeignView()
		{
			var layout = new StubLayout(new StubView(), new StubView());

			Assert.Equal(-1, layout.GetLayoutHandlerIndex(new StubView()));
		}

		[Fact]
		public void GetLayoutHandlerIndexHandlesSingleChild()
		{
			var only = new StubView();
			var layout = new StubLayout(only);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(only));
		}

		[Fact]
		public void GetLayoutHandlerIndexRespectsDeclarationOrderForEqualZIndex()
		{
			var a = new StubView();
			var b = new StubView();
			var c = new StubView();
			var layout = new StubLayout(a, b, c);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(a));
			Assert.Equal(1, layout.GetLayoutHandlerIndex(b));
			Assert.Equal(2, layout.GetLayoutHandlerIndex(c));
		}

		[Fact]
		public void GetLayoutHandlerIndexAccountsForZIndex()
		{
			var back = new StubView { ZIndex = -1 };
			var middle = new StubView { ZIndex = 0 };
			var front = new StubView { ZIndex = 5 };

			// Declaration order deliberately differs from z-order.
			var layout = new StubLayout(front, middle, back);

			Assert.Equal(0, layout.GetLayoutHandlerIndex(back));
			Assert.Equal(1, layout.GetLayoutHandlerIndex(middle));
			Assert.Equal(2, layout.GetLayoutHandlerIndex(front));
		}

		[Fact]
		public void ExtensionsRejectNullArguments()
		{
			Assert.Throws<ArgumentNullException>(() => ((ILayout)null!).OrderByZIndex());
			Assert.Throws<ArgumentNullException>(() => ((ILayout)null!).GetLayoutHandlerIndex(new StubView()));
			Assert.Throws<ArgumentNullException>(() => new StubLayout().GetLayoutHandlerIndex(null!));
		}

		sealed class StubLayout : ILayout
		{
			readonly List<IView> _children;

			public StubLayout(params IView[] children) => _children = children.ToList();

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

			public void Add(IView item) => _children.Add(item);

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

		sealed class StubView : IView
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
