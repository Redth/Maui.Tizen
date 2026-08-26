using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Primitives;
using Font = Microsoft.Maui.Font;

namespace Maui.Tizen.Sample
{
	/// <summary>
	/// Shared <see cref="IView"/> boilerplate for the sample's Core-level views.
	/// </summary>
	/// <remarks>
	/// The sample deliberately targets MAUI <b>Core</b> rather than MAUI Controls so that it
	/// exercises exactly the vertical slice this backend implements - application, window, page,
	/// layout and label - with nothing else in the way.
	/// </remarks>
	public abstract class SampleView : IView
	{
		public IViewHandler? Handler { get; set; }

		IElementHandler? IElement.Handler
		{
			get => Handler;
			set => Handler = value as IViewHandler;
		}

		public IElement? Parent { get; set; }

		public bool IsFocused { get; set; }

		public virtual Visibility Visibility => Visibility.Visible;

		public virtual double Opacity => 1;

		public virtual Paint? Background => null;

		public virtual IShape? Clip => null;

		public virtual IShadow? Shadow => null;

		public virtual bool InputTransparent => false;

		public virtual bool IsEnabled => true;

		public virtual double Width => double.NaN;

		public virtual double Height => double.NaN;

		public virtual double MinimumWidth => Dimension.Unset;

		public virtual double MinimumHeight => Dimension.Unset;

		public virtual double MaximumWidth => Dimension.Maximum;

		public virtual double MaximumHeight => Dimension.Maximum;

		public virtual Thickness Margin => Thickness.Zero;

		public Rect Frame { get; set; }

		public virtual FlowDirection FlowDirection => FlowDirection.LeftToRight;

		public virtual LayoutAlignment HorizontalLayoutAlignment => LayoutAlignment.Fill;

		public virtual LayoutAlignment VerticalLayoutAlignment => LayoutAlignment.Fill;

		public virtual Semantics? Semantics => null;

		public virtual string AutomationId => string.Empty;

		public virtual int ZIndex => 0;

		public Size DesiredSize { get; protected set; }

		public virtual double AnchorX => 0.5;

		public virtual double AnchorY => 0.5;

		public virtual double Rotation => 0;

		public virtual double RotationX => 0;

		public virtual double RotationY => 0;

		public virtual double Scale => 1;

		public virtual double ScaleX => 1;

		public virtual double ScaleY => 1;

		public virtual double TranslationX => 0;

		public virtual double TranslationY => 0;

		public virtual Size Arrange(Rect bounds)
		{
			Frame = bounds;
			Handler?.PlatformArrange(bounds);
			return bounds.Size;
		}

		public virtual Size Measure(double widthConstraint, double heightConstraint)
		{
			DesiredSize = Handler?.GetDesiredSize(widthConstraint, heightConstraint) ?? Size.Zero;
			return DesiredSize;
		}

		public void InvalidateArrange()
		{
		}

		public void InvalidateMeasure() => Handler?.Invoke(nameof(IView.InvalidateMeasure));

		public bool Focus() => false;

		public void Unfocus()
		{
		}
	}

	/// <summary>A minimal <see cref="ILabel"/> for the sample.</summary>
	public class SampleLabel : SampleView, ILabel
	{
		public string Text { get; set; } = string.Empty;

		public Color? TextColor { get; set; }

		public Font Font { get; set; } = Font.Default;

		public double CharacterSpacing { get; set; }

		public TextAlignment HorizontalTextAlignment { get; set; } = TextAlignment.Center;

		public TextAlignment VerticalTextAlignment { get; set; } = TextAlignment.Center;

		public TextDecorations TextDecorations { get; set; } = TextDecorations.None;

		public double LineHeight { get; set; } = -1;

		public Thickness Padding => Thickness.Zero;
	}

	/// <summary>A minimal vertical stack <see cref="ILayout"/> for the sample.</summary>
	public class SampleStackLayout : SampleView, ILayout
	{
		readonly List<IView> _children = new();

		public IView this[int index]
		{
			get => _children[index];
			set => _children[index] = value;
		}

		public int Count => _children.Count;

		public bool IsReadOnly => false;

		public bool ClipsToBounds => true;

		public Thickness Padding { get; set; } = new(24);

		public double Spacing { get; set; } = 12;

		public bool IgnoreSafeArea => false;

		public void Add(IView item)
		{
			if (item is SampleView sampleView)
				sampleView.Parent = this;

			_children.Add(item);

			// Matches how MAUI Controls' Layout notifies its handler: by command-mapper key string.
			Handler?.Invoke("Add", new LayoutHandlerUpdate(_children.Count - 1, item));
		}

		public void Clear() => _children.Clear();

		public bool Contains(IView item) => _children.Contains(item);

		public void CopyTo(IView[] array, int arrayIndex) => _children.CopyTo(array, arrayIndex);

		public IEnumerator<IView> GetEnumerator() => _children.GetEnumerator();

		public int IndexOf(IView item) => _children.IndexOf(item);

		public void Insert(int index, IView item) => _children.Insert(index, item);

		public bool Remove(IView item) => _children.Remove(item);

		public void RemoveAt(int index) => _children.RemoveAt(index);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint)
		{
			var width = 0d;
			var height = Padding.VerticalThickness;

			for (var i = 0; i < _children.Count; i++)
			{
				var measured = _children[i].Measure(
					widthConstraint - Padding.HorizontalThickness,
					double.PositiveInfinity);

				width = Math.Max(width, measured.Width);
				height += measured.Height;

				if (i < _children.Count - 1)
					height += Spacing;
			}

			return new Size(width + Padding.HorizontalThickness, height);
		}

		public Size CrossPlatformArrange(Rect bounds)
		{
			var y = bounds.Y + Padding.Top;
			var x = bounds.X + Padding.Left;
			var width = bounds.Width - Padding.HorizontalThickness;

			foreach (var child in _children)
			{
				var height = child.DesiredSize.Height;
				child.Arrange(new Rect(x, y, width, height));
				y += height + Spacing;
			}

			return bounds.Size;
		}
	}

	/// <summary>A minimal page (<see cref="IContentView"/>) for the sample.</summary>
	public class SamplePage : SampleView, IContentView
	{
		public SamplePage(IView content)
		{
			Content = content;

			if (content is SampleView sampleView)
				sampleView.Parent = this;
		}

		public object? Content { get; }

		public IView? PresentedContent => Content as IView;

		public Thickness Padding => Thickness.Zero;

		public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
			PresentedContent?.Measure(widthConstraint, heightConstraint) ?? Size.Zero;

		public Size CrossPlatformArrange(Rect bounds)
		{
			PresentedContent?.Arrange(bounds);
			return bounds.Size;
		}
	}
}
