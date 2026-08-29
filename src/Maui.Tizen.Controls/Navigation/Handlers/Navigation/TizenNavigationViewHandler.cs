using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

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

		protected override TizenStackNavigationManager CreatePlatformView() => new();

		protected override void ConnectHandler(TizenStackNavigationManager platformView)
		{
			base.ConnectHandler(platformView);
			try
			{
				platformView.Connect(VirtualView);
				UpdateToolbar();
			}
			catch
			{
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
					platformView.ClearToolbar();
					platformView.Disconnect();
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
			handler.UpdateToolbar();

		void UpdateToolbar()
		{
			if (VirtualView is not IToolbarElement { Toolbar: Toolbar toolbar }
				|| MauiContext is null)
			{
				PlatformView.ClearToolbar();
				return;
			}

			PlatformView.SetToolbar((TizenToolbarView)toolbar.ToPlatformView(MauiContext));
		}
	}
}
