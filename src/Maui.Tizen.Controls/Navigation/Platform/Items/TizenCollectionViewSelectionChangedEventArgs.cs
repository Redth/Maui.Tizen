using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Event arguments for collection view selection changes in the Tizen backend.
	/// </summary>
	public class TizenCollectionViewSelectionChangedEventArgs : EventArgs
	{
		/// <summary>
		/// Gets or sets the list of currently selected items.
		/// </summary>
		public IList<object>? SelectedItems { get; set; }

		public IReadOnlyList<int> SelectedIndexes { get; set; } = Array.Empty<int>();
	}
}
