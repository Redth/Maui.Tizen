using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Z-index helpers for <see cref="ILayout"/>.
	/// </summary>
	/// <remarks>
	/// Ported verbatim from <c>Microsoft.Maui.Handlers.LayoutExtensions</c> in dotnet/maui, which is
	/// <c>internal</c> and therefore unavailable to an out-of-repo backend. See docs/net11-status.md
	/// ("Required public MAUI API gaps").
	/// </remarks>
	public static class TizenLayoutExtensions
	{
		/// <summary>Orders the layout's children by their z-index, preserving declaration order.</summary>
		/// <param name="layout">The layout.</param>
		/// <returns>The children ordered by z-index.</returns>
		public static IOrderedEnumerable<IView> OrderByZIndex(this ILayout layout)
		{
			ArgumentNullException.ThrowIfNull(layout);

			return layout.OrderBy(v => v.ZIndex);
		}

		/// <summary>
		/// Computes the index at which a child's platform view should sit inside the platform
		/// container, accounting for z-index.
		/// </summary>
		/// <param name="layout">The layout.</param>
		/// <param name="view">The child view.</param>
		/// <returns>The platform child index, or <c>-1</c> when the view is not a child.</returns>
		public static int GetLayoutHandlerIndex(this ILayout layout, IView view)
		{
			ArgumentNullException.ThrowIfNull(layout);
			ArgumentNullException.ThrowIfNull(view);

			var count = layout.Count;
			switch (count)
			{
				case 0:
					return -1;
				case 1:
					return view == layout[0] ? 0 : -1;
				default:
					var found = false;
					var zIndex = view.ZIndex;
					var lowerViews = 0;

					for (var i = 0; i < count; i++)
					{
						var child = layout[i];
						var childZIndex = child.ZIndex;

						if (child == view)
							found = true;

						if (childZIndex < zIndex || (!found && childZIndex == zIndex))
							++lowerViews;
					}

					return found ? lowerViews : -1;
			}
		}
	}
}
