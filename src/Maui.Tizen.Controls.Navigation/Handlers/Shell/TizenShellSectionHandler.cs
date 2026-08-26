using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="ShellSection"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Manages the top tab bar and navigation stack for a shell section. Implements
	/// <see cref="IAppearanceObserver"/> to respond to shell appearance changes.
	/// </para>
	/// </remarks>
	public partial class TizenShellSectionHandler : ElementHandler<ShellSection, TizenShellSectionStackManager>,
		IAppearanceObserver, IDisposable
	{
		bool _disposedValue;
		Page? _dummyPage;

		public static PropertyMapper<ShellSection, TizenShellSectionHandler> Mapper =
			new PropertyMapper<ShellSection, TizenShellSectionHandler>(ElementMapper)
			{
				[nameof(ShellSection.CurrentItem)] = MapCurrentItem,
			};

		public static CommandMapper<ShellSection, TizenShellSectionHandler> CommandMapper =
			new CommandMapper<ShellSection, TizenShellSectionHandler>(ElementCommandMapper)
			{
				[nameof(IStackNavigation.RequestNavigation)] = RequestNavigation,
			};

		public TizenShellSectionHandler() : base(Mapper, CommandMapper)
		{
		}

		public TizenShellSectionHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		~TizenShellSectionHandler()
		{
			Dispose(disposing: false);
		}

		protected override TizenShellSectionStackManager CreatePlatformElement()
		{
			return new TizenShellSectionStackManager();
		}

		public static void MapCurrentItem(TizenShellSectionHandler handler, ShellSection item)
		{
			handler.SyncNavigationStack(animated: true);
		}

		public static void RequestNavigation(TizenShellSectionHandler handler, IStackNavigation view, object? arg3)
		{
			if (arg3 is NavigationRequest nr)
			{
				// Navigation through stack manager
				handler.PlatformView.RequestNavigation(nr);
			}
			else
			{
				throw new InvalidOperationException("Args must be NavigationRequest");
			}
		}

		protected override void ConnectHandler(TizenShellSectionStackManager platformView)
		{
			platformView.Connect(VirtualView);

			// Subscribe to navigation events to honour animated flag
			((IShellSectionController)VirtualView).NavigationRequested += OnNavigationRequested;

			var shell = VirtualView.FindParentOfType<Shell>();
			if (shell != null)
			{
				((IShellController)shell).AddAppearanceObserver(this, (Element)VirtualView);
			}

			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenShellSectionStackManager platformView)
		{
			((IShellSectionController)VirtualView).NavigationRequested -= OnNavigationRequested;

			var shell = VirtualView.FindParentOfType<Shell>();
			if (shell != null)
			{
				((IShellController)shell).RemoveAppearanceObserver(this);
			}

			base.DisconnectHandler(platformView);
		}

		void OnNavigationRequested(object? sender, Microsoft.Maui.Controls.Internals.NavigationRequestedEventArgs e)
		{
			SyncNavigationStack(e.Animated);
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

					((IShellSectionController)VirtualView).NavigationRequested -= OnNavigationRequested;

					var shell = VirtualView.FindParentOfType<Shell>();
					if (shell != null)
					{
						((IShellController)shell).RemoveAppearanceObserver(this);
					}

					(this as IElementHandler)?.DisconnectHandler();
				}

				_disposedValue = true;
			}
		}

		/// <summary>
		/// Synchronizes the platform navigation stack with the virtual view's navigation stack.
		/// </summary>
		/// <param name="animated">Whether the navigation should be animated.</param>
		void SyncNavigationStack(bool animated)
		{
			if (_dummyPage == null)
			{
				_dummyPage = new TizenDummyPage();
			}

			List<IView> pageStack = new List<IView>()
			{
				// Dummy root page to sync navigation stack
				_dummyPage
			};

			for (var i = 1; i < VirtualView.Navigation.NavigationStack.Count; i++)
			{
				pageStack.Add(VirtualView.Navigation.NavigationStack[i]);
			}

			(VirtualView as IStackNavigation).RequestNavigation(new NavigationRequest(pageStack, animated));
		}

		/// <summary>No-op: Top tab bar appearance is handled in the shell section view.</summary>
		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			// Top tab bar appearance is handled in the shell section view
		}
	}

	/// <summary>
	/// Dummy page used as root for navigation stack sync.
	/// </summary>
	internal class TizenDummyPage : Page
	{
	}
}
