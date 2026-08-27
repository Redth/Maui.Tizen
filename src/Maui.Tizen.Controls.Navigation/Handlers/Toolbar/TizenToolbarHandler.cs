using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="Toolbar"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The in-tree backend split this across three files: the neutral
	/// <c>ToolbarHandler</c>, a Tizen partial that added platform behaviour, and a
	/// <c>Toolbar.Tizen.cs</c> partial on the Controls type that installed Controls-level mappings
	/// through the internal <c>RemapForControls</c> hook.
	/// </para>
	/// <para>
	/// Out-of-tree none of that is reachable, so this handler declares one complete mapper over the
	/// concrete <see cref="Toolbar"/> type. Registering it with
	/// <c>AddHandler&lt;Toolbar, TizenToolbarHandler&gt;()</c> replaces the neutral handler outright
	/// rather than mutating its static mapper, which also removes a long-standing ordering hazard:
	/// <c>RemapForControls</c> had to run before the first handler was constructed.
	/// </para>
	/// </remarks>
	public partial class TizenToolbarHandler : ElementHandler<Toolbar, MauiToolbar>, IToolbarHandler
	{
		ITizenPlatformViewHandler? _titleViewHandler;

		public static IPropertyMapper<Toolbar, TizenToolbarHandler> Mapper =
			new PropertyMapper<Toolbar, TizenToolbarHandler>(ElementMapper)
			{
				[nameof(IToolbar.Title)] = MapTitle,
				[nameof(IToolbar.IsVisible)] = MapIsVisible,
				[nameof(IToolbar.BackButtonVisible)] = MapBackButtonVisible,
				[nameof(Toolbar.TitleIcon)] = MapTitleIcon,
				[nameof(Toolbar.TitleView)] = MapTitleView,
				[nameof(Toolbar.IconColor)] = MapIconColor,
				[nameof(Toolbar.ToolbarItems)] = MapToolbarItems,
				[nameof(Toolbar.BackButtonTitle)] = MapBackButtonTitle,

				// Upstream dotnet/maui#37863 adds Toolbar.MapDrawerToggleVisible for the additive
				// IToolbarDrawerToggleVisible capability. A literal rather than a constant reference so
				// the parity generator can extract the key; the string is identical either way, so this
				// becomes nameof(IToolbarDrawerToggleVisible.DrawerToggleVisible) on adoption.
				["DrawerToggleVisible"] = MapDrawerToggleVisible,
				[nameof(Toolbar.BarBackground)] = MapBarBackground,
				[nameof(Toolbar.BarTextColor)] = MapBarTextColor,
				[nameof(Toolbar.BackButtonEnabled)] = MapBackButtonEnabled,
				[nameof(Toolbar.DynamicOverflowEnabled)] = MapDynamicOverflowEnabled,
			};

		public static CommandMapper<Toolbar, TizenToolbarHandler> CommandMapper =
			new(ElementCommandMapper);

		public TizenToolbarHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenToolbarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IToolbar IToolbarHandler.VirtualView => VirtualView;

		// net11 declares IToolbarHandler.PlatformView as `object`; the 9.0.120 Tizen build typed it
		// as MauiToolbar. Matching the shipping contract, not the behaviour baseline.
		object IToolbarHandler.PlatformView => PlatformView;

		protected override MauiToolbar CreatePlatformElement() => new();

		protected override void ConnectHandler(MauiToolbar platformView)
		{
			platformView.IconPressed += OnIconPressed;
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(MauiToolbar platformView)
		{
			platformView.IconPressed -= OnIconPressed;

			// The title view is owned by this handler, not by the toolbar, so it has to be torn
			// down here or its platform view outlives the toolbar it was parented to.
			_titleViewHandler?.Dispose();
			_titleViewHandler = null;

			base.DisconnectHandler(platformView);
		}

		public static void MapTitle(TizenToolbarHandler handler, Toolbar toolbar)
		{
			// A title view, when present, owns the title slot; re-applying the text would draw
			// both.
			if (toolbar.TitleView is null)
			{
				handler.PlatformView.UpdateTitle(toolbar);
			}
		}

		public static void MapIsVisible(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateIsVisible(toolbar);

		public static void MapBackButtonVisible(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBackButton(toolbar, handler.GetDrawerToggleVisible(toolbar));

		public static void MapBackButtonTitle(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBackButton(toolbar, handler.GetDrawerToggleVisible(toolbar));

		public static void MapTitleIcon(TizenToolbarHandler handler, Toolbar toolbar)
		{
			if (handler.MauiContext is { } mauiContext)
			{
				handler.PlatformView.UpdateTitleIcon(toolbar, mauiContext, handler.GetDrawerToggleVisible(toolbar));
			}
		}

		/// <summary>
		/// Redraws the leading icon when the drawer-toggle capability changes.
		/// </summary>
		/// <remarks>
		/// The capability is read-only, so this maps a notification rather than applying a value.
		/// Rendering uses back-precedence: <see cref="IToolbar.BackButtonVisible"/> wins, and the
		/// drawer toggle is drawn only when no back button is showing. The capability itself stays
		/// true while a back button is up - the two are not mutually exclusive.
		/// </remarks>
		public static void MapDrawerToggleVisible(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBackButton(toolbar, handler.GetDrawerToggleVisible(toolbar));

		public static void MapTitleView(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.UpdateTitleView(toolbar);

		public static void MapToolbarItems(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateMenuItems(toolbar, handler.MauiContext);

		public static void MapBarBackground(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBarBackgroundColor(toolbar);

		public static void MapBarTextColor(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBarTextColor(toolbar);

		public static void MapIconColor(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBarIconColor(toolbar.IconColor);

		/// <summary>
		/// No-op: Tizen's toolbar icon has no separate enabled state.
		/// </summary>
		/// <remarks>
		/// The in-tree backend simply had no mapping, which meant a silent miss. Declaring it as an
		/// explicit no-op keeps <c>Parity/MapperParity.json</c> honest and gives the source tests
		/// something to assert against.
		/// </remarks>
		public static void MapBackButtonEnabled(TizenToolbarHandler handler, Toolbar toolbar)
		{
		}

		/// <summary>
		/// No-op: DynamicOverflowEnabled has no effect because Tizen always collapses secondary
		/// toolbar items behind the overflow button; there is no fixed-overflow mode to switch to.
		/// </summary>
		public static void MapDynamicOverflowEnabled(TizenToolbarHandler handler, Toolbar toolbar)
		{
		}

		/// <summary>
		/// Reads the drawer-toggle capability for this handler's toolbar.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Resolved from the toolbar's owning page, NOT from this handler's virtual view. The
		/// virtual view IS the toolbar, so an earlier <c>VirtualView as IFlyoutView</c> never
		/// matched and the capability was permanently false - which showed up as a shell popping
		/// back to its root and rendering an empty navigation slot instead of the hamburger, with no
		/// flyout-behaviour change to explain it.
		/// </para>
		/// <para>
		/// On adoption this collapses to a pattern match on the toolbar alone.
		/// </para>
		/// </remarks>
		bool GetDrawerToggleVisible(Toolbar toolbar)
			=> ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner: null);

		void UpdateTitleView(Toolbar toolbar)
		{
			IMauiContext mauiContext = MauiContext
				?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by the base class.");

			if (_titleViewHandler is not null)
			{
				PlatformView.Content = null;
				_titleViewHandler.Dispose();
				_titleViewHandler = null;
			}

			if (toolbar.TitleView is not VisualElement titleView)
			{
				PlatformView.UpdateTitle(toolbar);
				return;
			}

			global::Tizen.NUI.BaseComponents.View platformTitleView = titleView.ToPlatform(mauiContext);
			_titleViewHandler = titleView.Handler as ITizenPlatformViewHandler;

			PlatformView.Title = string.Empty;
			PlatformView.Content = platformTitleView;
		}

		async void OnIconPressed(object? sender, EventArgs args)
		{
			if (VirtualView is { BackButtonVisible: true, IsVisible: true })
			{
				// Delay so that other handlers attached to the same press (for example a
				// FlyoutPage's own back handling) observe it before the pop happens.
				await Task.Delay(100).ConfigureAwait(true);

				// The in-tree backend went through the internal MauiContext.GetPlatformWindow()
				// bridge. The owning window is reachable from the toolbar's parent chain and
				// IWindow.BackButtonClicked() is public, so no internal bridge is needed.
				VirtualView.FindParentOfType<IWindow>()?.BackButtonClicked();
			}
		}
	}
}
