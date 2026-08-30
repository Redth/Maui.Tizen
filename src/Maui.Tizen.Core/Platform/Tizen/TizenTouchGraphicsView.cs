// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/PlatformTouchGraphicsView.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using System;
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI.GraphicsView;
using PointStateType = Tizen.NUI.PointStateType;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenTouchGraphicsView : SkiaGraphicsView
	{
		IGraphicsView? _graphicsView;
		RectF _bounds;
		bool _dragStarted;
		PointF[] _lastMovedViewPoints = Array.Empty<PointF>();
		bool _pressedContained;

		public TizenTouchGraphicsView(IDrawable? drawable = null) : base(drawable)
		{
			HoverEvent += OnHoverEvent;
			TouchEvent += OnTouchEvent;
		}

		protected override void OnResized()
		{
			base.OnResized();
			_bounds = new RectF(0, 0, SizeWidth.ToScaledDP(), SizeHeight.ToScaledDP());
		}

		public void Connect(IGraphicsView graphicsView) => _graphicsView = graphicsView;

		public void Disconnect() => _graphicsView = null;

		bool OnTouchEvent(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			int touchCount = (int)e.Touch.GetPointCount();
			var touchPoints = new PointF[touchCount];
			for (uint i = 0; i < touchCount; i++)
				touchPoints[i] = new PointF(e.Touch.GetLocalPosition(i).X.ToScaledDP(), e.Touch.GetLocalPosition(i).Y.ToScaledDP());

			switch (e.Touch.GetState(0))
			{
				case PointStateType.Motion:
					TouchesMoved(touchPoints);
					break;
				case PointStateType.Down:
					TouchesBegan(touchPoints);
					break;
				case PointStateType.Up:
					TouchesEnded(touchPoints);
					break;
				case PointStateType.Interrupted:
					TouchesCanceled();
					break;
			}

			return false;
		}

		bool OnHoverEvent(object source, global::Tizen.NUI.BaseComponents.View.HoverEventArgs e)
		{
			int touchCount = (int)e.Hover.GetPointCount();
			var touchPoints = new PointF[touchCount];
			for (uint i = 0; i < touchCount; i++)
				touchPoints[i] = new PointF(e.Hover.GetLocalPosition(i).X.ToScaledDP(), e.Hover.GetLocalPosition(i).Y.ToScaledDP());

			switch (e.Hover.GetState(0))
			{
				case PointStateType.Motion:
					_graphicsView?.MoveHoverInteraction(touchPoints);
					break;
				case PointStateType.Started:
					_graphicsView?.StartHoverInteraction(touchPoints);
					break;
				case PointStateType.Finished:
					_graphicsView?.EndHoverInteraction();
					break;
			}

			return false;
		}

		void TouchesBegan(PointF[] points)
		{
			_dragStarted = false;
			_lastMovedViewPoints = points;
			_graphicsView?.StartInteraction(points);
			_pressedContained = true;
		}

		void TouchesMoved(PointF[] points)
		{
			if (!_dragStarted)
			{
				if (points.Length == 1)
				{
					float deltaX = _lastMovedViewPoints[0].X - points[0].X;
					float deltaY = _lastMovedViewPoints[0].Y - points[0].Y;

					if (MathF.Abs(deltaX) <= 3 && MathF.Abs(deltaY) <= 3)
						return;
				}
			}

			_lastMovedViewPoints = points;
			_dragStarted = true;
			_pressedContained = _bounds.ContainsAny(points);
			_graphicsView?.DragInteraction(points);
		}

		void TouchesEnded(PointF[] points)
		{
			_graphicsView?.EndInteraction(points, _pressedContained);
		}

		void TouchesCanceled()
		{
			_pressedContained = false;
			_graphicsView?.CancelInteraction();
		}
	}
}
