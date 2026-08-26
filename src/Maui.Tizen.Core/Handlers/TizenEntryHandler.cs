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
	public class TizenEntryHandler : TizenViewHandler<IEntry, TizenEntryView>, IEntryHandler
	{
		/// <summary>The complete property mapper for <see cref="IEntry"/>.</summary>
		public static readonly IPropertyMapper<IEntry, IEntryHandler> Mapper =
			new PropertyMapper<IEntry, IEntryHandler>(TizenHandlerMappers.Chain(EntryHandler.Mapper))
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
		public static readonly CommandMapper<IEntry, IEntryHandler> CommandMapper =
			new CommandMapper<IEntry, IEntryHandler>(TizenHandlerMappers.ChainCommands(EntryHandler.CommandMapper));

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
		IEntry IEntryHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="IEntryHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object IEntryHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="IEntryHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenEntryView? Platform(IEntryHandler handler) => handler.PlatformView as TizenEntryView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenEntryHandler AsHandler(IEntryHandler handler) => (TizenEntryHandler)handler;

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

		/// <remarks>
		/// An entry paints its own background, so the container has to be re-evaluated before
		/// the paint is applied or a gradient would be written to a wrapper that does not exist
		/// yet.
		/// </remarks>
		public static void MapBackground(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			handler.UpdateValue(nameof(IViewHandler.ContainerView));
			Platform(handler)?.UpdateBackground(entry.Background);
#endif
		}

		public static void MapText(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateText(entry);
#endif
		}

		public static void MapTextColor(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateTextColor(entry);
#endif
		}

		public static void MapIsPassword(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateIsPassword(entry);
#endif
		}

		public static void MapHorizontalTextAlignment(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateHorizontalTextAlignment(entry);
#endif
		}

		public static void MapVerticalTextAlignment(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateVerticalTextAlignment(entry);
#endif
		}

		public static void MapIsTextPredictionEnabled(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateIsTextPredictionEnabled(entry);
#endif
		}

		public static void MapIsSpellCheckEnabled(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateIsSpellCheckEnabled(entry);
#endif
		}

		public static void MapMaxLength(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateMaxLength(entry);
#endif
		}

		public static void MapPlaceholder(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdatePlaceholder(entry);
#endif
		}

		public static void MapPlaceholderColor(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdatePlaceholderColor(entry);
#endif
		}

		public static void MapFont(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateTizenFont(entry, handler.GetService<IFontManager>());
#endif
		}

		public static void MapIsReadOnly(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateIsReadOnly(entry);
#endif
		}

		public static void MapKeyboard(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateKeyboard(entry);
#endif
		}

		public static void MapReturnType(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateReturnType(entry);
#endif
		}

		public static void MapCharacterSpacing(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateCharacterSpacing(entry);
#endif
		}

		public static void MapCursorPosition(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateCursorPosition(entry);
#endif
		}

		public static void MapSelectionLength(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateSelectionLength(entry);
#endif
		}

		public static void MapClearButtonVisibility(IEntryHandler handler, IEntry entry)
		{
#if TIZEN
			Platform(handler)?.UpdateClearButtonVisibility(entry);
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
