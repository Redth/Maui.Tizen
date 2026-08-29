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
  restore the authored selection. Managed selection updates suppress native echo callbacks.
- Hidden sections are filtered from the tab adaptor. Visibility and items changes rebuild the bar,
  while `ShowTabs == false` detaches an already-created bar without discarding section state.
- `TizenShellHandler` and `TizenNavigationViewHandler` both expose reachable toolbar mappings.
  Toolbar replacement unsubscribes before the owning Core container disposes the outgoing toolbar.
  Back navigation requires a visible, enabled back button.
- Search is represented by `TizenShellSearchView`, attached to `TizenToolbarView.SearchBar`. It
  follows the current page's effective `SearchHandler`, query, list proxy, item template, enabled
  state, visibility, query confirmation, and item selection, and detaches all subscriptions on
  replacement or teardown.

## Flyout ownership and appearance

The Shell controller's already-realized header, footer, and custom content views are used directly.
Headers have one owner:

- `Scroll` and `CollapseOnScroll` place the header in the collection adaptor;
- fixed/default behavior places it in the fixed header slot.

Changing the behavior first releases the old owner, then realizes the new owner. Footer content is
fixed. Clearing custom flyout content unmounts and disposes it, then restores the default collection.
The `FlyoutHeader`, `FlyoutHeaderTemplate`, `FlyoutFooter`, `FlyoutFooterTemplate`,
`FlyoutContent`, and `FlyoutContentTemplate` mapper keys all refresh these paths.

Built-in flyout and tab rows bind title, icon, and enabled state through their current
`BindingContext`, so recycled rows do not retain an explicit source from a previous item. Selected
and unselected colors bind to one shared `TizenItemAppearance` instance and update live.
Programmatic `Shell.CurrentItem` changes and adaptor rebuilds resynchronize native selection.

## Items, grouping, and selection

`TizenGroupItemSource` is the production flattened grouped source. It observes both the outer group
collection and every observable inner group and translates add, remove, replace, move, and reset
notifications into flattened indexes. Grouped `ScrollTo` resolves both `(GroupIndex, Index)` and
`(Group, Item)` requests to absolute rows, including group headers and optional footers.

Native selection events preserve raw indexes. Header/footer indexes are rejected and unselected
before managed items are projected. Managed selection uses set differences in both directions;
`null` clears native selection, and the last valid index is restored for `SingleAlways`.
Selection is reapplied after every adaptor replacement and after source changes.

Adaptor replacement follows one ownership order:

1. detach `NativeCollectionView.Adaptor`;
2. unsubscribe the old adaptor;
3. dispose it and its realized row handlers;
4. subscribe the replacement;
5. install it;
6. reapply selection.

The sequence is exception-safe through `OwnedReplacementCoordinator`. Reused native controls call
`Rebind` before mapper updates. Grouped and empty adaptors retain global header/footer support via
explicit `TizenHeaderFooterPresenter` ownership. Empty content measures against the allocated
viewport rather than propagating infinite scroll constraints.

Native `Scrolled` events publish MAUI `ItemsViewScrolledEventArgs` and remaining-item threshold
notifications. Scrollbar visibility follows the active layout orientation.

## Carousel feedback

Carousel native scrolling updates both `Position` and `CurrentItem`. Managed updates and their
deferred native echoes are separated by `CarouselFeedbackCoordinator`, preventing recursive or
stale feedback. `IsSwipeEnabled` controls the native scroll input switch. Event subscriptions are
symmetrical across connect, rebind, disconnect, and disposal.

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
ShellItem and ShellSection current-item mappers.

`eng/tests/wave-c-mutations.json` is executed by a lock-protected mutation runner. It proves tests
fail for omissions in:

- Shell root mounting;
- toolbar transfer ordering;
- tab proposal restoration;
- recycled-row unregistration;
- grouped inner collection observation;
- raw-index selection filtering;
- adaptor replacement ordering;
- carousel feedback;
- member-access mapper parsing;
- command mapper coverage.

The canonical workload-free and hosted validation scripts run these checks along with Core, Wave B,
Wave C, source, PublicAPI, API15 RefPack, consumer, parity, and repository validation.

## External blockers and device-only gaps

- A bare `MenuItem` flyout template cannot reproduce the internal `MenuShellItem` relationship with
  the pinned package. dotnet/maui#37862 is merged upstream but not available in that package, so the
  typed resolver and expiry guard remain.
- dotnet/maui#37861, #37863, and #37864 are also merged upstream but absent from the pinned package.
  Typed adapters and expiry guards remain until the package is updated.
- The modal navigation seam (#37853), generic handler contracts (#37855), and Blazor APIs (#37858)
  are not available in the pinned package and are not worked around with reflection or internals.
- The selection-state seam is not a hard blocker. The current public deferred recomputation path is
  host-executed and accepted until a packaged upstream API is available.
- Samsung's .NET 11 Tizen workload and device lab are unavailable. Native NUI rendering,
  virtualization/focus behavior, gestures, navigation animations, TV interaction, visual baselines,
  and final disposal timing remain device-only validation gaps.
