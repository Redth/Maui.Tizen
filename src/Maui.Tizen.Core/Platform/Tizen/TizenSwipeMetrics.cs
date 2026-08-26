// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Kept in its own file because it has NO Tizen.NUI dependency. That lets the same source be
// compiled into the host-side test project and actually EXECUTED, rather than only type-checked by
// the ref-pack lane. This is the code that decides which way a swipe opens, so it is worth proving
// rather than assuming.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Swipe metrics and direction maths.
	/// </summary>
	/// <remarks>
	/// Upstream these lived in <c>Microsoft.Maui.Platform.SwipeViewExtensions</c> and
	/// <c>Microsoft.Maui.SwipeDirectionHelper</c>, both of which are <see langword="internal"/> to
	/// <c>Microsoft.Maui.Core</c>. An out-of-repo backend cannot reach them, so the values and the
	/// direction calculation are reproduced here, deliberately verbatim: this code decides which way
	/// a swipe opens, and an approximation would be a behaviour change disguised as a port.
	/// </remarks>
	public static class TizenSwipeMetrics
	{
		/// <summary>Distance, in device-independent units, a swipe must travel before it opens.</summary>
		/// <remarks>Matches <c>SwipeViewExtensions.SwipeThreshold</c>.</remarks>
		public const double SwipeThreshold = 250;

		/// <summary>Default width, in device-independent units, of a single swipe item.</summary>
		/// <remarks>Matches <c>SwipeViewExtensions.SwipeItemWidth</c>.</remarks>
		public const double SwipeItemWidth = 100;

		/// <summary>Determines the swipe direction between two points.</summary>
		/// <param name="initialPoint">Where the gesture started.</param>
		/// <param name="endPoint">Where the gesture currently is.</param>
		/// <returns>The swipe direction. Never null: any movement resolves to a direction.</returns>
		/// <remarks>
		/// Angle-based, exactly as upstream. An earlier version of this port used a
		/// "larger delta wins" approximation and returned <see langword="null"/> below a minimum
		/// travel distance. Both were wrong: the caller stores this in a nullable field that upstream
		/// only ever clears explicitly, so returning null on a small drag left the gesture with no
		/// direction and the swipe never opened.
		/// </remarks>
		public static SwipeDirection GetSwipeDirection(Point initialPoint, Point endPoint)
		{
			var angle = GetAngleFromPoints(initialPoint.X, initialPoint.Y, endPoint.X, endPoint.Y);
			return GetSwipeDirectionFromAngle(angle);
		}

		/// <summary>Returns the angle, in degrees, of the vector from the first point to the second.</summary>
		internal static double GetAngleFromPoints(double x1, double y1, double x2, double y2)
		{
			double rad = Math.Atan2(y1 - y2, x2 - x1) + Math.PI;
			return (rad * 180 / Math.PI + 180) % 360;
		}

		/// <summary>Maps an angle in degrees onto a swipe direction.</summary>
		internal static SwipeDirection GetSwipeDirectionFromAngle(double angle)
		{
			if (IsAngleInRange(angle, 45, 135))
				return SwipeDirection.Up;

			if (IsAngleInRange(angle, 0, 45) || IsAngleInRange(angle, 315, 360))
				return SwipeDirection.Right;

			if (IsAngleInRange(angle, 225, 315))
				return SwipeDirection.Down;

			return SwipeDirection.Left;
		}

		/// <summary>Half-open range check: inclusive of <paramref name="init"/>, exclusive of <paramref name="end"/>.</summary>
		internal static bool IsAngleInRange(double angle, float init, float end) => angle >= init && angle < end;
	}
}
