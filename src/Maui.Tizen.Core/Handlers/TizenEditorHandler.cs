// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IEditor"/>.
	/// </summary>
	public class TizenEditorHandler : TizenViewHandler<IEditor, TizenEditorView>, IEditorHandler
	{
		/// <summary>The complete property mapper for <see cref="IEditor"/>.</summary>
		public static readonly IPropertyMapper<IEditor, IEditorHandler> Mapper =
			new PropertyMapper<IEditor, IEditorHandler>(TizenHandlerMappers.Chain(EditorHandler.Mapper))
			{
				[nameof(IEditor.Background)] = MapBackground,
				[nameof(IEditor.Text)] = MapText,
				[nameof(IEditor.TextColor)] = MapTextColor,
				[nameof(IEditor.Placeholder)] = MapPlaceholder,
				[nameof(IEditor.PlaceholderColor)] = MapPlaceholderColor,
				[nameof(IEditor.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(IEditor.MaxLength)] = MapMaxLength,
				[nameof(IEditor.IsReadOnly)] = MapIsReadOnly,
				[nameof(IEditor.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
				[nameof(IEditor.IsSpellCheckEnabled)] = MapIsSpellCheckEnabled,
				[nameof(IEditor.Font)] = MapFont,
				[nameof(IEditor.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
				[nameof(IEditor.VerticalTextAlignment)] = MapVerticalTextAlignment,
				[nameof(IEditor.Keyboard)] = MapKeyboard,
				[nameof(IEditor.CursorPosition)] = MapCursorPosition,
				[nameof(IEditor.SelectionLength)] = MapSelectionLength,
			};

		/// <summary>The complete command mapper for <see cref="IEditor"/>.</summary>
		public static readonly CommandMapper<IEditor, IEditorHandler> CommandMapper =
			new CommandMapper<IEditor, IEditorHandler>(TizenHandlerMappers.ChainCommands(EditorHandler.CommandMapper));

		public TizenEditorHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenEditorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		/// <remarks>See <see cref="TizenEntryHandler.CreatePlatformView"/> for <c>FocusableInTouch</c>.</remarks>
		IEditor IEditorHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="IEditorHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object IEditorHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="IEditorHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenEditorView? Platform(IEditorHandler handler) => handler.PlatformView as TizenEditorView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenEditorHandler AsHandler(IEditorHandler handler) => (TizenEditorHandler)handler;

		protected override TizenEditorView CreatePlatformView()
		{
#if TIZEN
			return new() { Focusable = true, FocusableInTouch = true };
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenEditorView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.TextChanged += OnTextChanged;
			platformView.FocusLost += OnFocusLost;
			platformView.CursorPositionChanged += OnCursorPositionChanged;
			platformView.SelectionChanged += OnSelectionChanged;
			platformView.SelectionCleared += OnSelectionCleared;
#endif
		}

		protected override void DisconnectHandler(TizenEditorView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.TextChanged -= OnTextChanged;
				platformView.FocusLost -= OnFocusLost;
				platformView.CursorPositionChanged -= OnCursorPositionChanged;
				platformView.SelectionChanged -= OnSelectionChanged;
				platformView.SelectionCleared -= OnSelectionCleared;
			}
#endif
			base.DisconnectHandler(platformView);
		}

		/// <remarks>See <see cref="TizenEntryHandler.MapBackground"/>.</remarks>
		public static void MapBackground(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.UpdateValue(nameof(IViewHandler.ContainerView));
			Platform(handler)?.UpdateBackground(editor, clearWhenNull: false);
#endif
		}

		public static void MapText(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateText(editor);
#endif
		}

		public static void MapTextColor(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateTextColor(editor);
#endif
		}

		public static void MapPlaceholder(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdatePlaceholder(editor);
#endif
		}

		public static void MapPlaceholderColor(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdatePlaceholderColor(editor);
#endif
		}

		public static void MapCharacterSpacing(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateCharacterSpacing(editor);
#endif
		}

		public static void MapMaxLength(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateMaxLength(editor);
#endif
		}

		public static void MapIsReadOnly(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateIsReadOnly(editor);
#endif
		}

		public static void MapIsTextPredictionEnabled(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateIsTextPredictionEnabled(editor);
#endif
		}

		public static void MapIsSpellCheckEnabled(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateIsSpellCheckEnabled(editor);
#endif
		}

		public static void MapFont(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateTizenFont(editor, handler.GetService<IFontManager>());
#endif
		}

		public static void MapHorizontalTextAlignment(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateHorizontalTextAlignment(editor);
#endif
		}

		public static void MapVerticalTextAlignment(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateVerticalTextAlignment(editor);
#endif
		}

		public static void MapKeyboard(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateKeyboard(editor);
#endif
		}

		public static void MapCursorPosition(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateCursorPosition(editor);
#endif
		}

		public static void MapSelectionLength(IEditorHandler handler, IEditor editor)
		{
#if TIZEN
			Platform(handler)?.UpdateSelectionLength(editor);
#endif
		}

#if TIZEN
		void OnTextChanged(object? sender, global::Tizen.NUI.BaseComponents.TextEditor.TextChangedEventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.Text = PlatformView.Text;
		}
#endif

		/// <remarks>
		/// A multi-line editor has no return-to-commit gesture, so MAUI defines completion as
		/// losing focus.
		/// </remarks>
		void OnFocusLost(object? sender, EventArgs e) => VirtualView?.Completed();

		/// <remarks>
		/// Without this the virtual view's <see cref="ITextInput.CursorPosition"/> only ever moves
		/// when the application sets it. Moving the caret in the editor itself would leave MAUI
		/// believing it was still wherever it was last told, so the next programmatic edit or
		/// selection would be applied at the wrong offset.
		/// </remarks>
		void OnCursorPositionChanged(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.CursorPosition = PlatformView.PrimaryCursorPosition;
#endif
		}

		/// <remarks>
		/// NUI reports a selection as an ordered pair of offsets that runs backwards when the user
		/// drags right to left. MAUI models it as a start plus a non-negative length, so the pair
		/// is normalised here.
		/// </remarks>
		void OnSelectionChanged(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.ApplySelection(PlatformView.SelectedTextStart, PlatformView.SelectedTextEnd);
#endif
		}

		/// <remarks>
		/// Clearing the selection collapses it to a caret, so the length goes to zero and the
		/// cursor follows the platform's primary position.
		/// </remarks>
		void OnSelectionCleared(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.ApplyCaret(PlatformView.PrimaryCursorPosition);
#endif
		}
	}
}
