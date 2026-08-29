using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>Projects raw native indexes only after unselectable rows have been rejected.</summary>
	internal static class RawSelectionProjection
	{
		public static IReadOnlyList<object> ToItems(
			IEnumerable<int> rawIndexes,
			int count,
			Func<int, bool> isSelectable,
			Func<int, object?> getItem)
		{
			ArgumentNullException.ThrowIfNull(rawIndexes);
			ArgumentNullException.ThrowIfNull(isSelectable);
			ArgumentNullException.ThrowIfNull(getItem);

			return rawIndexes
				.Where(index => index >= 0 && index < count)
				.Where(isSelectable)
				.Select(getItem)
				.Where(item => item is not null)
				.Cast<object>()
				.ToList();
		}
	}
}
