# Mapper parity matrix

<!--
  GENERATED FILE - do not edit by hand.

  Produced from the shipped mappers by
  tests/Maui.Tizen.Core.UnitTests/MapperParityMatrixTests.cs. Regenerate with:

    MAUI_TIZEN_UPDATE_PARITY_MATRIX=1 dotnet test tests/Maui.Tizen.Core.UnitTests
-->

Every property MAUI can push at a handler, and what this backend does with it.
Generated from the real mappers, so it cannot drift from the code.

**Parity is measured against MAUI Controls, not Core alone.** `Microsoft.Maui.Controls`
calls `RemapForControls` for each control, mutating MAUI's static handler mappers in
place - adding `FormattedText`, `TextType`, `LineBreakMode`, `MaxLines`,
`TextTransform`, `CheckBox.Color`, `Picker.ItemsSource`, `Stepper.Increment` and the
accessibility keys.

Only `Label` and `CheckBox` remap from a static constructor. Every other control here
is remapped by `ConfigureControls` when a `MauiApp` is **built**, so these numbers are
taken after building a real Controls host (`ControlsRemap.Force`). Running class
constructors alone would leave most mappers un-remapped and quietly report a
Core-only subset instead of what an application sees.

| Legend | Meaning |
|---|---|
| tizen | The backend supplies a Tizen implementation. |
| inherited | The key resolves through MAUI's chained mapper, but its body is the
| | off-platform no-op - so nothing happens on Tizen. Reachable, not implemented. |
| excluded | Deliberately not implemented, for a documented reason. |
| **MISSING** | Not reachable at all. Nothing should be in this state. |
| n/a | MAUI's handler does not define the key either. |

The `inherited` distinction matters and is the reason this table is generated rather
than written: chaining MAUI's static mapper makes every key *resolve*, so a table that
only reported presence would show total parity while most properties did nothing.

Two keys are `excluded` throughout, both inherited from the core slice's base mapper:

- `ContainerView` - `ViewHandler.ContainerView` has a `private protected` setter, so an
  out-of-repo backend cannot publish a container view it constructs. Background, clip and
  shadow are rendered onto the platform view instead (`NeedsContainer => false`). Tracked
  upstream by dotnet/maui#37854; re-measure this key when that lands.
- `Border` - the obsolete `IBorder.Border` mapping. MAUI marks the property `[Obsolete]`
  and states it will be removed; border rendering is driven by the stroke and shape
  properties that replaced it.

`TextTransform`, `ContentLayout` and `Button.LineBreakMode` are `inherited`, and that is
a deliberate answer to a review question rather than an oversight. They are properties
of **Controls** types, not of the `Microsoft.Maui.*` interfaces this package consumes,
and upstream applies them from `Microsoft.Maui.Controls.Platform` rather than from a Core
handler - implementing them here would mean referencing Controls from the product
package, which this repository does not do. Matching sources do exist under
`src/Maui.Tizen.Controls`, but **that project is in no compiled lane**, so they are
unbuilt, unexecuted and untested. An earlier revision gave them a distinct `controls`
state on the strength of existing on disk; that overstated reality, because source
nobody compiles cannot be known to work. They are therefore reported exactly as what
they are today - reachable and inert - and `MapperParityMatrixTests` fails if the
project ever gains a lane, so the question gets revisited on evidence.

## Common view properties

Inherited by every control below through `TizenViewMappers.ViewMapper`, the
Tizen-owned base mapper. Chaining MAUI's neutral `ViewHandler.ViewMapper` instead would
register every key while doing nothing, because its bodies are the off-platform no-ops.

| Key | Status |
|---|---|
| `AnchorX` | mapped |
| `AnchorY` | mapped |
| `AutomationId` | mapped |
| `Background` | mapped |
| `BackgroundColor` | mapped |
| `BackgroundImageSource` | mapped |
| `Border` | mapped |
| `Clip` | mapped |
| `ContainerView` | mapped |
| `Description` | mapped |
| `ExcludedWithChildren` | mapped |
| `FlowDirection` | mapped |
| `Frame` | mapped |
| `HeadingLevel` | mapped |
| `Height` | mapped |
| `Hint` | mapped |
| `InputTransparent` | mapped |
| `IsEnabled` | mapped |
| `IsInAccessibleTree` | mapped |
| `MaximumHeight` | mapped |
| `MaximumWidth` | mapped |
| `MinimumHeight` | mapped |
| `MinimumWidth` | mapped |
| `Opacity` | mapped |
| `Rotation` | mapped |
| `RotationX` | mapped |
| `RotationY` | mapped |
| `Scale` | mapped |
| `ScaleX` | mapped |
| `ScaleY` | mapped |
| `Semantics` | mapped |
| `Shadow` | mapped |
| `ToolTip` | mapped |
| `TranslationX` | mapped |
| `TranslationY` | mapped |
| `Visibility` | mapped |
| `Width` | mapped |

## TizenActivityIndicatorHandler

Serves `IActivityIndicator`; compared against MAUI's `ActivityIndicatorHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Color` | mapped | tizen |
| `IsRunning` | mapped | tizen |

## TizenButtonHandler

Serves `IButton`; compared against MAUI's `ButtonHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `ContentLayout` | mapped | inherited |
| `CornerRadius` | mapped | tizen |
| `Font` | mapped | tizen |
| `LineBreakMode` | mapped | inherited |
| `Padding` | mapped | tizen |
| `Source` | mapped | tizen |
| `StrokeColor` | mapped | tizen |
| `StrokeThickness` | mapped | tizen |
| `Text` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `TextTransform` | mapped | inherited |

