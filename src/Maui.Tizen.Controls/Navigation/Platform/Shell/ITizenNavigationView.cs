using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Interface for the navigation drawer's flyout content area.
	/// </summary>
	public interface ITizenNavigationView
	{
		/// <summary>
		/// Gets the underlying platform view.
		/// </summary>
		NView? TargetView { get; }

		/// <summary>
		/// Gets or sets the header displayed at the top of the navigation view.
		/// </summary>
		NView? Header { get; set; }

		/// <summary>
		/// Gets or sets the main content area of the navigation view.
		/// </summary>
		NView? Content { get; set; }

		/// <summary>
		/// Gets or sets the footer displayed at the bottom of the navigation view.
		/// </summary>
		NView? Footer { get; set; }

		/// <summary>
		/// Gets or sets the background color of the navigation view.
		/// </summary>
		NColor? BackgroundColor { get; set; }
	}
}
