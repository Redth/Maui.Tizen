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
calls `RemapForControls` from each control's static constructor, mutating MAUI's static
handler mappers in place - adding `FormattedText`, `TextType`, `LineBreakMode`,
`MaxLines`, `TextTransform`, `CheckBox.Color` and the accessibility keys. Those
constructors are forced before these numbers are taken, so the table reflects what an
application actually sees rather than a Core-only subset.

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
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `Color` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
| `IsRunning` | mapped | tizen |

## TizenButtonHandler

Serves `IButton`; compared against MAUI's `ButtonHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `CornerRadius` | mapped | tizen |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
| `Padding` | mapped | tizen |
| `Source` | mapped | tizen |
| `StrokeColor` | mapped | tizen |
| `StrokeThickness` | mapped | tizen |
| `Text` | mapped | tizen |
| `TextColor` | mapped | tizen |

## TizenCheckBoxHandler

Serves `ICheckBox`; compared against MAUI's `CheckBoxHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `Color` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Foreground` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsChecked` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |

## TizenDatePickerHandler

Serves `IDatePicker`; compared against MAUI's `DatePickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `Date` | mapped | tizen |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `Format` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
| `IsOpen` | mapped | tizen |
| `MaximumDate` | mapped | tizen |
| `MinimumDate` | mapped | tizen |
| `TextColor` | mapped | tizen |

## TizenEditorHandler

Serves `IEditor`; compared against MAUI's `EditorHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `CursorPosition` | mapped | tizen |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |
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
| `VerticalTextAlignment` | mapped | tizen |

## TizenEntryHandler

Serves `IEntry`; compared against MAUI's `EntryHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ClearButtonVisibility` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `CursorPosition` | mapped | tizen |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |
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
| `VerticalTextAlignment` | mapped | tizen |

## TizenPickerHandler

Serves `IPicker`; compared against MAUI's `PickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |
| `IsOpen` | mapped | tizen |
| `Items` | mapped | tizen |
| `SelectedIndex` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `Title` | mapped | tizen |
| `TitleColor` | mapped | tizen |
| `VerticalTextAlignment` | mapped | tizen |

## TizenProgressBarHandler

Serves `IProgress`; compared against MAUI's `ProgressBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
| `Progress` | mapped | tizen |
| `ProgressColor` | mapped | tizen |

## TizenRadioButtonHandler

Serves `IRadioButton`; compared against MAUI's `RadioButtonHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `Content` | mapped | tizen |
| `CornerRadius` | mapped | tizen |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsChecked` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |
| `StrokeColor` | mapped | tizen |
| `StrokeThickness` | mapped | tizen |
| `TextColor` | mapped | tizen |

## TizenSearchBarHandler

Serves `ISearchBar`; compared against MAUI's `SearchBarHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CancelButtonColor` | mapped | tizen |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `CursorPosition` | mapped | tizen |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `HorizontalTextAlignment` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |
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
| `VerticalTextAlignment` | mapped | tizen |

## TizenSliderHandler

Serves `ISlider`; compared against MAUI's `SliderHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
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
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `Interval` | mapped | tizen |
| `IsInAccessibleTree` | mapped | inherited |
| `Maximum` | mapped | tizen |
| `Minimum` | mapped | tizen |
| `Value` | mapped | tizen |

## TizenSwitchHandler

Serves `ISwitch`; compared against MAUI's `SwitchHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
| `IsOn` | mapped | tizen |
| `ThumbColor` | mapped | tizen |
| `TrackColor` | mapped | tizen |

## TizenTimePickerHandler

Serves `ITimePicker`; compared against MAUI's `TimePickerHandler`.

| Key | MAUI | Tizen |
|---|---|---|
| `BackgroundColor` | mapped | inherited |
| `BackgroundImageSource` | mapped | inherited |
| `Border` | mapped | excluded |
| `CharacterSpacing` | mapped | tizen |
| `ContainerView` | mapped | excluded |
| `Description` | mapped | inherited |
| `ExcludedWithChildren` | mapped | inherited |
| `Font` | mapped | tizen |
| `Format` | mapped | tizen |
| `HeadingLevel` | mapped | inherited |
| `Hint` | mapped | inherited |
| `IsInAccessibleTree` | mapped | inherited |
| `IsOpen` | mapped | tizen |
| `TextColor` | mapped | tizen |
| `Time` | mapped | tizen |

