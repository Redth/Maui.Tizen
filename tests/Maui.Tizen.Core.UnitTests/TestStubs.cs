using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using Font = Microsoft.Maui.Font;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>Minimal <see cref="IView"/> boilerplate shared by the tests.</summary>
	public abstract class StubView : IView
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

		public Paint? Background { get; set; }

		public IShape? Clip => null;

		public IShadow? Shadow => null;

		public bool InputTransparent => false;

		public bool IsEnabled => true;

		public double Width => double.NaN;

		public double Height => double.NaN;

		public double MinimumWidth => Dimension.Unset;

		public double MinimumHeight => Dimension.Unset;

		public double MaximumWidth => Dimension.Maximum;

		public double MaximumHeight => Dimension.Maximum;

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

	/// <summary>Minimal <see cref="ILabel"/> stub.</summary>
	public sealed class StubLabel : StubView, ILabel
	{
		public string Text { get; set; } = string.Empty;

		public Color TextColor { get; set; } = Colors.Black;

		public Font Font { get; set; } = Font.Default;

		public double CharacterSpacing { get; set; }

		public TextAlignment HorizontalTextAlignment { get; set; } = TextAlignment.Start;

		public TextAlignment VerticalTextAlignment { get; set; } = TextAlignment.Start;

		public TextDecorations TextDecorations { get; set; } = TextDecorations.None;

		public double LineHeight { get; set; } = -1;

		public Thickness Padding => Thickness.Zero;
	}
}
