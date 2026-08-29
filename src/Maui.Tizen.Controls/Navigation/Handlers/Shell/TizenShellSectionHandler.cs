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
		bool _rebinding;
		ShellSection? _observedSection;
		Shell? _observedShell;

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

		public override void SetVirtualView(IElement view)
		{
			var platformView = ((IElementHandler)this).PlatformView as TizenShellSectionStackManager;
			if (platformView is not null && view is ShellSection section)
			{
				DetachObservers();
				platformView.Disconnect();
				platformView.Connect(section);
			}

			_rebinding = platformView is not null;
			try
			{
				base.SetVirtualView(view);
			}
			finally
			{
				_rebinding = false;
			}

			if (platformView is not null)
			{
				platformView.Connect(VirtualView);
				AttachObservers();
				platformView.UpdateCurrentItem(VirtualView.CurrentItem);
				SyncNavigationStack(animated: false);
			}
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
			if (handler._rebinding)
				return;

			handler.PlatformView.UpdateCurrentItem(item.CurrentItem);
			handler.SyncNavigationStack(animated: false);
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
			base.ConnectHandler(platformView);
			try
			{
				platformView.Connect(VirtualView);

				AttachObservers();

				platformView.UpdateCurrentItem(VirtualView.CurrentItem);
				SyncNavigationStack(animated: false);
			}
			catch
			{
				DetachObservers();
				platformView.Disconnect();
				base.DisconnectHandler(platformView);
				throw;
			}
		}

		protected override void DisconnectHandler(TizenShellSectionStackManager platformView)
		{
			try
			{
				DetachObservers();

				platformView.Disconnect();
			}
			finally
			{
				base.DisconnectHandler(platformView);
			}
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
			if (_disposedValue)
				return;

			_disposedValue = true;
			if (!disposing)
				return;

			var platformView = PlatformView;
			ExceptionSafeCleanup.Run(
				DetachObservers,
				() => (this as IElementHandler)?.DisconnectHandler(),
				() => platformView?.Dispose());
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

			PlatformView.RequestNavigation(new NavigationRequest(pageStack, animated));
		}

		/// <summary>
		/// No-op: Top tab bar appearance is handled in the shell section view.
		/// </summary>
		void IAppearanceObserver.OnAppearanceChanged(ShellAppearance appearance)
		{
			PlatformView?.UpdateTopTabBarColors(
				appearance.ForegroundColor,
				appearance.BackgroundColor,
				appearance.TitleColor,
				appearance.UnselectedColor);
		}

		void AttachObservers()
		{
			if (!ReferenceEquals(_observedSection, VirtualView))
			{
				_observedSection = VirtualView;
				((IShellSectionController)_observedSection).NavigationRequested += OnNavigationRequested;
			}

			var shell = VirtualView.FindParentOfType<Shell>();
			if (!ReferenceEquals(_observedShell, shell))
			{
				if (_observedShell is not null)
					((IShellController)_observedShell).RemoveAppearanceObserver(this);
				_observedShell = shell;
				if (_observedShell is not null)
					((IShellController)_observedShell).AddAppearanceObserver(this, VirtualView);
			}
		}

		void DetachObservers()
		{
			if (_observedSection is not null)
			{
				((IShellSectionController)_observedSection).NavigationRequested -= OnNavigationRequested;
				_observedSection = null;
			}

			if (_observedShell is not null)
			{
				((IShellController)_observedShell).RemoveAppearanceObserver(this);
				_observedShell = null;
			}
		}
	}

	/// <summary>
	/// Dummy page used as root for navigation stack sync.
	/// </summary>
	internal class TizenDummyPage : Page
	{
	}
}
