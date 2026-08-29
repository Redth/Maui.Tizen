# Wave C mapper parity

Companion to [`wave-c-mapper-parity.json`](wave-c-mapper-parity.json), generated deterministically
from the current Wave C source by `Maui.Tizen.SourceTests`. Regenerate the JSON with:

```bash
MAUI_TIZEN_UPDATE_PARITY=1 dotnet test tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj
```

## Summary

- 20 migrated handlers
- 57 supported mappings, 33 documented no-ops
- 0 handlers with recorded neutral-key gaps

`UncoveredNeutralKeys` are recorded gaps, not silent omissions. The source tests fail when the
current MAUI mapper surface and this manifest differ.

## Handlers

### `TizenCarouselViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenCarouselViewHandler.cs`
- Base: `TizenItemsViewHandler<CarouselView>`
- Neutral counterpart: `Microsoft.Maui.Controls.Handlers.Items.CarouselViewHandler`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `CurrentItem` | `MapCurrentItem` | Supported |  |
| `Position` | `MapPosition` | Supported |  |
| `IsBounceEnabled` | `MapIsBounceEnabled` | NoOp | No-op: IsBounceEnabled is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not support bounce/overscroll effects. This mapper is declared for API completeness but performs no operation. |
| `IsSwipeEnabled` | `MapIsSwipeEnabled` | NoOp | No-op: IsSwipeEnabled is not directly controllable on Tizen. Tizen.UIExtensions.NUI.CollectionView does not support disabling swipe/scroll gestures. The carousel is always scrollable when items are present. This mapper is declared for API completeness but performs no operation. |
| `PeekAreaInsets` | `MapPeekAreaInsets` | NoOp | No-op: PeekAreaInsets is not supported on Tizen. Tizen CollectionView does not support showing parts of adjacent items. This mapper is declared for API completeness but performs no operation. |
| `Loop` | `MapLoop` | NoOp | No-op: Loop is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not support infinite looping. This mapper is declared for API completeness but performs no operation. |
| `ItemsLayout` | `MapItemsLayout` | Supported |  |

### `TizenCollectionViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenCollectionViewHandler.cs`
- Base: `TizenReorderableItemsViewHandler<CollectionView>`
- Neutral counterpart: `Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler`

### `TizenFlyoutViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Navigation/TizenFlyoutViewHandler.cs`
- Base: `TizenViewHandler<IFlyoutView, DrawerView>`
- Neutral counterpart: `Microsoft.Maui.Handlers.FlyoutViewHandler`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Flyout` | `MapFlyout` | Supported |  |
| `Detail` | `MapDetail` | Supported |  |
| `IsPresented` | `MapIsPresented` | Supported |  |
| `FlyoutBehavior` | `MapFlyoutBehavior` | Supported |  |
| `FlyoutWidth` | `MapFlyoutWidth` | Supported |  |
| `IsGestureEnabled` | `MapIsGestureEnabled` | Supported |  |
| `Toolbar` | `MapToolbar` | Supported |  |
| `FlyoutLayoutBehavior` | `MapFlyoutLayoutBehavior` | Supported |  |

### `TizenGroupableItemsViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenGroupableItemsViewHandler.cs`
- Base: `TizenSelectableItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `IsGrouped` | `MapIsGrouped` | Supported |  |
| `GroupHeaderTemplate` | `MapGroupHeaderTemplate` | Supported |  |
| `GroupFooterTemplate` | `MapGroupFooterTemplate` | Supported |  |

### `TizenItemsViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenItemsViewHandler.cs`
- Base: `TizenViewHandler<TItemsView, NView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `ItemsSource` | `MapItemsSource` | Supported |  |
| `ItemTemplate` | `MapItemTemplate` | Supported |  |
| `EmptyView` | `MapEmptyView` | Supported |  |
| `EmptyViewTemplate` | `MapEmptyViewTemplate` | Supported |  |
| `RemainingItemsThreshold` | `MapRemainingItemsThreshold` | NoOp | No-op: RemainingItemsThreshold is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not currently expose an API for threshold-based notifications when approaching the end of content. |
| `HorizontalScrollBarVisibility` | `MapHorizontalScrollBarVisibility` | NoOp | No-op: HorizontalScrollBarVisibility is not configurable on Tizen CollectionView. |
| `VerticalScrollBarVisibility` | `MapVerticalScrollBarVisibility` | NoOp | No-op: VerticalScrollBarVisibility is not configurable on Tizen CollectionView. |
| `ItemsUpdatingScrollMode` | `MapItemsUpdatingScrollMode` | NoOp | No-op: ItemsUpdatingScrollMode is not supported on Tizen. |
| `IsVisible` | `MapIsVisible` | Supported |  |

**Command mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `ScrollTo` | `MapScrollTo` | Supported |  |

### `TizenMenuBarHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuBar, NView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `IsEnabled` | `MapIsEnabled` | NoOp | Unsupported: IsEnabled has no effect because Tizen NUI ships no menu bar widget, so there is no menu bar surface whose interactivity could be toggled. |

