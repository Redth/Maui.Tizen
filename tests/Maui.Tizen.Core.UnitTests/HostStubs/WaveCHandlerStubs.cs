using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

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
	class TizenShellItemHandler : WaveCHostHandler<ShellItem>;
	class TizenShellSectionHandler : WaveCHostHandler<ShellSection>;
	class TizenCollectionViewHandler : WaveCHostHandler<CollectionView>;
	class TizenCarouselViewHandler : WaveCHostHandler<CarouselView>;
}
