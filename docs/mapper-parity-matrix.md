# Mapper parity matrix

<!--
  GENERATED FILE - do not edit by hand.

  Produced from the shipped mappers by
  tests/Maui.Tizen.Core.UnitTests/MapperParityMatrixTests.cs. Regenerate with:

    MAUI_TIZEN_UPDATE_PARITY_MATRIX=1 dotnet test tests/Maui.Tizen.Core.UnitTests
-->

Every property MAUI can push at a handler, and what this backend does with it.
Generated from the real mappers, so it cannot drift from the code.

| Legend | Meaning |
|---|---|
| mapped | The Tizen handler maps the key. |
| excluded | Deliberately not mapped, for a documented reason - see the note below the table. |
| **MISSING** | MAUI maps it and this backend does not. Nothing should be in this state. |
| n/a | MAUI's neutral handler does not define the key either. |

Two keys are `excluded` throughout, both inherited from the core slice's base mapper:

- `ContainerView` - `ViewHandler.ContainerView` has a `private protected` setter, so an
  out-of-repo backend cannot publish a container view it constructs. Background, clip and
  shadow are rendered onto the platform view instead (`NeedsContainer => false`).
- `Border` - the obsolete `IBorder.Border` mapping. MAUI marks the property `[Obsolete]`
  and states it will be removed; border rendering is driven by the stroke and shape
  properties that replaced it.

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
| `Clip` | mapped |
| `FlowDirection` | mapped |
| `Frame` | mapped |
| `Height` | mapped |
| `InputTransparent` | mapped |
| `IsEnabled` | mapped |
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
| `Border` | mapped | excluded |
| `Color` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `IsRunning` | mapped | mapped |

## TizenButtonHandler

Serves `IButton`; compared against MAUI's `ButtonHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `CornerRadius` | mapped | mapped |
| `Font` | mapped | mapped |
| `Padding` | mapped | mapped |
| `Source` | mapped | mapped |
| `StrokeColor` | mapped | mapped |
| `StrokeThickness` | mapped | mapped |
| `Text` | mapped | mapped |
| `TextColor` | mapped | mapped |

## TizenCheckBoxHandler

Serves `ICheckBox`; compared against MAUI's `CheckBoxHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Foreground` | mapped | mapped |
| `IsChecked` | mapped | mapped |

## TizenDatePickerHandler

Serves `IDatePicker`; compared against MAUI's `DatePickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `Date` | mapped | mapped |
| `Font` | mapped | mapped |
| `Format` | mapped | mapped |
| `IsOpen` | mapped | mapped |
| `MaximumDate` | mapped | mapped |
| `MinimumDate` | mapped | mapped |
| `TextColor` | mapped | mapped |

## TizenEditorHandler

Serves `IEditor`; compared against MAUI's `EditorHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `CursorPosition` | mapped | mapped |
| `Font` | mapped | mapped |
| `HorizontalTextAlignment` | mapped | mapped |
| `IsReadOnly` | mapped | mapped |
| `IsSpellCheckEnabled` | mapped | mapped |
| `IsTextPredictionEnabled` | mapped | mapped |
| `Keyboard` | mapped | mapped |
| `MaxLength` | mapped | mapped |
| `Placeholder` | mapped | mapped |
| `PlaceholderColor` | mapped | mapped |
| `SelectionLength` | mapped | mapped |
| `Text` | mapped | mapped |
| `TextColor` | mapped | mapped |
| `VerticalTextAlignment` | mapped | mapped |

## TizenEntryHandler

Serves `IEntry`; compared against MAUI's `EntryHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ClearButtonVisibility` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `CursorPosition` | mapped | mapped |
| `Font` | mapped | mapped |
| `HorizontalTextAlignment` | mapped | mapped |
| `IsPassword` | mapped | mapped |
| `IsReadOnly` | mapped | mapped |
| `IsSpellCheckEnabled` | mapped | mapped |
| `IsTextPredictionEnabled` | mapped | mapped |
| `Keyboard` | mapped | mapped |
| `MaxLength` | mapped | mapped |
| `Placeholder` | mapped | mapped |
| `PlaceholderColor` | mapped | mapped |
| `ReturnType` | mapped | mapped |
| `SelectionLength` | mapped | mapped |
| `Text` | mapped | mapped |
| `TextColor` | mapped | mapped |
| `VerticalTextAlignment` | mapped | mapped |

## TizenPickerHandler

Serves `IPicker`; compared against MAUI's `PickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `Font` | mapped | mapped |
| `HorizontalTextAlignment` | mapped | mapped |
| `IsOpen` | mapped | mapped |
| `Items` | mapped | mapped |
| `SelectedIndex` | mapped | mapped |
| `TextColor` | mapped | mapped |
| `Title` | mapped | mapped |
| `TitleColor` | mapped | mapped |
| `VerticalTextAlignment` | mapped | mapped |

## TizenProgressBarHandler

Serves `IProgress`; compared against MAUI's `ProgressBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Progress` | mapped | mapped |
| `ProgressColor` | mapped | mapped |

## TizenRadioButtonHandler

Serves `IRadioButton`; compared against MAUI's `RadioButtonHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `Content` | mapped | mapped |
| `CornerRadius` | mapped | mapped |
| `Font` | mapped | mapped |
| `IsChecked` | mapped | mapped |
| `StrokeColor` | mapped | mapped |
| `StrokeThickness` | mapped | mapped |
| `TextColor` | mapped | mapped |

## TizenSearchBarHandler

Serves `ISearchBar`; compared against MAUI's `SearchBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CancelButtonColor` | mapped | mapped |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `CursorPosition` | mapped | mapped |
| `Font` | mapped | mapped |
| `HorizontalTextAlignment` | mapped | mapped |
| `IsReadOnly` | mapped | mapped |
| `IsSpellCheckEnabled` | mapped | mapped |
| `IsTextPredictionEnabled` | mapped | mapped |
| `Keyboard` | mapped | mapped |
| `MaxLength` | mapped | mapped |
| `Placeholder` | mapped | mapped |
| `PlaceholderColor` | mapped | mapped |
| `ReturnType` | mapped | mapped |
| `SearchIconColor` | mapped | mapped |
| `SelectionLength` | mapped | mapped |
| `Text` | mapped | mapped |
| `TextColor` | mapped | mapped |
| `VerticalTextAlignment` | mapped | mapped |

## TizenSliderHandler

Serves `ISlider`; compared against MAUI's `SliderHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Maximum` | mapped | mapped |
| `MaximumTrackColor` | mapped | mapped |
| `Minimum` | mapped | mapped |
| `MinimumTrackColor` | mapped | mapped |
| `ThumbColor` | mapped | mapped |
| `ThumbImageSource` | mapped | mapped |
| `Value` | mapped | mapped |

## TizenStepperHandler

Serves `IStepper`; compared against MAUI's `StepperHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Interval` | mapped | mapped |
| `Maximum` | mapped | mapped |
| `Minimum` | mapped | mapped |
| `Value` | mapped | mapped |

## TizenSwitchHandler

Serves `ISwitch`; compared against MAUI's `SwitchHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `IsOn` | mapped | mapped |
| `ThumbColor` | mapped | mapped |
| `TrackColor` | mapped | mapped |

## TizenTimePickerHandler

Serves `ITimePicker`; compared against MAUI's `TimePickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | mapped |
| `ContainerView` | mapped | excluded |
| `Font` | mapped | mapped |
| `Format` | mapped | mapped |
| `IsOpen` | mapped | mapped |
| `TextColor` | mapped | mapped |
| `Time` | mapped | mapped |

