using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// A pseudo-element representing the "More" tab in the bottom tab bar.
	/// </summary>
	/// <remarks>
	/// When more shell items exist than can fit in the tab bar, they are grouped under a "More" item
	/// that opens a popup to select from them.
	/// </remarks>
	internal class TizenMoreItem : BaseShellItem
	{
		public TizenMoreItem()
		{
			Title = "More";
		}
	}
}
