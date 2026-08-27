// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Reproductions of MAUI helpers that are `internal` to Microsoft.Maui.Core and therefore
// unreachable from an out-of-repo backend. Kept apart from TizenWaveBInterop because these have NO
// Tizen.NUI dependency, which lets the host-side test project compile and EXECUTE them. The
// compiler cannot diff a reproduction against an inaccessible original, so behaviour has to be
// pinned by tests instead.

using System;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Reproductions of MAUI helpers that are internal to Microsoft.Maui.Core.</summary>
	public static class TizenPortableExtensions
	{
		/// <summary>Converts a MAUI <see cref="Visibility"/> to a native shown/hidden flag.</summary>
		/// <remarks>
		/// Written as an explicit switch to match upstream, so a future <c>Visibility</c> member
		/// defaults to visible here exactly as it does there.
		/// </remarks>
		public static bool ToPlatformVisibility(this Visibility visibility) =>
			visibility switch
			{
				Visibility.Hidden => false,
				Visibility.Collapsed => false,
				_ => true,
			};

		/// <summary>Returns whether any of <paramref name="points"/> falls inside the rectangle.</summary>
		/// <remarks>Upstream used an internal <c>RectF.ContainsAny</c> helper.</remarks>
		public static bool ContainsAny(this RectF rect, PointF[] points) => points.Any(rect.Contains);
	
		/// <summary>
		/// Maps an indicator position onto the index of the dot that represents it.
		/// </summary>
		/// <param name="position">The selected position, which is NOT capped.</param>
		/// <param name="count">The total number of items.</param>
		/// <param name="visibleCount">The number of dots actually created.</param>
		/// <returns>The dot index to highlight, or -1 when nothing should be highlighted.</returns>
		/// <remarks>
		/// The dot count is capped at <c>MaximumVisible</c> but the position is not, so a position
		/// beyond the cap indexed past the end of the dot list. The native call bounds-checks and
		/// returns silently, so the selection simply stopped being drawn. The window slides to keep
		/// the selected item visible, which is the point of capping the dots at all.
		/// </remarks>
		public static int GetVisibleIndicatorPosition(int position, int count, int visibleCount)
		{
			if (visibleCount <= 0 || position < 0)
				return -1;

			if (count <= visibleCount)
				return Math.Min(position, visibleCount - 1);

			var maxStart = Math.Max(0, count - visibleCount);
			var start = Math.Clamp(position - visibleCount + 1, 0, maxStart);

			return Math.Clamp(position - start, 0, visibleCount - 1);
		}
	}
}
