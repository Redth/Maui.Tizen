// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Font = Microsoft.Maui.Font;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Stub virtual views for the control handlers.
	/// </summary>
	/// <remarks>
	/// These extend the core slice's <see cref="StubView"/> so the common <see cref="IView"/>
	/// surface stays defined in exactly one place. Each adds only the members its own control
	/// interface requires, with settable properties where a test needs to drive a value in or
	/// observe one being written back by an event proxy.
	/// </remarks>
	public static class StubViews
	{
		static readonly Dictionary<Type, Func<IView>> Factories = new()
		{
			[typeof(IActivityIndicator)] = static () => new StubActivityIndicator(),
			[typeof(IButton)] = static () => new StubButton(),
			[typeof(ICheckBox)] = static () => new StubCheckBox(),
			[typeof(IDatePicker)] = static () => new StubDatePicker(),
			[typeof(IEditor)] = static () => new StubEditor(),
			[typeof(IEntry)] = static () => new StubEntry(),
			[typeof(IPicker)] = static () => new StubPicker(),
			[typeof(IProgress)] = static () => new StubProgress(),
			[typeof(IRadioButton)] = static () => new StubRadioButton(),
			[typeof(ISearchBar)] = static () => new StubSearchBar(),
			[typeof(ISlider)] = static () => new StubSlider(),
			[typeof(IStepper)] = static () => new StubStepper(),
			[typeof(ISwitch)] = static () => new StubSwitch(),
			[typeof(ITimePicker)] = static () => new StubTimePicker(),
		};

		/// <summary>Creates a stub implementing <paramref name="virtualViewType"/>.</summary>
		/// <exception cref="InvalidOperationException">No stub is registered for the type.</exception>
		public static IView For(Type virtualViewType) =>
			Factories.TryGetValue(virtualViewType, out var factory)
				? factory()
				: throw new InvalidOperationException(
					$"No stub view is registered for {virtualViewType.Name}. Add one to {nameof(StubViews)}.");
	}

	/// <summary>Text-style boilerplate shared by the text-bearing stubs.</summary>
	public abstract class StubTextStyleView : StubView, ITextStyle
	{
		public Color TextColor { get; set; } = Colors.Black;

		public Font Font { get; set; } = Font.Default;

		public double CharacterSpacing { get; set; }
	}

	/// <summary>Text-input boilerplate shared by the entry-like stubs.</summary>
	public abstract class StubTextInputView : StubTextStyleView, ITextInput
	{
		public string Text { get; set; } = string.Empty;

		public string Placeholder { get; set; } = string.Empty;

		public Color PlaceholderColor { get; set; } = Colors.Grey;

		public bool IsReadOnly { get; set; }

		public bool IsTextPredictionEnabled { get; set; }

		public bool IsSpellCheckEnabled { get; set; }

		public int MaxLength { get; set; } = int.MaxValue;

		public Keyboard Keyboard { get; set; } = Keyboard.Default;

		public int CursorPosition { get; set; }

		public int SelectionLength { get; set; }

		public TextAlignment HorizontalTextAlignment { get; set; } = TextAlignment.Start;

		public TextAlignment VerticalTextAlignment { get; set; } = TextAlignment.Center;
	}

	public sealed class StubActivityIndicator : StubView, IActivityIndicator
	{
		public bool IsRunning { get; set; }

		public Color Color { get; set; } = Colors.Blue;
	}

	public sealed class StubButton : StubTextStyleView, IButton, IText, IImageButton
	{
		/// <summary>Counts <see cref="IButton.Clicked"/> so event wiring can be asserted.</summary>
		public int ClickedCount { get; private set; }

		public int PressedCount { get; private set; }

		public int ReleasedCount { get; private set; }

		public string Text { get; set; } = string.Empty;

		public Thickness Padding { get; set; } = Thickness.Zero;

		public Color StrokeColor { get; set; } = Colors.Transparent;

		public double StrokeThickness { get; set; }

		public int CornerRadius { get; set; } = -1;

		public IImageSource? Source { get; set; }

		public bool IsOpaque => false;

		public Aspect Aspect => Aspect.AspectFit;

		public bool IsAnimationPlaying => false;

		public bool IsLoading { get; private set; }

		public void Clicked() => ClickedCount++;

		public void Pressed() => PressedCount++;

		public void Released() => ReleasedCount++;

		public void UpdateIsLoading(bool isLoading) => IsLoading = isLoading;

		void IImageSourcePart.UpdateIsLoading(bool isLoading) => IsLoading = isLoading;
	}

	public sealed class StubCheckBox : StubView, ICheckBox
	{
		public bool IsChecked { get; set; }

		public Paint? Foreground { get; set; }
	}

	public sealed class StubDatePicker : StubTextStyleView, IDatePicker
	{
		public string Format { get; set; } = "d";

		public DateTime? Date { get; set; } = new DateTime(2026, 1, 1);

		public DateTime? MinimumDate { get; set; } = new DateTime(1900, 1, 1);

		public DateTime? MaximumDate { get; set; } = new DateTime(2100, 12, 31);
	}

	public sealed class StubEditor : StubTextInputView, IEditor
	{
		/// <summary>Counts <see cref="IEditor.Completed"/> so focus-loss wiring can be asserted.</summary>
		public int CompletedCount { get; private set; }

		public void Completed() => CompletedCount++;
	}

	public sealed class StubEntry : StubTextInputView, IEntry
	{
		public int CompletedCount { get; private set; }

		public bool IsPassword { get; set; }

		public ReturnType ReturnType { get; set; } = ReturnType.Default;

		public ClearButtonVisibility ClearButtonVisibility { get; set; }

		public void Completed() => CompletedCount++;
	}

	public sealed class StubPicker : StubTextStyleView, IPicker
	{
		public IList<string> Items { get; } = new List<string>();

		public string Title { get; set; } = string.Empty;

		public Color TitleColor { get; set; } = Colors.Grey;

		public int SelectedIndex { get; set; } = -1;

		public TextAlignment HorizontalTextAlignment { get; set; } = TextAlignment.Start;

		public TextAlignment VerticalTextAlignment { get; set; } = TextAlignment.Center;

		public int GetCount() => Items.Count;

		public string GetItem(int index) => Items[index];
	}

	public sealed class StubProgress : StubView, IProgress
	{
		public double Progress { get; set; }

		public Color ProgressColor { get; set; } = Colors.Blue;
	}

	public sealed class StubRadioButton : StubTextStyleView, IRadioButton
	{
		public bool IsChecked { get; set; }

		public object? Content { get; set; }

		public IView? PresentedContent { get; set; }

		public Thickness Padding { get; set; } = Thickness.Zero;

		public Color StrokeColor { get; set; } = Colors.Transparent;

		public double StrokeThickness { get; set; }

		public int CornerRadius { get; set; } = -1;

		public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) => Size.Zero;

		public Size CrossPlatformArrange(Rect bounds) => bounds.Size;
	}

	public sealed class StubSearchBar : StubTextInputView, ISearchBar
	{
		/// <summary>Counts <see cref="ISearchBar.SearchButtonPressed"/>.</summary>
		public int SearchPressedCount { get; private set; }

		public Color CancelButtonColor { get; set; } = Colors.Grey;

		/// <remarks>
		/// Declared <c>internal</c> by MAUI, so it is implemented explicitly through the interface
		/// rather than as a public member.
		/// </remarks>
		Color ISearchBar.SearchIconColor => Colors.Grey;

		public ReturnType ReturnType { get; set; } = ReturnType.Search;

		public void SearchButtonPressed() => SearchPressedCount++;
	}

	public sealed class StubSlider : StubView, ISlider
	{
		public int DragStartedCount { get; private set; }

		public int DragCompletedCount { get; private set; }

		public double Minimum { get; set; }

		public double Maximum { get; set; } = 1;

		public double Value { get; set; }

		public Color MinimumTrackColor { get; set; } = Colors.Blue;

		public Color MaximumTrackColor { get; set; } = Colors.Grey;

		public Color ThumbColor { get; set; } = Colors.Blue;

		public IImageSource ThumbImageSource { get; set; } = null!;

		public void DragStarted() => DragStartedCount++;

		public void DragCompleted() => DragCompletedCount++;
	}

	public sealed class StubStepper : StubView, IStepper
	{
		public double Minimum { get; set; }

		public double Maximum { get; set; } = 100;

		public double Interval { get; set; } = 1;

		public double Value { get; set; }
	}

	public sealed class StubSwitch : StubView, ISwitch
	{
		public bool IsOn { get; set; }

		public Color TrackColor { get; set; } = Colors.Grey;

		public Color ThumbColor { get; set; } = Colors.White;
	}

	public sealed class StubTimePicker : StubTextStyleView, ITimePicker
	{
		public string Format { get; set; } = "t";

		public TimeSpan? Time { get; set; } = TimeSpan.FromHours(9);
	}
}
