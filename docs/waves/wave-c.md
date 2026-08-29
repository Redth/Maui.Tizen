# Wave C — navigation and advanced Controls

Wave C owns the Tizen implementations for navigation, Shell, CollectionView/items, CarouselView,
toolbar, and menus. The shipping sources live under `src/Maui.Tizen.Controls/Navigation/` and are
compiled into the existing `Maui.Tizen.Controls` assembly. There is no separate navigation package
or second public startup API.

## Production composition

`UseMauiAppTizenControls<TApp>` is the single Controls application entry point. Its internal
Controls composition registers every concrete Wave C type after MAUI's neutral registrations, so
`IMauiHandlersFactory` resolves Tizen handlers for:

- `Toolbar` and menu types;
- `NavigationPage`, `FlyoutPage`, and `TabbedPage`;
- `Shell`, `ShellItem`, and `ShellSection`;
- `CollectionView` and `CarouselView`.

The handlers use MAUI's current public handler interfaces where available and chain the
Tizen-owned `TizenViewMappers` / `ViewCommandMapper` implementations rather than neutral platform
no-op bodies.

## Shell and navigation lifecycle

- `TizenShellSectionStackManager` owns one lazily-created `TizenShellSectionView` root. A current
  `ShellContent` set before root creation is applied when the root is first mounted. Later
  `CurrentItem` mappings update the root before synchronizing the navigation stack.
- Shell item and section platform views are cached while live, unmounted without disposal during
  selection changes, and disposed when removed or during teardown.
- Bottom-tab native changes go through `IShellItemController.ProposeSection`. Rejected proposals
  restore the authored selection. Accepted top-tab and More proposals explicitly resynchronize
  native selection. Managed selection updates suppress synchronous and deferred native echoes.
- Hidden sections are filtered from the tab adaptor. Visibility and items changes rebuild the bar,
  while `ShowTabs == false` detaches an already-created bar without discarding section state.
- `TizenShellHandler` and `TizenNavigationViewHandler` both expose reachable toolbar mappings.
  Shell toolbar replacement first detaches the native container, then disconnects/disposes and
  clears the persistent handler so reconnection cannot reuse a disposed platform view. Back
  navigation requires a visible, enabled back button.
- Search is represented by `TizenShellSearchView`, attached to `TizenToolbarView.SearchBar`. It
  follows the current page's effective `SearchHandler`, query, list proxy, item template, enabled
  state, `ShowsResults`, visibility, focus requests, query confirmation, and item selection.
  `Collapsible` presents a search affordance until focus or a query expands the editor. Results are
  disabled and hidden with search, measured and arranged below the entry only for an active query,
  and hidden after selection. Teardown detaches Shell, native-focus, and inherited search-control
  events. The toolbar preserves and restores custom `TitleView` content while search temporarily
  owns the content slot.

## Flyout ownership and appearance

The Shell controller's already-realized header, footer, and custom content views are used directly.
Headers have one owner:

- `Scroll` and `CollapseOnScroll` place the header in the collection adaptor;
- fixed/default behavior places it in the fixed header slot.

Changing the behavior first releases the old owner, then realizes the new owner. Footer content is
fixed. While custom content is active, header/behavior updates never rebuild or overwrite the
generated collection; fixed header/footer slots still update independently. A scrolling header
uses the fixed slot as the supported fallback while arbitrary custom content owns the scrolling
surface, preserving exactly one header owner. Clearing custom flyout content unmounts and disposes
it, then restores the default collection.
The `FlyoutHeader`, `FlyoutHeaderTemplate`, `FlyoutFooter`, `FlyoutFooterTemplate`,
`FlyoutContent`, and `FlyoutContentTemplate` mapper keys all refresh these paths. The literal
`FlyoutItems` notification key refreshes generated content when nested visibility or menu state
changes.

