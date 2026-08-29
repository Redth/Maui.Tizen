// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/MauiPageControl.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI.GraphicsView;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using NExtents = Tizen.NUI.Extents;
using NLayoutParamPolicies = Tizen.NUI.BaseComponents.LayoutParamPolicies;
using NLinearLayout = Tizen.NUI.LinearLayout;
using NPointStateType = Tizen.NUI.PointStateType;
using NSize = Tizen.NUI.Size;
using NView = Tizen.NUI.BaseComponents.View;
using TSize = Tizen.UIExtensions.Common.Size;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenPageControl : ViewGroup, IMeasurable
	{
		const double DefaultMargin = 4;

		IIndicatorView _indicatorView;
		ITizenPlatformViewHandler? _templatedViewHandler;
		ILayout? _templatedView;
		NView? _contentView;
		long _templatedContentGeneration;

		List<Indicator> _indicators = new List<Indicator>();
		int _currentPoistion = -1;
		int _visibleWindowStart;

		public TizenPageControl(IIndicatorView view)
		{
			_indicatorView = view;
			LayoutUpdated += OnLayoutUpdated;
		}

		internal void Rebind(IIndicatorView view)
		{
			ArgumentNullException.ThrowIfNull(view);

			if (ReferenceEquals(_indicatorView, view))
				return;

			_indicatorView = view;
			ResetIndicators();
		}

		bool UseDefaultIndicator { get; set; }

		double IndicatorSizeWithMargin => IndicatorSize + DefaultMargin * 2;

		double IndicatorSize => _indicatorView.IndicatorSize;

		/// <summary>Disposes the handler created for a templated indicator.</summary>
		public void DisposeTemplatedViewHandler()
		{
			if (_templatedViewHandler is null)
				return;

			var operation = TizenContentOwnership.Reserve(ref _templatedContentGeneration);
			_templatedView = null;
			TizenContentOwnership.Clear(
				operation,
				ref _contentView,
				ref _templatedViewHandler,
				ref _templatedContentGeneration,
				view =>
				{
					Children.Remove(view);
					view.Unparent();
				},
				static () => { },
				static () => true);
		}

		public void ResetIndicators()
		{
			ClearIndicatorView();
			if ((_indicatorView as ITemplatedIndicatorView)?.IndicatorsLayoutOverride == null)
			{
				CreateDefaultView();
			}
			else
			{
				CreateTemplatedView();
			}

			// Rebuilding only restores APPEARANCE. Count, HideSingle visibility and the windowed
			// selection are state, and were lost every time an appearance mapper ran - so changing
			// the indicator colour or shape silently dropped the cap and the highlight until the
			// next unrelated position change happened to restore them. UpdateCount is a no-op for a
			// templated view, which owns its own layout.
			UpdateCount();

			this.InvalidateMeasure(_indicatorView);
		}

		public void UpdatePosition()
		{
			if (!UseDefaultIndicator)
				return;

			UpdateIndicatorColor(_currentPoistion, _indicatorView.IndicatorColor);
			var window = GetVisibleWindow();
			_visibleWindowStart = window.Start;
			_currentPoistion = window.SelectedIndex;
			UpdateIndicatorColor(_currentPoistion, _indicatorView.SelectedIndicatorColor);
		}

		/// <summary>
		/// Maps <see cref="IIndicatorView.Position"/> onto the index of the dot that represents it.
		/// </summary>
		/// <remarks>
		/// The number of dots is capped at <see cref="IIndicatorView.MaximumVisible"/>, but the
		/// position is not: with 20 items and a maximum of 5, position 10 indexed past the end of the
		/// dot list. <c>UpdateIndicatorColor</c> bounds-checks and returns silently, so the selected
		/// dot simply stopped being highlighted once the position exceeded the cap — the indicator
		/// looked stuck at the last dot it managed to draw.
		/// <para>
		/// The window slides so the selected item stays visible, which is the point of capping the
		/// dot count in the first place.
		/// </para>
		/// </remarks>
		TizenPortableExtensions.IndicatorWindow GetVisibleWindow() =>
			TizenPortableExtensions.GetIndicatorWindow(
				_indicatorView.Position,
				_indicatorView.Count,
				_indicators.Count);

		public void UpdateCount()
		{
			if (!UseDefaultIndicator || _contentView == null)
				return;

			// Visibility combines the HideSingle policy with the virtual view's own Visibility.
			// Showing on the policy alone re-reveals an indicator the app has hidden, because this
			// runs for every Count / MaximumVisible / appearance change.
			if (TizenPortableExtensions.IsIndicatorVisible(_indicatorView.Visibility, _indicatorView.HideSingle, _indicatorView.Count))
				Show();
			else
				Hide();

			var count = Math.Max(0, Math.Min(_indicatorView.Count, _indicatorView.MaximumVisible));
			var diff = _indicators.Count - count;
			var needIncrease = diff < 0;

			diff = Math.Abs(diff);
			for (int i = 0; i < diff; i++)
			{
				if (needIncrease)
					IncreaseIndicator();
				else
					DecreaseIndicator();
			}
			UpdatePosition();
		}

		public TSize Measure(double availableWidth, double availableHeight)
		{
			if (UseDefaultIndicator)
			{
				return new TSize(IndicatorSizeWithMargin.ToScaledPixel() * _indicators.Count, IndicatorSizeWithMargin.ToScaledPixel());
			}
			else
			{
				return _templatedView?.CrossPlatformMeasure(availableWidth.ToScaledDP(), availableHeight.ToScaledDP()).ToPixel() ?? new TSize(0, 0);
			}
		}

		void UpdateIndicatorColor(int position, Paint? color)
		{
			if (position < 0 || position >= _indicators.Count)
				return;

			_indicators[position].Drawable.Background = color;
			_indicators[position].Invalidate();
		}

		void CreateTemplatedView()
		{
			UseDefaultIndicator = false;
			var indicatorView = _indicatorView;
			var layout = (indicatorView as ITemplatedIndicatorView)?.IndicatorsLayoutOverride;
			if (layout == null || _indicatorView?.Handler?.MauiContext == null)
				return;

			var operation = TizenContentOwnership.Reserve(ref _templatedContentGeneration);
			var contentView = layout.ToPlatformView(_indicatorView.Handler.MauiContext);
			var contentHandler = layout.Handler as ITizenPlatformViewHandler;
			var installed = TizenContentOwnership.Replace(
				operation,
				ref _contentView,
				ref _templatedViewHandler,
				ref _templatedContentGeneration,
				contentView,
				contentHandler,
				view =>
				{
					Children.Remove(view);
					view.Unparent();
				},
				view =>
				{
					view.WidthSpecification = NLayoutParamPolicies.MatchParent;
					view.HeightSpecification = NLayoutParamPolicies.MatchParent;
					Children.Add(view);
				},
				static () => { },
				() =>
					ReferenceEquals(_indicatorView, indicatorView) &&
					ReferenceEquals(
						(_indicatorView as ITemplatedIndicatorView)?.IndicatorsLayoutOverride,
						layout));

			_templatedView = installed ? layout : null;
		}

		void CreateDefaultView()
		{
			UseDefaultIndicator = true;
			_contentView = new NView
			{
				WidthSpecification = NLayoutParamPolicies.MatchParent,
				HeightSpecification = NLayoutParamPolicies.MatchParent,
				Layout = new NLinearLayout
				{
					LinearOrientation = NLinearLayout.Orientation.Horizontal,
				}
			};
			Children.Add(_contentView);

			// Visibility is deliberately NOT decided here. Upstream returned early when HideSingle
			// applied, which duplicated - and then diverged from - the policy in UpdateCount: this
			// built a different number of dots than UpdateCount believed existed. ResetIndicators
			// runs UpdateCount straight after, so HideSingle and the cap are applied in exactly one
			// place.
			var count = Math.Max(0, Math.Min(_indicatorView.Count, _indicatorView.MaximumVisible));

			// The highlight must use the WINDOWED position, not the raw one. With 20 items, a
			// maximum of 5 and position 10, `i == Position` is never true, so a rebuild triggered by
			// an appearance change (colour, size, shape) left no dot highlighted at all.
			var window = TizenPortableExtensions.GetIndicatorWindow(
				_indicatorView.Position,
				_indicatorView.Count,
				count);
			_visibleWindowStart = window.Start;
			_currentPoistion = window.SelectedIndex;

			for (int i = 0; i < count; i++)
			{
				var indicator = CreateIndicator((i == _currentPoistion) ? _indicatorView.SelectedIndicatorColor : _indicatorView.IndicatorColor);
				_contentView.Add(indicator);
				_indicators.Add(indicator);
			}
		}

		Indicator CreateIndicator(Paint? color)
		{
			var indicator = new Indicator()
			{
				Margin = new NExtents((ushort)DefaultMargin.ToScaledPixel()),
				Size = new NSize(IndicatorSize.ToScaledPixel(), IndicatorSize.ToScaledPixel())
			};
			indicator.Drawable.Shape = _indicatorView.IndicatorsShape;
			indicator.Drawable.Background = color;
			indicator.TouchEvent += OnIndicatorTouch;

			return indicator;
		}

		void ClearIndicatorView()
		{
			var operation = TizenContentOwnership.Reserve(ref _templatedContentGeneration);
			var indicators = _indicators;
			_indicators = new List<Indicator>();
			_templatedView = null;

			var cleanup = new List<Action>
			{
				() => TizenContentOwnership.Clear(
					operation,
					ref _contentView,
					ref _templatedViewHandler,
					ref _templatedContentGeneration,
					view =>
					{
						Children.Remove(view);
						view.Unparent();
					},
					static () => { },
					static () => true),
			};

			foreach (var indicator in indicators)
				cleanup.Add(indicator.Dispose);

			TizenCleanup.Run(cleanup.ToArray());
		}

		void DecreaseIndicator()
		{
			if (_contentView == null)
				return;

			var indicator = _indicators[_indicators.Count - 1];
			_contentView.Remove(indicator);
			_indicators.Remove(indicator);
			indicator.Dispose();
		}

		void IncreaseIndicator()
		{
			if (_contentView == null)
				return;

			var window = TizenPortableExtensions.GetIndicatorWindow(
				_indicatorView.Position,
				_indicatorView.Count,
				Math.Min(_indicatorView.Count, _indicatorView.MaximumVisible));
			var indicator = CreateIndicator(
				_indicators.Count == window.SelectedIndex
					? _indicatorView.SelectedIndicatorColor
					: _indicatorView.IndicatorColor);
			_contentView.Add(indicator);
			_indicators.Add(indicator);
		}

		bool OnIndicatorTouch(object source, TouchEventArgs e)
		{
			if (e.Touch.GetState(0) == NPointStateType.Up && source is Indicator indicator)
			{
				var touchPosition = e.Touch.GetLocalPosition(0);
				if (0 < touchPosition.X && touchPosition.X < indicator.SizeWidth
					&& 0 < touchPosition.Y && touchPosition.Y < indicator.SizeHeight)
				{
					var position = _indicators.IndexOf(indicator);
					if (position != -1)
					{
						_indicatorView.Position = _visibleWindowStart + position;
					}
				}
			}
			return true;
		}

		void OnLayoutUpdated(object? sender, LayoutEventArgs e)
		{
			if (UseDefaultIndicator || _templatedView == null)
				return;

			var platformGeometry = this.GetBounds().ToDP();
			_templatedView.CrossPlatformMeasure(platformGeometry.Width, platformGeometry.Height);

			platformGeometry.X = 0;
			platformGeometry.Y = 0;
			_templatedView.CrossPlatformArrange(platformGeometry);
		}

		class Indicator : SkiaGraphicsView
		{
			public Indicator() : base(new TizenDrawable()) { }

			public new TizenDrawable Drawable => (TizenDrawable)base.Drawable!;
		}
	}
}
