using System;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NShadow = Tizen.NUI.Shadow;
using NVector2 = Tizen.NUI.Vector2;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The NUI toolbar surface used by Tizen page and navigation handlers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.MauiToolbar</c> in dotnet/maui. Behaviour is
	/// preserved; only the owning type and namespace changed, so this package does not need the
	/// raw imported <c>MauiToolbar</c> compiled alongside it.
	/// </para>
	/// <para>
	/// Named <c>TizenToolbarView</c> rather than <c>MauiToolbar</c> so it cannot collide (CS0433)
	/// with the <c>net*-tizen</c> build of <c>Microsoft.Maui.dll</c>, which still exports its own
	/// <c>Microsoft.Maui.Platform.MauiToolbar</c>.
	/// </para>
	/// </remarks>
	public class TizenToolbarView : TitleView
	{
		const double ToolbarTextSize = 20d;
		const double ToolbarHeight = 50d;

		/// <summary>Initializes a new instance of the <see cref="TizenToolbarView"/> class.</summary>
		public TizenToolbarView()
		{
			BoxShadow = new NShadow(5d.ToScaledPixel(), NColor.Black, new NVector2(0, 0));
			Label.FontSize = ToolbarTextSize.ToScaledPoint();
			SizeHeight = ToolbarHeight.ToScaledPixel();
		}

		/// <summary>Raised when the toolbar icon is pressed.</summary>
		public event EventHandler? IconPressed;

		/// <summary>
		/// Gets or sets a search bar shown in place of the title.
		/// </summary>
		/// <remarks>
		/// <c>TitleView</c> cannot show a title and content at once, so assigning content collapses
		/// the label's width and clearing it restores it. dotnet/maui carries the same workaround
		/// and the same note that Tizen.UIExtensions should grow a real API for this.
		/// </remarks>
		public View? SearchBar
		{
			get => base.Content;
			set
			{
				base.Content = value;
				Label.SizeWidth = base.Content is null ? SizeWidth : 0;
			}
		}

		/// <summary>Gets the toolbar's expanded height, in scaled pixels.</summary>
		public static float ExpandedHeight => ToolbarHeight.ToScaledPixel();

		/// <summary>Restores the toolbar to its full height.</summary>
		public void Expand() => SizeHeight = ToolbarHeight.ToScaledPixel();

		/// <summary>Collapses the toolbar to zero height, hiding it without detaching it.</summary>
		public void Collapse() => SizeHeight = 0;

		/// <summary>Raises <see cref="IconPressed"/>.</summary>
		public void SendIconPressed() => IconPressed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Implemented by platform views that can host a <see cref="TizenToolbarView"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Platform.IToolbarContainer</c>. Kept in this file because it
	/// exists solely to describe how a toolbar is attached, and consumed by Wave C's toolbar and
	/// navigation handlers.
	/// </remarks>
	public interface ITizenToolbarContainer
	{
		/// <summary>Attaches a toolbar, replacing and disposing any previous one.</summary>
		/// <param name="toolbar">The toolbar to attach.</param>
		void SetToolbar(TizenToolbarView toolbar);
	}
}