### `TizenMenuBarItemHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuBarItem, NView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Text` | `MapText` | NoOp | Unsupported: Text has no effect because Tizen NUI ships no menu bar, so there is no menu bar item label to render the string into. |
| `IsEnabled` | `MapIsEnabled` | NoOp | Unsupported: IsEnabled has no effect because Tizen NUI ships no menu bar, so there is no menu bar item whose interactivity could be toggled. |

### `TizenMenuFlyoutHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyout, NView>`

### `TizenMenuFlyoutItemHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyoutItem, NView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Text` | `MapText` | NoOp | Unsupported: Text has no effect because Tizen NUI ships no context-menu primitive, so there is no menu flyout item label to render the string into. |
| `IsEnabled` | `MapIsEnabled` | NoOp | Unsupported: IsEnabled has no effect because Tizen NUI ships no context-menu primitive, so there is no menu flyout item whose interactivity could be toggled. |

### `TizenMenuFlyoutSeparatorHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyoutSeparator, NView>`

### `TizenMenuFlyoutSubItemHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyoutSubItem, NView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Text` | `MapText` | NoOp | Unsupported: Text has no effect because Tizen NUI ships no context-menu primitive, so there is no submenu header to render the string into. |

### `TizenNavigationViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Navigation/TizenNavigationViewHandler.cs`
- Base: `TizenViewHandler<IStackNavigationView, TizenStackNavigationManager>`
- Neutral counterpart: `Microsoft.Maui.Handlers.NavigationViewHandler`

**Command mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `RequestNavigation` | `RequestNavigation` | Supported |  |

### `TizenReorderableItemsViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenReorderableItemsViewHandler.cs`
- Base: `TizenGroupableItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `CanReorderItems` | `MapCanReorderItems` | NoOp | #region Mapper Methods Unsupported: CanReorderItems is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not currently support drag-and-drop reordering of items. This mapper is declared for API completeness but performs no operation. |

### `TizenSelectableItemsViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenSelectableItemsViewHandler.cs`
- Base: `TizenStructuredItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `SelectedItem` | `MapSelectedItem` | Supported |  |
| `SelectedItems` | `MapSelectedItems` | Supported |  |
| `SelectionMode` | `MapSelectionMode` | Supported |  |

### `TizenShellHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Shell/TizenShellHandler.cs`
- Base: `TizenViewHandler<Shell, TizenShellView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Flyout` | `MapFlyout` | Supported |  |
| `IsPresented` | `MapIsPresented` | Supported |  |
| `FlyoutBehavior` | `MapFlyoutBehavior` | Supported |  |
| `FlyoutWidth` | `MapFlyoutWidth` | Supported |  |
| `FlyoutBackground` | `MapFlyoutBackground` | Supported |  |
| `CurrentItem` | `MapCurrentItem` | Supported |  |
| `FlyoutBackdrop` | `MapFlyoutBackdrop` | Supported |  |
| `FlyoutFooter` | `MapFlyoutFooter` | Supported |  |
| `FlyoutHeader` | `MapFlyoutHeader` | Supported |  |
| `FlyoutHeaderBehavior` | `MapFlyoutHeaderBehavior` | Supported |  |
| `Items` | `MapItems` | Supported |  |
| `FlyoutContent` | `MapFlyoutContent` | Supported |  |
| `FlowDirection` | `MapFlowDirection` | NoOp | No-op: Tizen does not support FlowDirection on Shell flyout. |
| `FlyoutBackgroundImage` | `MapFlyoutBackgroundImage` | NoOp | No-op: Tizen does not support FlyoutBackgroundImage. |
| `FlyoutBackgroundImageAspect` | `MapFlyoutBackgroundImageAspect` | NoOp | No-op: Tizen does not support FlyoutBackgroundImageAspect. |
| `FlyoutVerticalScrollMode` | `MapFlyoutVerticalScrollMode` | NoOp | No-op: Tizen does not support FlyoutVerticalScrollMode. |
| `FlyoutIcon` | `MapFlyoutIcon` | NoOp | No-op: Tizen does not support custom FlyoutIcon. |

### `TizenShellItemHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Shell/TizenShellItemHandler.cs`
- Base: `ElementHandler<ShellItem, TizenShellItemView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `CurrentItem` | `MapCurrentItem` | Supported |  |

### `TizenShellSectionHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Shell/TizenShellSectionHandler.cs`
- Base: `ElementHandler<ShellSection, TizenShellSectionStackManager>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `CurrentItem` | `MapCurrentItem` | Supported |  |

**Command mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `RequestNavigation` | `RequestNavigation` | Supported |  |

### `TizenStructuredItemsViewHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Items/TizenStructuredItemsViewHandler.cs`
- Base: `TizenItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Header` | `MapHeader` | Supported |  |
| `HeaderTemplate` | `MapHeaderTemplate` | Supported |  |
| `Footer` | `MapFooter` | Supported |  |
| `FooterTemplate` | `MapFooterTemplate` | Supported |  |
| `ItemsLayout` | `MapItemsLayout` | Supported |  |
| `ItemSizingStrategy` | `MapItemSizingStrategy` | Supported |  |

### `TizenTabbedPageHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Navigation/TizenTabbedPageHandler.cs`
- Base: `TizenViewHandler<TabbedPage, NView>`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `BarBackground` | `MapBarBackground` | NoOp | #region Mapper Methods No-op: BarBackground styling is handled via bindings in the TabbedItem. The BarBackground is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `BarBackgroundColor` | `MapBarBackgroundColor` | NoOp | No-op: BarBackgroundColor styling is handled via bindings in the TabbedItem. The BarBackgroundColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `BarTextColor` | `MapBarTextColor` | NoOp | No-op: BarTextColor styling is handled via bindings in the TabbedItem. The BarTextColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `UnselectedTabColor` | `MapUnselectedTabColor` | NoOp | No-op: UnselectedTabColor styling is handled via bindings in the TabbedItem. The UnselectedTabColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `SelectedTabColor` | `MapSelectedTabColor` | NoOp | No-op: SelectedTabColor styling is handled via bindings in the TabbedItem. The SelectedTabColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `ItemsSource` | `MapItemsSource` | NoOp | No-op: ItemsSource is managed through Children collection. TabbedPage uses the Children collection directly rather than ItemsSource. This mapper exists for API completeness but performs no operation. |
| `ItemTemplate` | `MapItemTemplate` | NoOp | No-op: ItemTemplate is not used by TabbedPage on Tizen. TabbedPage uses a fixed template for tab items. This mapper exists for API completeness but performs no operation. |
| `SelectedItem` | `MapSelectedItem` | NoOp | No-op: SelectedItem is managed through CurrentPage. TabbedPage uses CurrentPage rather than SelectedItem. This mapper exists for API completeness but performs no operation. |
| `CurrentPage` | `MapCurrentPage` | Supported |  |
| `BadgeText` | `MapBadgeText` | NoOp | Unsupported: Tizen has no tab badge affordance. Upstream (dotnet/maui#37755) added <c>BadgeText</c>, <c>BadgeColor</c> and "Tizen exposes the shared API without a platform renderer, matching Shell's current support matrix". Tizen's NUI tab strip is a plain is no badge decoration to drive. The mapping is declared rather than omitted so that the gap is an explicit, reviewable classification in the parity artifact instead of a silent miss. Setting a badge on Tizen binds and raises property changes normally; nothing is drawn. |
| `BadgeColor` | `MapBadgeColor` | NoOp | Unsupported: Tizen has no tab badge affordance, so there is no badge to colour. See <see cref="MapBadgeText"/> for the full rationale and the upstream reference. |
| `BadgeTextColor` | `MapBadgeTextColor` | NoOp | Unsupported: Tizen has no tab badge affordance, so there is no badge text to colour. See <see cref="MapBadgeText"/> for the full rationale and the upstream reference. |

### `TizenToolbarHandler`

- Source: `src/Maui.Tizen.Controls/Navigation/Handlers/Toolbar/TizenToolbarHandler.cs`
- Base: `ElementHandler<Toolbar, TizenToolbarView>`
- Neutral counterpart: `Microsoft.Maui.Handlers.ToolbarHandler`

**Property mappers**

| Key | Method | Status | Notes |
| --- | --- | --- | --- |
| `Title` | `MapTitle` | Supported |  |
| `IsVisible` | `MapIsVisible` | Supported |  |
| `BackButtonVisible` | `MapBackButtonVisible` | Supported |  |
| `TitleIcon` | `MapTitleIcon` | Supported |  |
| `TitleView` | `MapTitleView` | Supported |  |
| `IconColor` | `MapIconColor` | Supported |  |
| `ToolbarItems` | `MapToolbarItems` | Supported |  |
| `BackButtonTitle` | `MapBackButtonTitle` | Supported |  |
| `DrawerToggleVisible` | `MapDrawerToggleVisible` | Supported |  |
| `BarBackground` | `MapBarBackground` | Supported |  |
| `BarTextColor` | `MapBarTextColor` | Supported |  |
| `BackButtonEnabled` | `MapBackButtonEnabled` | NoOp | No-op: Tizen's toolbar icon has no separate enabled state. The in-tree backend simply had no mapping, which meant a silent miss. Declaring it as an explicit no-op keeps <c>Parity/MapperParity.json</c> honest and gives the source tests something to assert against. |
| `DynamicOverflowEnabled` | `MapDynamicOverflowEnabled` | NoOp | No-op: DynamicOverflowEnabled has no effect because Tizen always collapses secondary toolbar items behind the overflow button; there is no fixed-overflow mode to switch to. |

