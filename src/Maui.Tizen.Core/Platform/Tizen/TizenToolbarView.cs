using System;
using System.Linq;
using Microsoft.Maui;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.Common.GraphicsView;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NShadow = Tizen.NUI.Shadow;
using MaterialIconButton = Tizen.UIExtensions.NUI.GraphicsView.MaterialIconButton;
using NVector2 = Tizen.NUI.Vector2;
using TColor = Tizen.UIExtensions.Common.Color;

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
		readonly View _contentHost;
		readonly TizenContentSlot<View> _contentSlot = new();

		/// <summary>Initializes a new instance of the <see cref="TizenToolbarView"/> class.</summary>
		public TizenToolbarView()
		{
			BoxShadow = new NShadow(5d.ToScaledPixel(), NColor.Black, new NVector2(0, 0));
			Label.FontSize = ToolbarTextSize.ToScaledPoint();
			SizeHeight = ToolbarHeight.ToScaledPixel();
			_contentHost = new View
			{
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.MatchParent,
			};
			_contentHost.Hide();
			base.Content = _contentHost;
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
			get => _contentSlot.Search;
			set
			{
				if (ReferenceEquals(_contentSlot.Search, value))
					return;

				UpdateContentSlot(_contentSlot.SetSearch(value));
			}
		}

		/// <summary>Updates the custom title content without displacing an active search view.</summary>
		public void SetTitleContent(View? content)
		{
			UpdateContentSlot(_contentSlot.SetTitle(content));
		}

		void UpdateContentSlot(TizenContentSlotChange<View> change)
		{
			if (change.Previous is not null
				&& !ReferenceEquals(change.Previous, change.Current)
				&& ReferenceEquals(change.Previous.GetParent(), _contentHost))
			{
				_contentHost.Remove(change.Previous);
			}

			var active = _contentSlot.Current;
			foreach (var child in _contentHost.Children.ToList())
			{
				if (!ReferenceEquals(child, active))
					_contentHost.Remove(child);
			}

			if (active is null)
			{
				_contentHost.Hide();
				Label.SizeWidth = SizeWidth;
				return;
			}

			if (!ReferenceEquals(active.GetParent(), _contentHost))
			{
				active.Unparent();
				_contentHost.Add(active);
			}
			_contentHost.Show();
			Label.SizeWidth = 0;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_contentSlot.Search?.Unparent();
				_contentSlot.Title?.Unparent();
				_contentSlot.Clear();
			}

			base.Dispose(disposing);
		}

		/// <summary>Gets the toolbar's expanded height, in scaled pixels.</summary>
		public static float ExpandedHeight => ToolbarHeight.ToScaledPixel();

		/// <summary>Restores the toolbar to its full height.</summary>
		public void Expand() => SizeHeight = ToolbarHeight.ToScaledPixel();

		/// <summary>Collapses the toolbar to zero height, hiding it without detaching it.</summary>
		public void Collapse() => SizeHeight = 0;

		/// <summary>Raises <see cref="IconPressed"/>.</summary>
		public void SendIconPressed() => IconPressed?.Invoke(this, EventArgs.Empty);

		/// <summary>Applies <see cref="IToolbar.Title"/>.</summary>
		/// <remarks>
		/// Ported from <c>Microsoft.Maui.Platform.ToolbarExtensions.UpdateTitle</c>. Declared as an
		/// instance method rather than an extension so it is unambiguous at the call site even if a
		/// consumer also has MAUI's <c>ToolbarExtensions</c> in scope.
		/// </remarks>
		/// <param name="toolbar">The cross-platform toolbar.</param>
		public void UpdateTitle(IToolbar toolbar)
		{
			ArgumentNullException.ThrowIfNull(toolbar);

			Title = toolbar.Title ?? string.Empty;
		}

		/// <summary>
		/// Installs the menu icon button, wired to raise <see cref="IconPressed"/>.
		/// </summary>
		/// <remarks>
		/// Ported from <c>ToolbarExtensions.UpdateMenuButton</c>. The icon colour is chosen from the
		/// toolbar background's grayscale value so it stays legible on either a light or dark bar.
		/// </remarks>
		/// <param name="toolbar">The cross-platform toolbar.</param>
		public void UpdateMenuButton(IToolbar toolbar)
		{
			var button = new MaterialIconButton
			{
				Icon = MaterialIcons.Menu,
				Color = GetAccentColor(),
			};

			button.Clicked += (_, _) => SendIconPressed();
			Icon = button;
		}

		TColor GetAccentColor()
		{
			var grayscale = (BackgroundColor.R + BackgroundColor.G + BackgroundColor.B) / 3.0f;
			return grayscale > 0.5 ? TColor.Black : TColor.White;
		}
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
		/// <remarks>
		/// <para>
		/// This is a PUSH with ownership transfer. The container takes ownership of
		/// <paramref name="toolbar"/> and, when a different toolbar replaces it, both removes and
		/// <see cref="IDisposable.Dispose"/>s the previous one. Callers must not keep using a
		/// toolbar they have handed over, and must not dispose it themselves.
		/// </para>
		/// <para>
		/// Because of that, callers must unsubscribe from the outgoing toolbar's events BEFORE
		/// replacing it, and subscribe to the incoming one afterwards. A caller that caches a
		/// toolbar it pulled earlier will be holding a disposed instance.
		/// </para>
		/// <para>
		/// Passing the toolbar that is already attached is a no-op and is explicitly safe: it does
		/// not dispose and re-add, which would leave a disposed native view in the tree. That makes
		/// "ensure the toolbar is attached" callers idempotent.
		/// </para>
		/// </remarks>
		void SetToolbar(TizenToolbarView toolbar);

		/// <summary>Detaches and disposes the toolbar currently owned by the container.</summary>
		void ClearToolbar();

		/// <summary>Detaches <paramref name="toolbar"/> without disposing it for an ownership transfer.</summary>
		void DetachToolbar(TizenToolbarView toolbar);
	}
}
