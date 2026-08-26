# Wave C mapper parity

Companion to [`wave-c-mapper-parity.json`](wave-c-mapper-parity.json), which is **generated from
source** by `Maui.Tizen.SourceTests`. Regenerate both after an intentional change:

```bash
MAUI_TIZEN_UPDATE_PARITY=1 dotnet test tests/Maui.Tizen.SourceTests/Maui.Tizen.SourceTests.csproj
```

`WaveCMapperParityTests.ParityManifestMatchesSource` fails if the JSON and the source disagree,
so this cannot drift silently.

## How to read the statuses

| Status | Meaning |
| --- | --- |
| `Supported` | The mapper does real work on Tizen. |
| `NoOp` | Declared but intentionally empty, because Tizen has no equivalent. Every one carries an XML doc comment saying why, and `EveryNoOpMapperDocumentsWhy` enforces that. |

`UncoveredNeutralKeys` lists keys the neutral MAUI handler declares that the Tizen handler does
not. These are **recorded gaps, not silent ones**: `EveryNeutralMapperKeyIsImplementedOrRecorded`
fails if a new one appears without being written down here.

## Summary

- 20 migrated handlers
- 54 supported mappings, 33 documented no-ops
- 4 handlers with recorded neutral-key gaps

The recorded gaps are all view-level or semantic properties (`BackgroundColor`, `Hint`,
`HeadingLevel`, `IsInAccessibleTree`, ...) supplied at runtime by the chained
`ViewMapper`/`ElementMapper`. They are listed for completeness, not as missing behaviour.

## TabbedPage badges

