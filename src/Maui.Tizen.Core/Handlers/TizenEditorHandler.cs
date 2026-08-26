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
	public class TizenEditorHandler : TizenViewHandler<IEditor, TizenEditorView>
	{
		/// <summary>The complete property mapper for <see cref="IEditor"/>.</summary>
		public static readonly IPropertyMapper<IEditor, TizenEditorHandler> Mapper =
			new PropertyMapper<IEditor, TizenEditorHandler>(ViewHandler.ViewMapper)
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
		public static readonly CommandMapper<IEditor, TizenEditorHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		public TizenEditorHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenEditorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		/// <remarks>See <see cref="TizenEntryHandler.CreatePlatformView"/> for <c>FocusableInTouch</c>.</remarks>
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
#endif
		}

		protected override void DisconnectHandler(TizenEditorView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.TextChanged -= OnTextChanged;
				platformView.FocusLost -= OnFocusLost;
			}
#endif
			base.DisconnectHandler(platformView);
		}

#if TIZEN
		/// <remarks>See <see cref="TizenEntryHandler.MapBackground"/>.</remarks>
		public static void MapBackground(TizenEditorHandler handler, IEditor editor)
		{
			handler.UpdateValue(nameof(IViewHandler.ContainerView));
			handler.PlatformView?.UpdateBackground(editor.Background);
		}
#endif

		public static void MapText(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateText(editor);
#endif
		}

		public static void MapTextColor(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextColor(editor);
#endif
		}

		public static void MapPlaceholder(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdatePlaceholder(editor);
#endif
		}

		public static void MapPlaceholderColor(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdatePlaceholderColor(editor);
#endif
		}

		public static void MapCharacterSpacing(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateCharacterSpacing(editor);
#endif
		}

		public static void MapMaxLength(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateMaxLength(editor);
#endif
		}

		public static void MapIsReadOnly(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsReadOnly(editor);
#endif
		}

		public static void MapIsTextPredictionEnabled(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsTextPredictionEnabled(editor);
#endif
		}

		public static void MapIsSpellCheckEnabled(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsSpellCheckEnabled(editor);
#endif
		}

		public static void MapFont(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateTizenFont(editor, handler.GetService<IFontManager>());
#endif
		}

		public static void MapHorizontalTextAlignment(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateHorizontalTextAlignment(editor);
#endif
		}

		public static void MapVerticalTextAlignment(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateVerticalTextAlignment(editor);
#endif
		}

		public static void MapKeyboard(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateKeyboard(editor);
#endif
		}

		public static void MapCursorPosition(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateCursorPosition(editor);
#endif
		}

		public static void MapSelectionLength(TizenEditorHandler handler, IEditor editor)
		{
#if TIZEN
			handler.PlatformView?.UpdateSelectionLength(editor);
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
	}
}
