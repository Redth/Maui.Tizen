// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using Microsoft.Maui.Graphics;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using SkiaGraphicsView = Tizen.UIExtensions.NUI.GraphicsView.SkiaGraphicsView;
using TSize = Tizen.UIExtensions.Common.Size;
using TTextAlignment = Tizen.UIExtensions.Common.TextAlignment;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The platform view for <c>SearchBar</c>: a rounded entry with a search affordance.
	/// </summary>
	/// <remarks>
	/// Tizen has no search control, so this composes a NUI entry with a Skia-drawn search icon.
	/// The icon is drawn rather than shipped as an image so it scales with the display density
	/// and tints without needing multiple assets.
	/// </remarks>
	public class TizenSearchBarView : ViewGroup, global::Tizen.UIExtensions.Common.IMeasurable
	{
		const double IconSize = 24;
		const double IconMargin = 8;
		const double CornerRadiusDp = 8;

		static readonly NColor DefaultBackground = new(0.9f, 0.9f, 0.9f, 1);

		readonly Entry _entry;
		readonly SkiaGraphicsView _searchButton;

		PointStateType _lastPointState;

		public TizenSearchBarView()
		{
			BackgroundColor = DefaultBackground;

			_entry = new Entry
			{
				Padding = new Extents(10),
				VerticalTextAlignment = TTextAlignment.Center,
			};
			_entry.KeyEvent += OnEntryKeyEvent;

			_searchButton = new SkiaGraphicsView
			{
				Focusable = true,
				Drawable = new SearchIcon(),
			};
			_searchButton.TouchEvent += OnSearchButtonTouchEvent;
			_searchButton.KeyEvent += OnSearchButtonKeyEvent;

			Children.Add(_entry);
			Children.Add(_searchButton);

			LayoutUpdated += OnLayoutUpdated;
		}

		/// <summary>The text field the search bar's text mappings write to.</summary>
		public Entry Entry => _entry;

		/// <summary>Raised when the user commits the query.</summary>
		public event EventHandler? SearchButtonPressed;

		/// <summary>
		/// Detaches the event handlers this control owns.
		/// </summary>
		/// <remarks>
		/// The handler cannot unsubscribe these itself: they are attached to children it does
		/// not own a reference to. Called from the handler's disconnect path so the control does
		/// not keep itself alive through its own children.
		/// </remarks>
		public void DisconnectEvents()
		{
			if (_entry.HasBody())
				_entry.KeyEvent -= OnEntryKeyEvent;

			if (_searchButton.HasBody())
			{
				_searchButton.TouchEvent -= OnSearchButtonTouchEvent;
				_searchButton.KeyEvent -= OnSearchButtonKeyEvent;
			}
		}

		protected override void OnEnabled(bool enabled)
		{
			base.OnEnabled(enabled);

			// The group's enabled state does not cascade to children in NUI.
			_entry.IsEnabled = enabled;
			_searchButton.IsEnabled = enabled;
		}

		public TSize Measure(double availableWidth, double availableHeight)
		{
			var minimumHeight = Math.Max(IconSize.ToScaledPixel() + IconMargin.ToScaledPixel(), _entry.PixelSize + 10);

			if (!string.IsNullOrEmpty(_entry.Text) || !string.IsNullOrEmpty(_entry.Placeholder))
				return new TSize(availableWidth, Math.Max(_entry.NaturalSize.Height, minimumHeight));

			return new TSize(Math.Max(_entry.PixelSize + 10, availableWidth), minimumHeight);
		}

		bool OnEntryKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (e.Key.IsAcceptKeyEvent())
			{
				SearchButtonPressed?.Invoke(this, EventArgs.Empty);
				return true;
			}

			return false;
		}

		/// <remarks>
		/// A press is only treated as a click when the down and up both landed on the button,
		/// so dragging off the icon cancels it the way a button should.
		/// </remarks>
		bool OnSearchButtonTouchEvent(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			var state = e.Touch.GetState(0);

			if (state == PointStateType.Up && _lastPointState == PointStateType.Down)
				SearchButtonPressed?.Invoke(this, EventArgs.Empty);

			_lastPointState = state;

			return state is PointStateType.Up or PointStateType.Down;
		}

		bool OnSearchButtonKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (e.Key.IsAcceptKeyEvent())
			{
				SearchButtonPressed?.Invoke(this, EventArgs.Empty);
				return true;
			}

			return false;
		}

		void OnLayoutUpdated(object? sender, global::Tizen.UIExtensions.Common.LayoutEventArgs e)
		{
			var margin = (float)IconMargin.ToScaledPixel();
			var halfMargin = margin / 2.0f;
			var iconSize = (float)IconSize.ToScaledPixel();
			var iconArea = iconSize + margin;

			CornerRadius = CornerRadiusDp.ToScaledPixel();

			_entry.Position = new Position(halfMargin, 0);
			_entry.SizeHeight = SizeHeight;
			_entry.SizeWidth = Math.Max(0, SizeWidth - iconArea - halfMargin);

			_searchButton.Position = new Position(_entry.SizeWidth + _entry.Position.X + halfMargin, (SizeHeight - iconSize) / 2.0f);
			_searchButton.SizeHeight = iconSize;
			_searchButton.SizeWidth = iconSize;
		}

		/// <summary>The Material "search" glyph, drawn as a path.</summary>
		sealed class SearchIcon : IDrawable
		{
			const string PathData =
				"M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z";

			public void Draw(ICanvas canvas, RectF dirtyRect)
			{
				canvas.SaveState();

				// The glyph is authored on a 24x24 grid; centre it in whatever we were given.
				canvas.Translate((dirtyRect.Width - (float)IconSize) / 2.0f, (dirtyRect.Height - (float)IconSize) / 2.0f);

				var path = new PathBuilder().BuildPath(PathData);
				canvas.FillColor = Colors.Black;
				canvas.FillPath(path);

				canvas.RestoreState();
			}
		}
	}
}
