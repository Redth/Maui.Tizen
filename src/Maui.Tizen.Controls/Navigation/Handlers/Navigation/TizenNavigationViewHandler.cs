using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IStackNavigationView"/> (the platform side of
	/// <c>NavigationPage</c>).
	/// </summary>
	/// <remarks>
	/// The stack itself is owned by <see cref="TizenStackNavigationManager"/>, which is Core-level
	/// (Maui.Tizen.Core) infrastructure shared with Shell. Wave C only owns the handler that binds a
	/// navigation view to it.
	/// </remarks>
	public partial class TizenNavigationViewHandler
		: TizenViewHandler<IStackNavigationView, TizenStackNavigationManager>, INavigationViewHandler
	{
		Toolbar? _toolbarElement;
		TizenToolbarView? _platformToolbar;
		bool _rebinding;

		public static IPropertyMapper<IStackNavigationView, TizenNavigationViewHandler> Mapper =
			new PropertyMapper<IStackNavigationView, TizenNavigationViewHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IToolbarElement.Toolbar)] = MapToolbar,
			};

		public static CommandMapper<IStackNavigationView, TizenNavigationViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
				[nameof(IStackNavigation.RequestNavigation)] = RequestNavigation,
			};

		public TizenNavigationViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenNavigationViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IStackNavigationView INavigationViewHandler.VirtualView => VirtualView;

		object INavigationViewHandler.PlatformView => PlatformView;

		public override void SetVirtualView(IView view)
		{
			var platformView = ((IElementHandler)this).PlatformView as TizenStackNavigationManager;
			if (platformView is not null)
			{
				ReleaseToolbar(platformView);
				platformView.Disconnect();
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
				UpdateToolbar();
				SyncNavigationStack(platformView);
			}
		}

		protected override TizenStackNavigationManager CreatePlatformView() => new();

		protected override void ConnectHandler(TizenStackNavigationManager platformView)
		{
			base.ConnectHandler(platformView);
			try
			{
				platformView.Connect(VirtualView);
				UpdateToolbar();
				SyncNavigationStack(platformView);
			}
			catch
			{
				ReleaseToolbar(platformView);
				base.DisconnectHandler(platformView);
				throw;
			}
		}

		protected override void DisconnectHandler(TizenStackNavigationManager platformView)
		{
			// The manager's NUI body is disposed independently of the handler (for example when the
			// owning window closes first). Touching a disposed body throws, so bail out the same way
			// the in-tree backend did.
			try
			{
				if (platformView.HasBody())
				{
					ReleaseToolbar(platformView, detachNative: true);
					platformView.Disconnect();
				}
				else
				{
					ReleaseToolbar(platformView, detachNative: false);
				}
			}
			finally
			{
				base.DisconnectHandler(platformView);
			}
		}

		public static void RequestNavigation(TizenNavigationViewHandler handler, IStackNavigation view, object? args)
		{
			if (args is NavigationRequest request)
			{
				handler.PlatformView?.RequestNavigation(request);
			}
			else
			{
				throw new InvalidOperationException($"{nameof(args)} must be a {nameof(NavigationRequest)}.");
			}
		}

		public static void MapToolbar(TizenNavigationViewHandler handler, IStackNavigationView view) =>
			handler.MapToolbar();

		void MapToolbar()
		{
			if (!_rebinding)
				UpdateToolbar();
		}

		void UpdateToolbar()
		{
			if (VirtualView is not IToolbarElement { Toolbar: Toolbar toolbar }
				|| MauiContext is null)
			{
				ReleaseToolbar(PlatformView);
				return;
			}

			var platformToolbar = (TizenToolbarView)toolbar.ToPlatformView(MauiContext);
			if (ReferenceEquals(_platformToolbar, platformToolbar))
			{
				PlatformView.SetToolbar(platformToolbar);
				return;
			}

			ReleaseToolbar(PlatformView);
			_toolbarElement = toolbar;
			_platformToolbar = platformToolbar;
			try
			{
				PlatformView.SetToolbar(platformToolbar);
			}
			catch
			{
				ReleaseToolbar(PlatformView);
				throw;
			}
		}

		void ReleaseToolbar(TizenStackNavigationManager container, bool detachNative = true)
		{
			var toolbar = _toolbarElement;
			var platformToolbar = _platformToolbar;
			var elementHandler = toolbar?.Handler;
			_toolbarElement = null;
			_platformToolbar = null;
			if (platformToolbar is null)
				return;

			ExceptionSafeCleanup.Run(
				() =>
				{
					if (detachNative)
						container.DetachToolbar(platformToolbar);
				},
				() => elementHandler?.DisconnectHandler(),
				platformToolbar.Dispose,
				() =>
				{
					if (toolbar is not null && ReferenceEquals(toolbar.Handler, elementHandler))
						toolbar.Handler = null;
				});
		}

		void SyncNavigationStack(TizenStackNavigationManager platformView)
		{
			if (VirtualView is NavigationPage page)
			{
				platformView.RequestNavigation(new NavigationRequest(
					page.Navigation.NavigationStack.Cast<IView>().ToList(),
					animated: false));
			}
		}
	}
}
