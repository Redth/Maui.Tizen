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
| unsupported | The backend explicitly maps the key to a documented no-op. |
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

`TextTransform`, `ContentLayout` and `Button.LineBreakMode` remain `inherited` after
re-measuring the compiled Controls bridge. They are Controls properties that upstream
applies from `Microsoft.Maui.Controls.Platform`, not from the Core interfaces consumed
by these handlers. The shipping `Maui.Tizen.Controls` assembly currently compiles only
its startup/mapping bridge. That bridge maps **Label** `LineBreakMode` and accessibility;
it does not map **Button** `LineBreakMode`, `ContentLayout`, or `TextTransform`, and the
raw imported files that mention those keys remain outside the compile closure.
`ControlsLayerFollowUpMatchesCompiledBridge` pins that closure so adding an implementation
forces this matrix to be re-measured.

## Intentional no-op mappings

These entries are reachable, but their Tizen mapper bodies are explicitly empty. The
classification is compared with the source by
`UnsupportedMapperClassificationTests`: adding an empty mapper without evidence, or
implementing one without removing it from this list, fails the test.

| Owner | Kind | Key | Evidence |
|---|---|---|---|
| `TizenApplicationHandler` | command | `OpenWindow` | Tizen exposes one NUI window per process, so another window cannot be opened. |
| `TizenDatePickerHandler` | property | `IsOpen` | IDatePicker.IsOpen is internal and cannot be read by an out-of-tree backend. |
| `TizenLabelHandler` | property | `Padding` | dotnet/maui marks the Tizen label padding mapper as MissingMapper. |
| `TizenPageHandler` | property | `Title` | dotnet/maui marks the base Tizen page title mapper as MissingMapper. |
| `TizenPickerHandler` | property | `IsOpen` | IPicker.IsOpen is internal and cannot be read by an out-of-tree backend. |
| `TizenRadioButtonHandler` | property | `CharacterSpacing` | Text styling belongs to the templated content's own label handler. |
| `TizenRadioButtonHandler` | property | `Font` | Text styling belongs to the templated content's own label handler. |
| `TizenRadioButtonHandler` | property | `IsChecked` | The checked visual is supplied by the radio button content template, not a native indicator. |
| `TizenRadioButtonHandler` | property | `TextColor` | Text styling belongs to the templated content's own label handler. |
| `TizenSearchBarHandler` | property | `CancelButtonColor` | The Tizen search bar has no cancel affordance to tint. |
| `TizenSearchBarHandler` | property | `SearchIconColor` | The search icon drawable exposes no tint property. |
| `TizenTimePickerHandler` | property | `IsOpen` | ITimePicker.IsOpen is internal and cannot be read by an out-of-tree backend. |
| `TizenViewMappers` | property | `MaximumHeight` | NUI MaximumSize does not behave correctly; dotnet/maui leaves this mapping empty. |
| `TizenViewMappers` | property | `MaximumWidth` | NUI MaximumSize does not behave correctly; dotnet/maui leaves this mapping empty. |
| `TizenViewMappers` | property | `ToolTip` | NUI has no tooltip primitive; dotnet/maui leaves this mapping empty. |
| `TizenWindowHandler` | property | `Title` | dotnet/maui leaves the Tizen window title mapper empty. |

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
| `MaximumHeight` | unsupported |
| `MaximumWidth` | unsupported |
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
| `ToolTip` | unsupported |
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
| `IsOpen` | mapped | unsupported |
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
| `IsOpen` | mapped | unsupported |
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
| `CharacterSpacing` | mapped | unsupported |
| `Content` | mapped | tizen |
| `CornerRadius` | mapped | tizen |
| `Font` | mapped | unsupported |
| `IsChecked` | mapped | unsupported |
| `StrokeColor` | mapped | tizen |
| `StrokeThickness` | mapped | tizen |
| `TextColor` | mapped | unsupported |

## TizenSearchBarHandler

Serves `ISearchBar`; compared against MAUI's `SearchBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `CancelButtonColor` | mapped | unsupported |
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
| `SearchIconColor` | mapped | unsupported |
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
| `IsOpen` | mapped | unsupported |
| `TextColor` | mapped | tizen |
| `Time` | mapped | tizen |
