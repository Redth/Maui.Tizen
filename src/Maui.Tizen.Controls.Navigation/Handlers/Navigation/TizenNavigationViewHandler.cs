using System;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IStackNavigationView"/> (the platform side of
	/// <c>NavigationPage</c>).
	/// </summary>
	/// <remarks>
	/// The stack itself is owned by <see cref="StackNavigationManager"/>, which is Core-level
	/// (Maui.Tizen.Core) infrastructure shared with Shell. Wave C only owns the handler that binds a
	/// navigation view to it.
	/// </remarks>
	public partial class TizenNavigationViewHandler
		: ViewHandler<IStackNavigationView, StackNavigationManager>, INavigationViewHandler, IPlatformViewHandler
	{
		public static IPropertyMapper<IStackNavigationView, TizenNavigationViewHandler> Mapper =
			new PropertyMapper<IStackNavigationView, TizenNavigationViewHandler>(ViewMapper);

		public static CommandMapper<IStackNavigationView, TizenNavigationViewHandler> CommandMapper =
			new(ViewCommandMapper)
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

		protected override StackNavigationManager CreatePlatformView() => new();

		protected override void ConnectHandler(StackNavigationManager platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Connect(VirtualView);
		}

		protected override void DisconnectHandler(StackNavigationManager platformView)
		{
			// The manager's NUI body is disposed independently of the handler (for example when the
			// owning window closes first). Touching a disposed body throws, so bail out the same way
			// the in-tree backend did.
			if (!platformView.HasBody())
			{
				return;
			}

			base.DisconnectHandler(platformView);
			platformView.Disconnect();
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
	}
}
