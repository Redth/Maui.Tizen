using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;
using TButton = Tizen.UIExtensions.NUI.Button;

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
	public partial class TizenToolbarHandler : ElementHandler<Toolbar, TizenToolbarView>, IToolbarHandler
	{
		ITizenPlatformViewHandler? _titleViewHandler;
		TizenImageLoader<TizenImageSource> _titleIconLoader = new();
		readonly List<TizenImageLoader<TizenImageSource>> _actionIconLoaders = new();

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
		// as TizenToolbarView. Matching the shipping contract, not the behaviour baseline.
		object IToolbarHandler.PlatformView => PlatformView;

		protected override TizenToolbarView CreatePlatformElement() => new();

		protected override void ConnectHandler(TizenToolbarView platformView)
		{
			var replacement = new TizenImageLoader<TizenImageSource>();
			var outgoing = _titleIconLoader;
			_titleIconLoader = replacement;

			ExceptionSafeCleanup.Run(
				outgoing.Dispose,
				DisposeActionIconLoaders,
				() => base.ConnectHandler(platformView),
				() => platformView.IconPressed += OnIconPressed);
		}

		protected override void DisconnectHandler(TizenToolbarView platformView)
		{
			ExceptionSafeCleanup.Run(
				_titleIconLoader.Dispose,
				DisposeActionIconLoaders,
				() =>
				{
					if (platformView.HasBody())
						platformView.IconPressed -= OnIconPressed;
				},
				DisposeTitleView,
				() => base.DisconnectHandler(platformView));
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
			=> handler.UpdateNavigationIcon(toolbar);

		public static void MapBackButtonTitle(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.UpdateNavigationIcon(toolbar);

		public static void MapTitleIcon(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.UpdateNavigationIcon(toolbar);

		public static void MapTitleView(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.UpdateTitleView(toolbar);

		public static void MapToolbarItems(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.UpdateToolbarItems(toolbar);

		public static void MapBarBackground(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBarBackgroundColor(toolbar);

		public static void MapBarTextColor(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBarTextColor(toolbar);

		public static void MapIconColor(TizenToolbarHandler handler, Toolbar toolbar)
			=> handler.PlatformView.UpdateBarIconColor(toolbar.IconColor);

		/// <summary>
		/// Re-evaluates the leading icon; press handling checks BackButtonEnabled before navigating.
		/// </summary>
		/// <remarks>
		/// NUI has no separate disabled visual for the back glyph, but the interaction contract is
		/// still enforced by <see cref="OnIconPressed"/>.
		/// </remarks>
		public static void MapBackButtonEnabled(TizenToolbarHandler handler, Toolbar toolbar) =>
			handler.UpdateNavigationIcon(toolbar);

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
		bool GetDrawerToggleVisible(Toolbar toolbar, IFlyoutView? owner = null)
			=> ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, owner);

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

			global::Tizen.NUI.BaseComponents.View platformTitleView = titleView.ToPlatformView(mauiContext);
			_titleViewHandler = titleView.Handler as ITizenPlatformViewHandler;

			PlatformView.Title = string.Empty;
			PlatformView.Content = platformTitleView;
		}

		internal void UpdateNavigationIcon(Toolbar toolbar, IFlyoutView? owner = null)
		{
			var drawerToggleVisible = GetDrawerToggleVisible(toolbar, owner);
			var kind = TizenToolbarNavigationSlot.GetNavigationIconKind(toolbar, drawerToggleVisible);

			PlatformView.UpdateBackButton(toolbar, drawerToggleVisible);

			if (kind is TizenNavigationIconKind.BackButton or TizenNavigationIconKind.DrawerToggle)
			{
				ResetTitleIconLoader();
				return;
			}

			LoadTitleIcon(toolbar, drawerToggleVisible);
		}

		void LoadTitleIcon(Toolbar toolbar, bool drawerToggleVisible)
		{
			var source = toolbar.TitleIcon;
			var generation = TizenToolbarNavigationSlot.BeginNavigationIconUpdate(toolbar);
			var provider = MauiContext?.Services.GetService<IImageSourceServiceProvider>();
			var dispatcher = MauiContext?.Services.GetService<IDispatcher>();
			var target = PlatformView;
			var virtualView = VirtualView;
			Func<Action, Task> commitOnUiThread = action => DispatchAsync(dispatcher, action);

			_titleIconLoader.LoadAsync(
				source,
				(imageSource, token) => provider is null
					? Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null)
					: provider.GetTizenImageAsync(imageSource, token),
				commitOnUiThread,
				image =>
				{
					if (TizenToolbarNavigationSlot.IsCurrentTitleIconUpdate(
						toolbar, generation, source, drawerToggleVisible))
					{
						target.Icon = image?.ResourceUrl is { } url
							? new global::Tizen.UIExtensions.NUI.Image { ResourceUrl = url }
							: null;
					}
				},
				() => ReferenceEquals(toolbar.TitleIcon, source),
				() => ReferenceEquals(VirtualView, virtualView)
					&& ReferenceEquals(PlatformView, target))
				.FireAndForget(this);
		}

		void UpdateToolbarItems(Toolbar toolbar)
		{
			DisposeActionIconLoaders();
			PlatformView.UpdateMenuItems(toolbar, MauiContext, LoadActionIcon);
		}

		void LoadActionIcon(ImageSource source, TButton button)
		{
			var loader = new TizenImageLoader<TizenImageSource>();
			_actionIconLoaders.Add(loader);

			var provider = MauiContext?.Services.GetService<IImageSourceServiceProvider>();
			var dispatcher = MauiContext?.Services.GetService<IDispatcher>();
			var target = button.Icon;
			Func<Action, Task> commitOnUiThread = action => DispatchAsync(dispatcher, action);

			loader.LoadAsync(
				source,
				(imageSource, token) => provider is null
					? Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null)
					: provider.GetTizenImageAsync(imageSource, token),
				commitOnUiThread,
				image => target.ResourceUrl = image?.ResourceUrl ?? string.Empty,
				() => true,
				() => _actionIconLoaders.Contains(loader))
				.FireAndForget(this);
		}

		void ResetTitleIconLoader()
		{
			var replacement = new TizenImageLoader<TizenImageSource>();
			var outgoing = _titleIconLoader;
			_titleIconLoader = replacement;
			ExceptionSafeCleanup.Run(outgoing.Dispose);
		}

		void DisposeActionIconLoaders()
		{
			List<Exception>? errors = null;
			foreach (var loader in _actionIconLoaders)
			{
				try
				{
					loader.Dispose();
				}
				catch (Exception ex)
				{
					(errors ??= new()).Add(ex);
				}
			}

			_actionIconLoaders.Clear();

			if (errors is { Count: > 0 })
				throw new AggregateException(errors);
		}

		void DisposeTitleView()
		{
			_titleViewHandler?.Dispose();
			_titleViewHandler = null;
		}

		static Task DispatchAsync(IDispatcher? dispatcher, Action action)
		{
			if (dispatcher is null || !dispatcher.IsDispatchRequired)
			{
				action();
				return Task.CompletedTask;
			}

			return dispatcher.DispatchAsync(action);
		}

		async void OnIconPressed(object? sender, EventArgs args)
		{
			if (ToolbarDrawerToggle.ShouldNavigateBack(VirtualView))
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
