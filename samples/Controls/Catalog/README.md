# Navigation and collection catalog

Catalog pages exercising the Wave C handlers: stack navigation, flyout, tabs, Shell,
CollectionView and CarouselView.

## Why these are plain C# and not XAML

XAML would require the MAUI build tasks and a full application project. Neither can run until
Samsung publishes `samsung.net.sdk.tizen.manifest-11.0.100` (see `eng/baselines.json` and
`docs/migration.md`), so a XAML catalog would be unverifiable text.

Written as plain C#, these pages are compiled by
`tests/Maui.Tizen.Controls.ConsumerCompile` against the API15-compiled
`Maui.Tizen.Controls` assembly and current MAUI references.
That does not prove they render correctly — nothing here has been executed on a device or emulator —
but it does prove the API they exercise exists and is public, which is the property this migration
is actually about.

## What each page is for

| Page | Exercises |
| --- | --- |
| `NavigationCatalogPage` | Push/pop, `InsertPageBefore`, primary and secondary toolbar items, title view round-tripping |
| `TabbedCatalogPage` | Tab selection, bar colours, per-tab content |
| `FlyoutCatalogPage` | Flyout behaviour and the toolbar drawer toggle |
| `CollectionViewCatalogPage` | Virtualization over 500 items, grouping, single/multi selection, header/footer, empty view, grid layout, live insert/remove |
| `CarouselViewCatalogPage` | Looping and position tracking |
| `ShellCatalogPage` | Flyout header/footer, menu items, shell items and sections, search handler, and **lazy** shell content via `ContentTemplate` |

A few of these are chosen specifically because they are the behaviours most likely to regress
silently during the migration:

- `CollectionViewCatalogPage` mutates the source while the view is realized, which is what shakes
  out adaptor/recycling desync.
- `ShellCatalogPage` uses `ContentTemplate` rather than `Content`, because eager shell content
  creation looks identical on screen and only shows up as a startup cost.
- `ShellCatalogPage` includes a bare `MenuItem` in the flyout, which is the known unresolvable
  template case described in `docs/waves/wave-c.md`.

## Not yet an app

There is no application project here yet. Wiring these pages into a runnable sample needs the
Tizen workload and a `Platforms/Tizen` host, which is tracked with the rest of the sample work
rather than in Wave C.
