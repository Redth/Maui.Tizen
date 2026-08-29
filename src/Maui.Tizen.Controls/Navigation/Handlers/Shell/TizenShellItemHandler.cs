using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="ShellItem"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Manages the bottom tab bar and the current shell section. Implements
	/// <see cref="IAppearanceObserver"/> to respond to shell appearance changes.
	/// </para>
	/// </remarks>
	public partial class TizenShellItemHandler : ElementHandler<ShellItem, TizenShellItemView>,
		IAppearanceObserver, IDisposable
	{
		bool _disposedValue;
		Shell? _observedShell;

		public static PropertyMapper<ShellItem, TizenShellItemHandler> Mapper =
			new PropertyMapper<ShellItem, TizenShellItemHandler>(ElementMapper)
			{
				[nameof(ShellItem.CurrentItem)] = MapCurrentItem,
				[Shell.TabBarIsVisibleProperty.PropertyName] = MapTabBarIsVisible,
			};

		public static CommandMapper<ShellItem, TizenShellItemHandler> CommandMapper =
			new CommandMapper<ShellItem, TizenShellItemHandler>(ElementCommandMapper);

		public TizenShellItemHandler() : base(Mapper, CommandMapper)
		{
		}

		public TizenShellItemHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		public override void SetVirtualView(IElement view)
		{
			DetachAppearanceObserver();
			if (((IElementHandler)this).PlatformView is TizenShellItemView platformView
				&& view is ShellItem shellItem)
			{
				platformView.Rebind(shellItem);
			}

			base.SetVirtualView(view);
			if (((IElementHandler)this).PlatformView is not null)
				AttachAppearanceObserver();
		}

		protected override TizenShellItemView CreatePlatformElement()
			=> new TizenShellItemView(VirtualView, MauiContext!);

		protected override void ConnectHandler(TizenShellItemView platformView)
		{
			base.ConnectHandler(platformView);
			try
			{
				AttachAppearanceObserver();

				platformView.UpdateTabBar(Shell.GetTabBarIsVisible(VirtualView));
				platformView.UpdateCurrentItem(VirtualView.CurrentItem);
			}
			catch
			{
				DetachAppearanceObserver();
				base.DisconnectHandler(platformView);
				throw;
			}
		}

		protected override void DisconnectHandler(TizenShellItemView platformView)
		{
			try
			{
				DetachAppearanceObserver();
			}
			finally
			{
				base.DisconnectHandler(platformView);
			}
		}

		public static void MapTabBarIsVisible(TizenShellItemHandler handler, ShellItem item)
		{
			handler.PlatformView.UpdateTabBar(Shell.GetTabBarIsVisible(item));
		}

		public static void MapCurrentItem(TizenShellItemHandler handler, ShellItem item)
		{
			handler.PlatformView.UpdateCurrentItem(item.CurrentItem);
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (_disposedValue)
				return;

			_disposedValue = true;
			if (!disposing)
				return;

			var platformView = PlatformView;
			var actions = VirtualView.Items
				.Select(item => item.Handler)
				.OfType<IDisposable>()
				.Select<IDisposable, Action>(handler => handler.Dispose)
				.ToList();
			actions.Add(DetachAppearanceObserver);
			actions.Add(() => (this as IElementHandler)?.DisconnectHandler());
			if (platformView is not null)
				actions.Add(platformView.Dispose);

			ExceptionSafeCleanup.Run(actions.ToArray());
		}

		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			if (appearance is null)
				return;

			var shellView = VirtualView?.FindParentOfType<Shell>()?.Handler?.PlatformView as TizenShellView;
			shellView?.UpdateToolbarColors(appearance.ForegroundColor, appearance.BackgroundColor, appearance.TitleColor);

			if (appearance is IShellAppearanceElement shellAppearance)
			{
				PlatformView?.UpdateBottomTabBarColors(
					shellAppearance.EffectiveTabBarBackgroundColor,
					shellAppearance.EffectiveTabBarTitleColor,
					shellAppearance.EffectiveTabBarUnselectedColor);
			}
		}

		void AttachAppearanceObserver()
		{
			var shell = VirtualView?.FindParentOfType<Shell>();
			if (ReferenceEquals(_observedShell, shell))
				return;

			DetachAppearanceObserver();
			_observedShell = shell;
			if (_observedShell is not null)
				((IShellController)_observedShell).AddAppearanceObserver(this, VirtualView);
		}

		void DetachAppearanceObserver()
		{
			if (_observedShell is null)
				return;

			((IShellController)_observedShell).RemoveAppearanceObserver(this);
			_observedShell = null;
		}
	}
}
