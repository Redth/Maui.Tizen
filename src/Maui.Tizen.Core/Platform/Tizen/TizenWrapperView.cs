// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/WrapperView.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using System;
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI.GraphicsView;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using NView = Tizen.NUI.BaseComponents.View;
using TRect = Tizen.UIExtensions.Common.Rect;
using TSize = Tizen.UIExtensions.Common.Size;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenWrapperView : ViewGroup, IMeasurable
	{
		Lazy<SkiaGraphicsView> _drawableCanvas;
		NView? _content;
		TizenDrawable _mauiDrawable;

		public TizenWrapperView() : base()
		{
			_mauiDrawable = new TizenDrawable();
			_drawableCanvas = new Lazy<SkiaGraphicsView>(() =>
			{
				var view = new SkiaGraphicsView()
				{
					Drawable = _mauiDrawable
				};
				view.Show();
				Children.Add(view);
				view.Lower();
				return view;
			});


			LayoutUpdated += OnLayout;
		}

		public NView? Content
		{
			get => _content;
			set
			{
				UpdateContent(value, _content);
				_content = value;
			}
		}

		bool NeedToUpdateCanvas => _drawableCanvas.IsValueCreated || _mauiDrawable.Background != null || _mauiDrawable.Shape != null || _mauiDrawable.Border != null || _mauiDrawable.Shadow != null;

		public void UpdateBackground(Paint? paint)
		{
			_mauiDrawable.Background = paint;
			UpdateDrawableCanvas();
		}

		public void UpdateShape(IShape? shape)
		{
			_mauiDrawable.Shape = shape;
			UpdateDrawableCanvas();
		}


		IShadow? _shadow;
		IShape? _clip;
		IBorderStroke? _border;

		/// <summary>Gets or sets the shadow drawn behind the wrapped content.</summary>
		public IShadow? Shadow
		{
			get => _shadow;
			set
			{
				_shadow = value;
				ShadowChanged();
			}
		}

		/// <summary>Gets or sets the clip shape applied to the wrapped content.</summary>
		public IShape? Clip
		{
			get => _clip;
			set
			{
				_clip = value;
				ClipChanged();
			}
		}

		/// <summary>Gets or sets the border stroke drawn around the wrapped content.</summary>
		public IBorderStroke? Border
		{
			get => _border;
			set
			{
				_border = value;
				BorderChanged();
			}
		}

		public void UpdateBorder(IBorderStroke? border)
		{
			Border = border;
		}

		void ShadowChanged()
		{
			_mauiDrawable.Shadow = Shadow;
			UpdateDrawableCanvas(true);
		}

		/// <summary>
		/// Records the clip on the drawable only.
		/// </summary>
		/// <remarks>
		/// UNSUPPORTED: upstream also drove a native clipper view, which cannot be built here.
		/// Its draw callback takes a <c>SkiaSharp.Views.Tizen.SKPaintSurfaceEventArgs</c>, and
		/// SkiaSharp.Views publishes tizen-only assets, so the type is unavailable to any lane that
		/// is not a full Tizen build. The raw MauiClipperView.cs import is retained beside this file
		/// for whoever restores it. See docs/wave-b-mapper-parity.md.
		/// </remarks>
		void ClipChanged()
		{
			_mauiDrawable.Clip = Clip;
		}

		void BorderChanged()
		{
			_mauiDrawable.Border = Border;
			UpdateShape(Border?.Shape);
			UpdateDrawableCanvas(Border != null);
		}

		void UpdateDrawableCanvas(bool geometryUpdate = false)
		{
			if (NeedToUpdateCanvas)
			{
				if (geometryUpdate)
					UpdateDrawableCanvasGeometry();
				_drawableCanvas.Value.Invalidate();
			}
		}

		void OnLayout(object? sender, LayoutEventArgs e)
		{
			Content?.UpdateBounds(new TRect(0, 0, Size.Width, Size.Height));


			UpdateDrawableCanvas(true);
		}


		void UpdateContent(NView? newValue, NView? oldValue)
		{
			// Upstream re-parented the content into the clipper view when one existed. Without a
			// clipper (see ClipChanged) the content is always a direct child of this view.
			if (oldValue != null)
			{
				Children.Remove(oldValue);
			}

			if (newValue != null)
			{
				Children.Add(newValue);
			}
		}

		void UpdateDrawableCanvasGeometry()
		{
			var bounds = new TRect(0, 0, SizeWidth, SizeHeight);
			if (Shadow != null)
			{
				var shadowThinkness = Shadow.GetShadowMargin();
				_mauiDrawable.ShadowThickness = shadowThinkness;
				bounds = bounds.ToDP().ExpandTo(shadowThinkness).ToPixel();
			}
			_drawableCanvas.Value.UpdateBounds(bounds);
		}

		TSize IMeasurable.Measure(double availableWidth, double availableHeight)
		{
			if (Content is IMeasurable measurable)
			{
				return measurable.Measure(availableWidth, availableHeight);
			}
			else if (Content != null)
			{
				return Content.NaturalSize2D.ToCommon();
			}
			else
			{
				return NaturalSize2D.ToCommon();
			}
		}
	}
}
