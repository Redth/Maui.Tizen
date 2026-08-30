using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Host-buildable stand-ins for the Wave A control platform views.
	/// </summary>
	/// <remarks>
	/// These mirror the real types in <c>src/Maui.Tizen.Core/Platform/Tizen/TizenControlViews.cs</c>
	/// one-for-one. They carry no behaviour: every handler body that touches NUI is wrapped in
	/// <c>#if TIZEN</c>, which is not defined here, so nothing ever calls into them. Their only
	/// job is to give the handler generic parameters a type that exists on a plain net11.0 host,
	/// so mappers, registration and command dispatch can genuinely be executed by tests.
	/// </remarks>
	public class TizenButtonView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>Entry</c> platform view.</summary>
	public class TizenEntryView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>Editor</c> platform view.</summary>
	public class TizenEditorView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>CheckBox</c> platform view.</summary>
	public class TizenCheckBoxView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>Switch</c> platform view.</summary>
	public class TizenSwitchView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>ProgressBar</c> platform view.</summary>
	public class TizenProgressBarView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>ActivityIndicator</c> platform view.</summary>
	public class TizenActivityIndicatorView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>Slider</c> platform view.</summary>
	public class TizenSliderView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>Stepper</c> platform view.</summary>
	public class TizenStepperView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>SearchBar</c> platform view.</summary>
	public class TizenSearchBarView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the picker entry platform view.</summary>
	/// <remarks>Shared by <c>Picker</c>, <c>DatePicker</c> and <c>TimePicker</c>.</remarks>
	public class TizenPickerView : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for the <c>RadioButton</c> platform view.</summary>
	public class TizenRadioButtonView : TizenContentViewGroup
	{
		/// <param name="view">The cross-platform view being presented.</param>
		public TizenRadioButtonView(IView? view)
			: base(view)
		{
		}
	}
}
