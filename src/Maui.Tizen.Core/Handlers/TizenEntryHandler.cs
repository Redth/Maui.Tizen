// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IEntry"/>.
	/// </summary>
	public class TizenEntryHandler : TizenViewHandler<IEntry, TizenEntryView>
	{
		/// <summary>The complete property mapper for <see cref="IEntry"/>.</summary>
		public static readonly IPropertyMapper<IEntry, TizenEntryHandler> Mapper =
			new PropertyMapper<IEntry, TizenEntryHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IEntry.Background)] = MapBackground,
				[nameof(IEntry.Text)] = MapText,
				[nameof(IEntry.TextColor)] = MapTextColor,
				[nameof(IEntry.IsPassword)] = MapIsPassword,
				[nameof(IEntry.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
				[nameof(IEntry.VerticalTextAlignment)] = MapVerticalTextAlignment,
				[nameof(IEntry.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
				[nameof(IEntry.IsSpellCheckEnabled)] = MapIsSpellCheckEnabled,
				[nameof(IEntry.MaxLength)] = MapMaxLength,
				[nameof(IEntry.Placeholder)] = MapPlaceholder,
				[nameof(IEntry.PlaceholderColor)] = MapPlaceholderColor,
				[nameof(IEntry.Font)] = MapFont,
				[nameof(IEntry.IsReadOnly)] = MapIsReadOnly,
				[nameof(IEntry.Keyboard)] = MapKeyboard,
				[nameof(IEntry.ReturnType)] = MapReturnType,
				[nameof(IEntry.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(IEntry.CursorPosition)] = MapCursorPosition,
				[nameof(IEntry.SelectionLength)] = MapSelectionLength,
				[nameof(IEntry.ClearButtonVisibility)] = MapClearButtonVisibility,
			};

		/// <summary>The complete command mapper for <see cref="IEntry"/>.</summary>
		public static readonly CommandMapper<IEntry, TizenEntryHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenEntryHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenEntryHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		/// <remarks>
		/// <c>FocusableInTouch</c> is required in addition to <c>Focusable</c>: without it the
		/// entry takes focus from the remote/keyboard but not from a tap, so tapping the field
		/// on a touch device would not raise the IME.
		/// </remarks>
		protected override TizenEntryView CreatePlatformView()
		{
#if TIZEN
			return new() { Focusable = true, FocusableInTouch = true };
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenEntryView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.KeyEvent += OnKeyEvent;
			platformView.TextChanged += OnTextChanged;
			platformView.CursorPositionChanged += OnCursorPositionChanged;
			platformView.SelectionChanged += OnSelectionChanged;
			platformView.SelectionCleared += OnSelectionCleared;
#endif
		}

		protected override void DisconnectHandler(TizenEntryView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.KeyEvent -= OnKeyEvent;
				platformView.TextChanged -= OnTextChanged;
				platformView.CursorPositionChanged -= OnCursorPositionChanged;
				platformView.SelectionChanged -= OnSelectionChanged;
				platformView.SelectionCleared -= OnSelectionCleared;
			}
#endif
			base.DisconnectHandler(platformView);
		}

#if TIZEN
		/// <remarks>
		/// An entry paints its own background, so the container has to be re-evaluated before
		/// the paint is applied or a gradient would be written to a wrapper that does not exist
		/// yet.
		/// </remarks>
		public static void MapBackground(TizenEntryHandler handler, IEntry entry)
		{
			handler.UpdateValue(nameof(IViewHandler.ContainerView));
			handler.PlatformView?.UpdateBackground(entry.Background);
		}
#endif

		public static void MapText(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateText(entry);
#endif
		}

		public static void MapTextColor(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextColor(entry);
#endif
		}

		public static void MapIsPassword(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsPassword(entry);
#endif
		}

		public static void MapHorizontalTextAlignment(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateHorizontalTextAlignment(entry);
#endif
		}

		public static void MapVerticalTextAlignment(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateVerticalTextAlignment(entry);
#endif
		}

		public static void MapIsTextPredictionEnabled(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsTextPredictionEnabled(entry);
#endif
		}

		public static void MapIsSpellCheckEnabled(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsSpellCheckEnabled(entry);
#endif
		}

		public static void MapMaxLength(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateMaxLength(entry);
#endif
		}

		public static void MapPlaceholder(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdatePlaceholder(entry);
#endif
		}

		public static void MapPlaceholderColor(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdatePlaceholderColor(entry);
#endif
		}

		public static void MapFont(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateTizenFont(entry, handler.GetService<IFontManager>());
#endif
		}

		public static void MapIsReadOnly(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsReadOnly(entry);
#endif
		}

		public static void MapKeyboard(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateKeyboard(entry);
#endif
		}

		public static void MapReturnType(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateReturnType(entry);
#endif
		}

		public static void MapCharacterSpacing(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateCharacterSpacing(entry);
#endif
		}

		public static void MapCursorPosition(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateCursorPosition(entry);
#endif
		}

		public static void MapSelectionLength(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateSelectionLength(entry);
#endif
		}

		public static void MapClearButtonVisibility(TizenEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.PlatformView?.UpdateClearButtonVisibility(entry);
#endif
		}

#if TIZEN
		/// <remarks>
		/// Return commits the entry. The event is consumed so the key does not additionally
		/// insert a newline into a single-line field.
		/// </remarks>
		bool OnKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return false;

			if (e.Key.State == global::Tizen.NUI.Key.StateType.Down && e.Key.KeyPressedName is "Return" or "Enter")
			{
				VirtualView.Completed();
				return true;
			}

			return false;
		}
#endif

#if TIZEN
		void OnTextChanged(object? sender, global::Tizen.NUI.BaseComponents.TextField.TextChangedEventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.Text = PlatformView.Text;
		}
#endif

#if TIZEN
		void OnCursorPositionChanged(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.CursorPosition = PlatformView.PrimaryCursorPosition;
#endif
		}
#endif

#if TIZEN
		/// <remarks>
		/// NUI reports a selection as an ordered pair of offsets that can run backwards when the
		/// user drags right to left. MAUI models it as a start plus a non-negative length, so the
		/// pair is normalised here.
		/// </remarks>
		void OnSelectionChanged(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.ApplySelection(PlatformView.SelectedTextStart, PlatformView.SelectedTextEnd);
#endif
		}
#endif

#if TIZEN
		void OnSelectionCleared(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.ApplyCaret(PlatformView.PrimaryCursorPosition);
#endif
		}
#endif
	}
}