Built-in flyout and tab rows bind title, icon, and enabled state through their current
`BindingContext`, so recycled rows do not retain an explicit source from a previous item. Selected
and unselected colors bind to one shared `TizenItemAppearance` instance and update live.
Programmatic item, section, and content navigation and adaptor rebuilds resolve the most-specific
generated flyout entry and resynchronize native selection. User selection awaits the public
`IShellController.OnFlyoutItemSelectedAsync` path before checking the resulting hierarchy, so a
canceled navigation or a `MenuItem` action cannot leave a false native selection.

## Items, grouping, and selection

`TizenGroupItemSource` is the production flattened grouped source. It implements non-generic
`IList` plus `INotifyCollectionChanged`, which keeps the pinned `ItemAdaptor` on the live source
instead of snapshotting it. It observes both the outer group collection and every observable inner
group and translates add, remove, replace, move, and reset notifications into flattened indexes;
all moves and multi-row replacements emit `Reset`, matching the pinned native adaptor's supported
incremental operations. Disposal is checked before every callback and rebuild, including between
listeners when disposal occurs inside an in-flight reset. Group header rows exist only when a
`GroupHeaderTemplate` exists.
Grouped `ScrollTo` resolves both `(GroupIndex, Index)` and `(Group, Item)` requests to absolute rows,
including configured group decorations.

Native selection events preserve raw indexes. Header/footer indexes are rejected and unselected
before managed items are projected. Managed selection uses set differences in both directions;
`null` clears native selection, and the last valid index is restored only in single-selection mode
after an invalid native row was actually rejected. Selection is reapplied after every adaptor
replacement and queued after observable source mutations, guarded by adaptor generation so stale
work cannot target a replacement. Native selection callbacks are suppressed while selection mode
and adaptor ownership are configured, and stale out-of-range native indexes are explicitly cleared.

Adaptor replacement follows one ownership order:

1. detach `NativeCollectionView.Adaptor`;
2. unsubscribe the old adaptor;
3. dispose it and its realized row handlers;
4. subscribe the replacement;
5. install it;
6. reapply selection.

Disconnect uses MAUI's captured `platformView`, detaches its adaptor before disposal, and skips
install-time selection synchronization during teardown. The sequence is exception-safe through
`OwnedReplacementCoordinator`. Reused native controls call
`Rebind` before mapper updates. Grouped and empty adaptors retain global header/footer support via
explicit `TizenHeaderFooterPresenter` ownership. Empty content measures against the allocated
viewport remaining after global header/footer decorations rather than propagating infinite scroll
constraints. A header/footer-only empty source retains a placeholder extent, and empty content uses
the full grid cross-axis instead of one grid cell.

Native `Scrolled` events publish MAUI `ItemsViewScrolledEventArgs` and remaining-item threshold
notifications. `ItemsLayout` changes are observed through disposal/rebind; span, item spacing,
orientation, snap type, and snap alignment update at runtime. Scrollbar visibility follows the
active native layout orientation.

## Carousel feedback

Carousel native scrolling updates both `Position` and `CurrentItem`. Managed updates and their
deferred native echoes are separated by `CarouselFeedbackCoordinator`, preventing recursive or
stale feedback; managed changes update their companion property before the native push. Initial
`Position`/`CurrentItem` and its animation choice are retained until both an adaptor and non-zero
layout bounds exist, then retried after layout, resize/rotation, adaptor, and observable-source
changes. Visible rows receive `CurrentItem`, `PreviousItem`, `NextItem`, and `DefaultItem` visual
states and maintain `VisibleViews`. Drag and animation events jointly keep `IsDragging` and
`IsScrolling` truthful. `IsSwipeEnabled` controls the native scroll input switch. Event
subscriptions are symmetrical across connect, rebind, disconnect, and disposal.

