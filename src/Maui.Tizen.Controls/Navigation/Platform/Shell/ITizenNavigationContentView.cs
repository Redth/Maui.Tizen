using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Interface for the navigation drawer's main content area with a title view.
	/// </summary>
	public interface ITizenNavigationContentView
	{
		/// <summary>
		/// Gets the underlying platform view.
		/// </summary>
		NView? TargetView { get; }

		/// <summary>
		/// Gets or sets the title view (toolbar) area.
		/// </summary>
		NView? TitleView { get; set; }

		/// <summary>
		/// Gets or sets the main content area.
		/// </summary>
		NView? Content { get; set; }
	}
}