## TizenCheckBoxHandler

Serves `ICheckBox`; compared against MAUI's `CheckBoxHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Color` | mapped | tizen |
| `Foreground` | mapped | tizen |
| `IsChecked` | mapped | tizen |

## TizenDatePickerHandler

Serves `IDatePicker`; compared against MAUI's `DatePickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `Date` | mapped | tizen |
| `Font` | mapped | tizen |
| `Format` | mapped | tizen |
| `IsOpen` | mapped | tizen |
| `MaximumDate` | mapped | tizen |
| `MinimumDate` | mapped | tizen |
| `TextColor` | mapped | tizen |

## TizenEditorHandler

Serves `IEditor`; compared against MAUI's `EditorHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `CursorPosition` | mapped | tizen |
| `Font` | mapped | tizen |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsReadOnly` | mapped | tizen |
| `IsSpellCheckEnabled` | mapped | tizen |
| `IsTextPredictionEnabled` | mapped | tizen |
| `Keyboard` | mapped | tizen |
| `MaxLength` | mapped | tizen |
| `Placeholder` | mapped | tizen |
| `PlaceholderColor` | mapped | tizen |
| `SelectionLength` | mapped | tizen |
| `Text` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `TextTransform` | mapped | inherited |
| `VerticalTextAlignment` | mapped | tizen |

## TizenEntryHandler

Serves `IEntry`; compared against MAUI's `EntryHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `ClearButtonVisibility` | mapped | tizen |
| `CursorPosition` | mapped | tizen |
| `Font` | mapped | tizen |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsPassword` | mapped | tizen |
| `IsReadOnly` | mapped | tizen |
| `IsSpellCheckEnabled` | mapped | tizen |
| `IsTextPredictionEnabled` | mapped | tizen |
| `Keyboard` | mapped | tizen |
| `MaxLength` | mapped | tizen |
| `Placeholder` | mapped | tizen |
| `PlaceholderColor` | mapped | tizen |
| `ReturnType` | mapped | tizen |
| `SelectionLength` | mapped | tizen |
| `Text` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `TextTransform` | mapped | inherited |
| `VerticalTextAlignment` | mapped | tizen |

## TizenPickerHandler

Serves `IPicker`; compared against MAUI's `PickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `Font` | mapped | tizen |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsOpen` | mapped | tizen |
| `Items` | mapped | tizen |
| `ItemsSource` | mapped | tizen |
| `SelectedIndex` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `Title` | mapped | tizen |
| `TitleColor` | mapped | tizen |
| `VerticalTextAlignment` | mapped | tizen |

## TizenProgressBarHandler

Serves `IProgress`; compared against MAUI's `ProgressBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Progress` | mapped | tizen |
| `ProgressColor` | mapped | tizen |

## TizenRadioButtonHandler

Serves `IRadioButton`; compared against MAUI's `RadioButtonHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `Content` | mapped | tizen |
| `CornerRadius` | mapped | tizen |
| `Font` | mapped | tizen |
| `IsChecked` | mapped | tizen |
| `StrokeColor` | mapped | tizen |
| `StrokeThickness` | mapped | tizen |
| `TextColor` | mapped | tizen |

## TizenSearchBarHandler

Serves `ISearchBar`; compared against MAUI's `SearchBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CancelButtonColor` | mapped | tizen |
| `CharacterSpacing` | mapped | tizen |
| `CursorPosition` | mapped | tizen |
| `Font` | mapped | tizen |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsReadOnly` | mapped | tizen |
| `IsSpellCheckEnabled` | mapped | tizen |
| `IsTextPredictionEnabled` | mapped | tizen |
| `Keyboard` | mapped | tizen |
| `MaxLength` | mapped | tizen |
| `Placeholder` | mapped | tizen |
| `PlaceholderColor` | mapped | tizen |
| `ReturnType` | mapped | tizen |
| `SearchIconColor` | mapped | tizen |
| `SelectionLength` | mapped | tizen |
| `Text` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `TextTransform` | mapped | inherited |
| `VerticalTextAlignment` | mapped | tizen |

## TizenSliderHandler

Serves `ISlider`; compared against MAUI's `SliderHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Maximum` | mapped | tizen |
| `MaximumTrackColor` | mapped | tizen |
| `Minimum` | mapped | tizen |
| `MinimumTrackColor` | mapped | tizen |
| `ThumbColor` | mapped | tizen |
| `ThumbImageSource` | mapped | tizen |
| `Value` | mapped | tizen |

## TizenStepperHandler

Serves `IStepper`; compared against MAUI's `StepperHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Increment` | mapped | tizen |
| `Interval` | mapped | tizen |
| `Maximum` | mapped | tizen |
| `Minimum` | mapped | tizen |
| `Value` | mapped | tizen |

## TizenSwitchHandler

Serves `ISwitch`; compared against MAUI's `SwitchHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `IsOn` | mapped | tizen |
| `ThumbColor` | mapped | tizen |
| `TrackColor` | mapped | tizen |

## TizenTimePickerHandler

Serves `ITimePicker`; compared against MAUI's `TimePickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CharacterSpacing` | mapped | tizen |
| `Font` | mapped | tizen |
| `Format` | mapped | tizen |
| `IsOpen` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `Time` | mapped | tizen |

