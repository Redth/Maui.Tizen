// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Reproductions of MAUI helpers that are `internal` to Microsoft.Maui.Core and therefore
// unreachable from an out-of-repo backend. Kept apart from TizenWaveBInterop because these have NO
// Tizen.NUI dependency, which lets the host-side test project compile and EXECUTE them. The
// compiler cannot diff a reproduction against an inaccessible original, so behaviour has to be
// pinned by tests instead.

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
	}
}