Animated navigation pops rely on `TizenNaviPage.Dispose` to detach handler-owned content before the
native wrapper destroys its children; no wrapper is accessed after `NavigationStack.Pop(true)`
returns. Navigation handler rebinds disconnect the old manager, suppress mapper side effects until
the new virtual view is installed, then reconnect and resynchronize stack and toolbar. Request
generations suppress stale completion callbacks. `TabbedPage` observes `PagesChanged`, rebuilds and
reselects after moves, tracks every realized page handler so removed pages are disposed, and never
creates handlers during disconnect.

## Mapper parity and source closure

`docs/wave-c-mapper-parity.json` and its Markdown companion are generated from the current source.
The parser recognizes:

- `nameof(...)` keys;
- string literals;
- local constants;
- member-access keys such as `Shell.TabBarIsVisibleProperty.PropertyName`.

Property and command mapper coverage are evaluated independently. The synthetic
`DrawerToggleVisible` string is not declared as a mapper key because the pinned MAUI packages do
not publish that property; the adapter is driven by real flyout/back/toolbar state changes instead.

The shipping Controls project and `Maui.Tizen.Controls.RefPackCompile` import the same explicit
Wave C source manifest. Raw imported sources remain as provenance and are never compiled.

## Validation

Host-executable production helpers cover root mounting, bidirectional feedback, proposal rejection,
toolbar ownership, adaptor replacement, grouped notifications, scroll thresholds, selection
projection, and appearance changes. Core host tests build the real
`UseMauiAppTizenControls<TApp>` path, resolve all concrete Wave C registrations, and execute the
Shell, ShellItem, ShellSection, and NavigationPage production handlers/mappers. Metadata tests also
inspect the actual pinned `Tizen.UIExtensions.NUI.ItemAdaptor` implementation to prove its
non-generic `IList` retention and `INotifyCollectionChanged` subscription behavior.

`eng/tests/wave-c-mutations.json` contains 51 mutations executed by a lock-protected runner. It
proves tests fail for omissions in:

- Shell root mounting;
- toolbar transfer ordering;
- tab proposal restoration;
- recycled-row unregistration;
- grouped inner collection observation;
- raw-index selection filtering;
- adaptor replacement ordering;
- carousel feedback;
- member-access mapper parsing;
- command mapper coverage;
- non-generic grouped-source retention;
- grouped disposal and reset normalization;
- captured-platform disconnect;
- adaptor setup feedback suppression and stale-index cleanup;
- Shell row category/measurement;
- custom flyout ownership and `FlyoutItems` dispatch;
- asynchronous flyout resynchronization;
- search/title-slot ownership, focus, disabled/results behavior, and layout;
- public menu activation and overflow routing;
- synchronous selection echoes;
- Carousel companion properties, visual states, interaction state, resize retry, and animation;
- runtime items-layout/snap changes and empty grid/decorated viewport behavior;
- NavigationPage rebind and stale-request suppression;
- TabbedPage move/removal;
- animated-pop content detachment;
- Shell toolbar handler detachment;
- effective appearance values and null Shell content.

The canonical workload-free and hosted validation scripts run these checks along with Core, Wave B,
Wave C, source, PublicAPI, API15 RefPack, consumer, parity, and repository validation.

## External blockers and device-only gaps

- A bare `MenuItem` flyout template cannot reproduce the internal `MenuShellItem` relationship with
  the pinned package. dotnet/maui#37862 is merged upstream but not available in that package, so the
  typed resolver and expiry guard remain.
- dotnet/maui#37861, #37863, and #37864 are also merged upstream but absent from the pinned package.
  Typed adapters and expiry guards remain until the package is updated.
- The modal navigation seam (#37853) is not available in the pinned package and is not worked
  around with reflection or internals.
- The selection-state seam is not a hard blocker. The current public deferred recomputation path is
  host-executed and accepted until a packaged upstream API is available.
- Samsung's .NET 11 Tizen workload and device lab are unavailable. Native NUI rendering,
  virtualization/focus behavior, gestures, navigation animations, TV interaction, visual baselines,
  and final disposal timing remain device-only validation gaps.
