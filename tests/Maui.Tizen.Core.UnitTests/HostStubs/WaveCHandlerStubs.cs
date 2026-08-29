using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	abstract class WaveCHostHandler<TVirtualView> : ElementHandler<TVirtualView, object>
		where TVirtualView : class, IElement
	{
		protected WaveCHostHandler()
			: base(ElementMapper)
		{
		}

		protected override object CreatePlatformElement() => new();
	}

	class TizenToolbarHandler : ElementHandler<Toolbar, TizenToolbarView>
	{
		public TizenToolbarHandler() : base(ElementMapper)
		{
		}

		protected override TizenToolbarView CreatePlatformElement() => new();
	}
	class TizenMenuBarHandler : WaveCHostHandler<IMenuBar>;
	class TizenMenuBarItemHandler : WaveCHostHandler<IMenuBarItem>;
	class TizenMenuFlyoutHandler : WaveCHostHandler<IMenuFlyout>;
	class TizenMenuFlyoutItemHandler : WaveCHostHandler<IMenuFlyoutItem>;
	class TizenMenuFlyoutSeparatorHandler : WaveCHostHandler<IMenuFlyoutSeparator>;
	class TizenMenuFlyoutSubItemHandler : WaveCHostHandler<IMenuFlyoutSubItem>;
	class TizenFlyoutViewHandler : WaveCHostHandler<FlyoutPage>;
	class TizenTabbedPageHandler : WaveCHostHandler<TabbedPage>;
	class TizenCollectionViewHandler : WaveCHostHandler<CollectionView>;
	class TizenCarouselViewHandler : WaveCHostHandler<CarouselView>;
}

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	using Microsoft.Maui.Controls;
	using Microsoft.Maui.Graphics;

	public class TizenShellView : TizenPlatformView
	{
		public bool IsOpened { get; set; }
		public int FlyoutItemsUpdates { get; private set; }
		public Brush? FlyoutBackground { get; private set; }
		public event EventHandler? Toggled;

		public void SetElement(Shell shell, IMauiContext context)
		{
		}

		public void UpdateFlyout(IView? flyout)
		{
		}

		public void UpdateFlyoutBehavior(FlyoutBehavior behavior)
		{
		}

		public void UpdateDrawerWidth(double width)
		{
		}

		internal void UpdateFlyoutBackground(Brush? brush) => FlyoutBackground = brush;

		public void UpdateCurrentItem(ShellItem? item)
		{
		}

		public void UpdateFlyoutBackDrop(Brush? brush)
		{
		}

		public void UpdateFlyoutFooter(Shell shell)
		{
		}

		public void UpdateFlyoutHeader(Shell shell)
		{
		}

		public void UpdateItems() => FlyoutItemsUpdates++;

		public void UpdateFlyoutContent()
		{
		}

		public void UpdateToolbar()
		{
		}

		public void UpdateSearchHandler()
		{
		}

		public void DetachToolbar()
		{
		}

		public void UpdateToolbarColors(Color? foreground, Color? background, Color? title)
		{
		}

		public void RaiseToggled() => Toggled?.Invoke(this, EventArgs.Empty);
	}

	public class TizenShellItemView : IDisposable
	{
		public TizenShellItemView(ShellItem item, IMauiContext context)
		{
		}

		public ShellSection? CurrentItem { get; private set; }
		public int CurrentItemUpdates { get; private set; }
		public bool? TabBarVisible { get; private set; }
		public int RebindCount { get; private set; }

		public void Rebind(ShellItem item)
		{
			RebindCount++;
		}

		public void UpdateCurrentItem(ShellSection? section)
		{
			CurrentItem = section;
			CurrentItemUpdates++;
		}

		public void UpdateTabBar(bool visible) => TabBarVisible = visible;

		public void UpdateBottomTabBarColors(Color? background, Color? title, Color? unselected)
		{
		}

		public void Dispose()
		{
		}
	}

	public class TizenShellSectionStackManager : IDisposable
	{
		public List<string> Calls { get; } = new();
		public ShellContent? CurrentItem { get; private set; }
		public int CurrentItemUpdates { get; private set; }
		public int NavigationRequests { get; private set; }
		public int ConnectCount { get; private set; }

		public void Connect(IElement element)
		{
			ConnectCount++;
		}

		public void Disconnect()
		{
		}

		public void UpdateCurrentItem(ShellContent? content)
		{
			Calls.Add("current");
			CurrentItem = content;
			CurrentItemUpdates++;
		}

		public void RequestNavigation(NavigationRequest request)
		{
			Calls.Add("navigation");
			NavigationRequests++;
		}

		public void UpdateTopTabBarColors(Color foreground, Color background, Color title, Color unselected)
		{
		}

		public void Dispose()
		{
		}
	}
}

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenToolbarView : TizenPlatformView
	{
	}

	public class TizenStackNavigationManager : TizenPlatformView
	{
		public IView? ConnectedView { get; private set; }
		public int ConnectCount { get; private set; }
		public int DisconnectCount { get; private set; }
		public int NavigationRequests { get; private set; }
		public IReadOnlyList<IView>? LastStack { get; private set; }
		public TizenToolbarView? Toolbar { get; private set; }

		public void Connect(IView view)
		{
			ConnectedView = view;
			ConnectCount++;
		}

		public void Disconnect()
		{
			ConnectedView = null;
			DisconnectCount++;
		}

		public void RequestNavigation(NavigationRequest request)
		{
			LastStack = request.NavigationStack;
			NavigationRequests++;
		}

		public void SetToolbar(TizenToolbarView toolbar) => Toolbar = toolbar;

		public void DetachToolbar(TizenToolbarView toolbar)
		{
			if (ReferenceEquals(Toolbar, toolbar))
				Toolbar = null;
		}
	}
}

namespace Microsoft.Maui.Platform
{
	using Microsoft.Maui.Controls;

	internal static class WaveCToolbarHostExtensions
	{
		public static object ToPlatformView(this Toolbar toolbar, IMauiContext context) =>
			new global::Microsoft.Maui.Platforms.Tizen.TizenToolbarView();
	}
}
