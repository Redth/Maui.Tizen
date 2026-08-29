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

	class TizenToolbarHandler : WaveCHostHandler<Toolbar>;
	class TizenMenuBarHandler : WaveCHostHandler<IMenuBar>;
	class TizenMenuBarItemHandler : WaveCHostHandler<IMenuBarItem>;
	class TizenMenuFlyoutHandler : WaveCHostHandler<IMenuFlyout>;
	class TizenMenuFlyoutItemHandler : WaveCHostHandler<IMenuFlyoutItem>;
	class TizenMenuFlyoutSeparatorHandler : WaveCHostHandler<IMenuFlyoutSeparator>;
	class TizenMenuFlyoutSubItemHandler : WaveCHostHandler<IMenuFlyoutSubItem>;
	class TizenNavigationViewHandler : WaveCHostHandler<NavigationPage>;
	class TizenFlyoutViewHandler : WaveCHostHandler<FlyoutPage>;
	class TizenTabbedPageHandler : WaveCHostHandler<TabbedPage>;
	class TizenShellHandler : WaveCHostHandler<Shell>;
	class TizenCollectionViewHandler : WaveCHostHandler<CollectionView>;
	class TizenCarouselViewHandler : WaveCHostHandler<CarouselView>;
}

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	using Microsoft.Maui.Controls;
	using Microsoft.Maui.Graphics;

	public class TizenShellView
	{
		public void UpdateToolbarColors(Color? foreground, Color? background, Color? title)
		{
		}
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