`BadgeText`, `BadgeColor` and `BadgeTextColor` (dotnet/maui#37755) are declared and classified
`NoOp`. Upstream states that "Tizen exposes the shared API without a platform renderer, matching
Shell's current support matrix" - Tizen's tab strip is a plain `CollectionView` with a label and
a selection bar, with no badge decoration to drive. Setting a badge binds and raises property
changes normally; nothing is drawn.

Their mapper keys are string literals rather than `nameof`, because the compile-verification lane
targets the repository's behaviourBaseline (MAUI 9.0.120), which predates the API. The literals
match `BindableProperty.CreateAttached(...)` exactly and should become `nameof` once the
validation baseline carries the properties.

## Handlers

### `TizenCarouselViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenCarouselViewHandler.cs`
- Base: `TizenItemsViewHandler<CarouselView>`
- Neutral counterpart: `Microsoft.Maui.Controls.Handlers.Items.CarouselViewHandler`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `CurrentItem` | Supported |  |
| `Position` | Supported |  |
| `IsBounceEnabled` | NoOp | No-op: IsBounceEnabled is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not support bounce/overscroll effects. This mapper is declared for API completeness but performs no operation. |
| `IsSwipeEnabled` | NoOp | No-op: IsSwipeEnabled is not directly controllable on Tizen. Tizen.UIExtensions.NUI.CollectionView does not support disabling swipe/scroll gestures. The carousel is always scrollable when items are present. This mapper is declared for API completeness but performs no operation. |
| `PeekAreaInsets` | NoOp | No-op: PeekAreaInsets is not supported on Tizen. Tizen CollectionView does not support showing parts of adjacent items. This mapper is declared for API completeness but performs no operation. |
| `Loop` | NoOp | No-op: Loop is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not support infinite looping. This mapper is declared for API completeness but performs no operation. |
| `ItemsLayout` | Supported |  |

**Recorded gaps:** `BackgroundColor`, `BackgroundImageSource`, `Description`, `ExcludedWithChildren`, `HeadingLevel`, `Hint`, `IsInAccessibleTree`, `IsVisible`

### `TizenCollectionViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenCollectionViewHandler.cs`
- Base: `TizenReorderableItemsViewHandler<CollectionView>`
- Neutral counterpart: `Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler`

**Recorded gaps:** `BackgroundColor`, `BackgroundImageSource`, `Description`, `ExcludedWithChildren`, `HeadingLevel`, `Hint`, `IsInAccessibleTree`, `IsVisible`

### `TizenFlyoutViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Navigation/TizenFlyoutViewHandler.cs`
- Base: `ViewHandler<IFlyoutView, DrawerView>`
- Neutral counterpart: `Microsoft.Maui.Handlers.FlyoutViewHandler`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Flyout` | Supported |  |
| `Detail` | Supported |  |
| `IsPresented` | Supported |  |
| `FlyoutBehavior` | Supported |  |
| `FlyoutWidth` | Supported |  |
| `IsGestureEnabled` | Supported |  |
| `Toolbar` | Supported |  |

**Recorded gaps:** `BackgroundColor`, `BackgroundImageSource`, `Description`, `ExcludedWithChildren`, `HeadingLevel`, `Hint`, `IsInAccessibleTree`

### `TizenGroupableItemsViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenGroupableItemsViewHandler.cs`
- Base: `TizenSelectableItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `IsGrouped` | Supported |  |
| `GroupHeaderTemplate` | Supported |  |
| `GroupFooterTemplate` | Supported |  |

### `TizenItemsViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenItemsViewHandler.cs`
- Base: `ViewHandler<TItemsView, NView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `ItemsSource` | Supported |  |
| `ItemTemplate` | Supported |  |
| `EmptyView` | Supported |  |
| `EmptyViewTemplate` | Supported |  |
| `RemainingItemsThreshold` | NoOp | No-op: RemainingItemsThreshold is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not currently expose an API for threshold-based notifications when approaching the end of content. |
| `HorizontalScrollBarVisibility` | NoOp | No-op: HorizontalScrollBarVisibility is not configurable on Tizen CollectionView. |
| `VerticalScrollBarVisibility` | NoOp | No-op: VerticalScrollBarVisibility is not configurable on Tizen CollectionView. |
| `ItemsUpdatingScrollMode` | NoOp | No-op: ItemsUpdatingScrollMode is not supported on Tizen. |

**Command mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `ScrollTo` | Supported |  |

### `TizenMenuBarHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuBar, NView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `IsEnabled` | NoOp | Unsupported: there is no menu bar to enable or disable. |

### `TizenMenuBarItemHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuBarItem, NView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Text` | NoOp | Unsupported: there is no menu bar item to label. |
| `IsEnabled` | NoOp | Unsupported: there is no menu bar item to enable or disable. |

### `TizenMenuFlyoutHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyout, NView>`

### `TizenMenuFlyoutItemHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyoutItem, NView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Text` | NoOp | Unsupported: there is no menu flyout item to label. |
| `IsEnabled` | NoOp | Unsupported: there is no menu flyout item to enable or disable. |

### `TizenMenuFlyoutSeparatorHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyoutSeparator, NView>`

### `TizenMenuFlyoutSubItemHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Menu/TizenMenuHandlers.cs`
- Base: `ElementHandler<IMenuFlyoutSubItem, NView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Text` | NoOp | Unsupported: there is no submenu to label. |

### `TizenNavigationViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Navigation/TizenNavigationViewHandler.cs`
- Base: `ViewHandler<IStackNavigationView, StackNavigationManager>`
- Neutral counterpart: `Microsoft.Maui.Handlers.NavigationViewHandler`

**Command mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `RequestNavigation` | Supported |  |

**Recorded gaps:** `BackgroundColor`, `BackgroundImageSource`, `Description`, `ExcludedWithChildren`, `HeadingLevel`, `Hint`, `IsInAccessibleTree`

### `TizenReorderableItemsViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenReorderableItemsViewHandler.cs`
- Base: `TizenGroupableItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `CanReorderItems` | NoOp | #region Mapper Methods Unsupported: CanReorderItems is not supported on Tizen. Tizen.UIExtensions.NUI.CollectionView does not currently support drag-and-drop reordering of items. This mapper is declared for API completeness but performs no operation. |

### `TizenSelectableItemsViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenSelectableItemsViewHandler.cs`
- Base: `TizenStructuredItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `SelectedItem` | Supported |  |
| `SelectedItems` | Supported |  |
| `SelectionMode` | Supported |  |

### `TizenShellHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Shell/TizenShellHandler.cs`
- Base: `ViewHandler<Shell, TizenShellView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Flyout` | Supported |  |
| `IsPresented` | Supported |  |
| `FlyoutBehavior` | Supported |  |
| `FlyoutWidth` | Supported |  |
| `FlyoutBackground` | Supported |  |
| `CurrentItem` | Supported |  |
| `FlyoutBackdrop` | Supported |  |
| `FlyoutFooter` | Supported |  |
| `FlyoutHeader` | Supported |  |
| `FlyoutHeaderBehavior` | Supported |  |
| `Items` | Supported |  |
| `FlyoutContent` | Supported |  |
| `FlowDirection` | NoOp | No-op: Tizen does not support FlowDirection on Shell flyout. |
| `FlyoutBackgroundImage` | NoOp | No-op: Tizen does not support FlyoutBackgroundImage. |
| `FlyoutBackgroundImageAspect` | NoOp | No-op: Tizen does not support FlyoutBackgroundImageAspect. |
| `FlyoutVerticalScrollMode` | NoOp | No-op: Tizen does not support FlyoutVerticalScrollMode. |
| `FlyoutIcon` | NoOp | No-op: Tizen does not support custom FlyoutIcon. |

### `TizenShellItemHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Shell/TizenShellItemHandler.cs`
- Base: `ElementHandler<ShellItem, TizenShellItemView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `CurrentItem` | Supported |  |

### `TizenShellSectionHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Shell/TizenShellSectionHandler.cs`
- Base: `ElementHandler<ShellSection, TizenShellSectionStackManager>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `CurrentItem` | Supported |  |

**Command mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `RequestNavigation` | Supported |  |

### `TizenStructuredItemsViewHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Items/TizenStructuredItemsViewHandler.cs`
- Base: `TizenItemsViewHandler<TItemsView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Header` | Supported |  |
| `HeaderTemplate` | Supported |  |
| `Footer` | Supported |  |
| `FooterTemplate` | Supported |  |
| `ItemsLayout` | Supported |  |
| `ItemSizingStrategy` | Supported |  |

### `TizenTabbedPageHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Navigation/TizenTabbedPageHandler.cs`
- Base: `ViewHandler<TabbedPage, NView>`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `BarBackground` | NoOp | #region Mapper Methods No-op: BarBackground styling is handled via bindings in the TabbedItem. The BarBackground is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `BarBackgroundColor` | NoOp | No-op: BarBackgroundColor styling is handled via bindings in the TabbedItem. The BarBackgroundColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `BarTextColor` | NoOp | No-op: BarTextColor styling is handled via bindings in the TabbedItem. The BarTextColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `UnselectedTabColor` | NoOp | No-op: UnselectedTabColor styling is handled via bindings in the TabbedItem. The UnselectedTabColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `SelectedTabColor` | NoOp | No-op: SelectedTabColor styling is handled via bindings in the TabbedItem. The SelectedTabColor is bound directly to the tab items via XAML bindings. This mapper exists for API completeness but performs no additional operation. |
| `ItemsSource` | NoOp | No-op: ItemsSource is managed through Children collection. TabbedPage uses the Children collection directly rather than ItemsSource. This mapper exists for API completeness but performs no operation. |
| `ItemTemplate` | NoOp | No-op: ItemTemplate is not used by TabbedPage on Tizen. TabbedPage uses a fixed template for tab items. This mapper exists for API completeness but performs no operation. |
| `SelectedItem` | NoOp | No-op: SelectedItem is managed through CurrentPage. TabbedPage uses CurrentPage rather than SelectedItem. This mapper exists for API completeness but performs no operation. |
| `CurrentPage` | Supported |  |
| `BadgeText` | NoOp | Unsupported: Tizen has no tab badge affordance. Upstream (dotnet/maui#37755) added <c>BadgeText</c>, <c>BadgeColor</c> and "Tizen exposes the shared API without a platform renderer, matching Shell's current support matrix". Tizen's NUI tab strip is a plain is no badge decoration to drive. The mapping is declared rather than omitted so that the gap is an explicit, reviewable classification in the parity artifact instead of a silent miss. Setting a badge on Tizen binds and raises property changes normally; nothing is drawn. |
| `BadgeColor` | NoOp | Unsupported: Tizen has no tab badge affordance, so there is no badge to colour. See <see cref="MapBadgeText"/> for the full rationale and the upstream reference. |
| `BadgeTextColor` | NoOp | Unsupported: Tizen has no tab badge affordance, so there is no badge text to colour. See <see cref="MapBadgeText"/> for the full rationale and the upstream reference. |

### `TizenToolbarHandler`

- Source: `src/Maui.Tizen.Controls.Navigation/Handlers/Toolbar/TizenToolbarHandler.cs`
- Base: `ElementHandler<Toolbar, MauiToolbar>`
- Neutral counterpart: `Microsoft.Maui.Handlers.ToolbarHandler`

**Property mappers**

| Key | Status | Notes |
| --- | --- | --- |
| `Title` | Supported |  |
| `IsVisible` | Supported |  |
| `BackButtonVisible` | Supported |  |
| `TitleIcon` | Supported |  |
| `TitleView` | Supported |  |
| `IconColor` | Supported |  |
| `ToolbarItems` | Supported |  |
| `BackButtonTitle` | Supported |  |
| `BarBackground` | Supported |  |
| `BarTextColor` | Supported |  |
| `BackButtonEnabled` | NoOp | No-op: Tizen's toolbar icon has no separate enabled state. The in-tree backend simply had no mapping, which meant a silent miss. Declaring it as an explicit no-op keeps <c>Parity/MapperParity.json</c> honest and gives the source tests something to assert against. |
| `DynamicOverflowEnabled` | NoOp | No-op: Tizen has no dynamic overflow concept; overflow is always dynamic. |

