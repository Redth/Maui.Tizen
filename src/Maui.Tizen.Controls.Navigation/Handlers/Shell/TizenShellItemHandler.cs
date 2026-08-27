using System;
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

		protected override TizenShellItemView CreatePlatformElement()
			=> new TizenShellItemView(VirtualView, MauiContext!);

		protected override void ConnectHandler(TizenShellItemView platformView)
		{
			var shell = VirtualView.Parent as Shell;
			if (shell != null)
			{
				((IShellController)shell).AddAppearanceObserver(this, (Element)VirtualView);
			}
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenShellItemView platformView)
		{
			var shell = VirtualView.Parent as Shell;
			if (shell != null)
			{
				((IShellController)shell).RemoveAppearanceObserver(this);
			}
			base.DisconnectHandler(platformView);
		}

		public static void MapTabBarIsVisible(TizenShellItemHandler handler, ShellItem item)
		{
			handler.PlatformView.UpdateTabBar(Shell.GetTabBarIsVisible(item));
		}

		public static void MapCurrentItem(TizenShellItemHandler handler, ShellItem item)
		{
			if (item.CurrentItem != null)
				handler.PlatformView.UpdateCurrentItem(item.CurrentItem);
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					var platformView = PlatformView;
					foreach (var item in VirtualView.Items)
					{
						if (item.Handler is IDisposable thandler)
						{
							thandler.Dispose();
						}
					}

					var shell = VirtualView.FindParentOfType<Shell>();
					if (shell != null)
					{
						((IShellController)shell).RemoveAppearanceObserver(this);
					}

					(this as IElementHandler)?.DisconnectHandler();
					platformView?.Dispose();
				}

				_disposedValue = true;
			}
		}

		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			if (appearance != null)
			{
				var shellView = VirtualView?.FindParentOfType<Shell>()?.Handler?.PlatformView as TizenShellView;
				shellView?.UpdateToolbarColors(appearance.ForegroundColor, appearance.BackgroundColor, appearance.TitleColor);
			}

			if (appearance is IShellAppearanceElement shellAppearance)
			{
				var tabBarBackgroundColor = shellAppearance.EffectiveTabBarBackgroundColor;
				var tabBarTitleColor = shellAppearance.EffectiveTabBarTitleColor;
				var tabBarUnselectedColor = shellAppearance.EffectiveTabBarUnselectedColor;

				// Tab bar colors are handled through appearance observer in platform view
			}
		}
	}
}
