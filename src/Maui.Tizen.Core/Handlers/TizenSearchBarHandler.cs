// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="ISearchBar"/>.
	/// </summary>
	public class TizenSearchBarHandler : TizenViewHandler<ISearchBar, TizenSearchBarView>
	{
		/// <summary>The complete property mapper for <see cref="ISearchBar"/>.</summary>
		public static readonly IPropertyMapper<ISearchBar, TizenSearchBarHandler> Mapper =
			new PropertyMapper<ISearchBar, TizenSearchBarHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(ISearchBar.Text)] = MapText,
				[nameof(ISearchBar.TextColor)] = MapTextColor,
				[nameof(ISearchBar.Placeholder)] = MapPlaceholder,
				[nameof(ISearchBar.PlaceholderColor)] = MapPlaceholderColor,
				[nameof(ISearchBar.Font)] = MapFont,
				[nameof(ISearchBar.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(ISearchBar.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
				[nameof(ISearchBar.VerticalTextAlignment)] = MapVerticalTextAlignment,
				[nameof(ISearchBar.MaxLength)] = MapMaxLength,
				[nameof(ISearchBar.IsReadOnly)] = MapIsReadOnly,
				[nameof(ISearchBar.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
				[nameof(ISearchBar.IsSpellCheckEnabled)] = MapIsSpellCheckEnabled,
				[nameof(ISearchBar.Keyboard)] = MapKeyboard,
				[nameof(ISearchBar.ReturnType)] = MapReturnType,
				[nameof(ISearchBar.CursorPosition)] = MapCursorPosition,
				[nameof(ISearchBar.SelectionLength)] = MapSelectionLength,
				[nameof(ISearchBar.CancelButtonColor)] = MapCancelButtonColor,
				["SearchIconColor"] = MapSearchIconColor,
			};

		/// <summary>The complete command mapper for <see cref="ISearchBar"/>.</summary>
		/// <remarks>
		/// Focus is overridden because a search bar is a composite: the group itself draws no caret
		/// and accepts no text, so focusing it would appear to do nothing. The request is forwarded
		/// to the inner text field instead.
		/// </remarks>
		public static readonly CommandMapper<ISearchBar, TizenSearchBarHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
				[nameof(IView.Focus)] = MapFocus,
				[nameof(IView.Unfocus)] = MapUnfocus,
			};

		/// <summary>Maps <see cref="IView.Focus"/> onto the inner text field.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="searchBar">The search bar.</param>
		/// <param name="args">The <see cref="FocusRequest"/>.</param>
		public static void MapFocus(TizenSearchBarHandler handler, ISearchBar searchBar, object? args)
		{
			if (args is not FocusRequest request)
				return;
#if TIZEN
			request.TrySetResult(handler.PlatformView?.FocusEntry() ?? false);
#else
			request.TrySetResult(false);
#endif
		}

		public TizenSearchBarHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSearchBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenSearchBarView CreatePlatformView()
		{
#if TIZEN
			return new();
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenSearchBarView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.Entry.TextChanged += OnTextChanged;
			platformView.SearchButtonPressed += OnSearchButtonPressed;
			platformView.Entry.CursorPositionChanged += OnCursorPositionChanged;
			platformView.Entry.SelectionChanged += OnSelectionChanged;
			platformView.Entry.SelectionCleared += OnSelectionCleared;
			platformView.EntryFocused += OnEntryFocused;
			platformView.EntryUnfocused += OnEntryUnfocused;
#endif
		}

		protected override void DisconnectHandler(TizenSearchBarView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.Entry.TextChanged -= OnTextChanged;
				platformView.SearchButtonPressed -= OnSearchButtonPressed;
				platformView.Entry.CursorPositionChanged -= OnCursorPositionChanged;
				platformView.Entry.SelectionChanged -= OnSelectionChanged;
				platformView.Entry.SelectionCleared -= OnSelectionCleared;
				platformView.EntryFocused -= OnEntryFocused;
				platformView.EntryUnfocused -= OnEntryUnfocused;
				platformView.DisconnectEvents();
			}
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapText(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateText(searchBar);
#endif
		}

		public static void MapTextColor(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateTextColor(searchBar);
#endif
		}

		public static void MapPlaceholder(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdatePlaceholder(searchBar);
#endif
		}

		public static void MapPlaceholderColor(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdatePlaceholderColor(searchBar);
#endif
		}

		public static void MapFont(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateTizenFont(searchBar, handler.GetService<IFontManager>());
#endif
		}

		public static void MapCharacterSpacing(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateCharacterSpacing(searchBar);
#endif
		}

		public static void MapHorizontalTextAlignment(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateHorizontalTextAlignment(searchBar);
#endif
		}

		public static void MapVerticalTextAlignment(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateVerticalTextAlignment(searchBar);
#endif
		}

		public static void MapMaxLength(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateMaxLength(searchBar);
#endif
		}

		public static void MapIsReadOnly(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateIsReadOnly(searchBar);
#endif
		}

		public static void MapIsTextPredictionEnabled(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateIsTextPredictionEnabled(searchBar);
#endif
		}

		public static void MapIsSpellCheckEnabled(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateIsSpellCheckEnabled(searchBar);
#endif
		}

		public static void MapKeyboard(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateKeyboard(searchBar);
#endif
		}

		/// <summary>
		/// Applies the return key type to the query editor.
		/// </summary>
		/// <remarks>
		/// Upstream leaves this unimplemented. It is mapped here because the underlying entry
		/// supports it and a search field defaulting to "done" instead of "search" is a visible
		/// difference on the soft keyboard.
		/// </remarks>
		public static void MapReturnType(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateReturnType(searchBar.ReturnType);
#endif
		}

		public static void MapCursorPosition(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateCursorPosition(searchBar);
#endif
		}

		public static void MapSelectionLength(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
#if TIZEN
			handler.PlatformView?.Entry.UpdateSelectionLength(searchBar);
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// The Tizen search bar has no cancel affordance to tint - clearing is done from the
		/// soft keyboard. Deliberate no-op.
		/// </remarks>
		public static void MapCancelButtonColor(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// <c>SearchIconColor</c> is internal to MAUI, so the key is mapped by string. The icon
		/// is drawn by <see cref="TizenSearchBarView"/> in a fixed colour; tinting it would need a
		/// public property on the drawable. Deliberate no-op, tracked in the parity matrix.
		/// </remarks>
		public static void MapSearchIconColor(TizenSearchBarHandler handler, ISearchBar searchBar)
		{
		}

		/// <summary>Maps <see cref="IView.Unfocus"/> onto the inner text field.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="searchBar">The search bar.</param>
		/// <param name="args">Unused.</param>
		public static void MapUnfocus(TizenSearchBarHandler handler, ISearchBar searchBar, object? args)
		{
#if TIZEN
			handler.PlatformView?.UnfocusEntry();
#endif
		}

#if TIZEN
		void OnTextChanged(object? sender, global::Tizen.NUI.BaseComponents.TextField.TextChangedEventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.Text = PlatformView.Entry.Text;
		}
#endif

		void OnSearchButtonPressed(object? sender, EventArgs e) => VirtualView?.SearchButtonPressed();

		/// <remarks>
		/// See <c>TizenEditorHandler.OnCursorPositionChanged</c>. The events come from the inner
		/// text field, since that is what actually owns the caret.
		/// </remarks>
		void OnCursorPositionChanged(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.CursorPosition = PlatformView.Entry.PrimaryCursorPosition;
#endif
		}

		/// <remarks>
		/// NUI's selection offsets run backwards on a right-to-left drag; MAUI wants a start plus
		/// a non-negative length.
		/// </remarks>
		void OnSelectionChanged(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.ApplySelection(PlatformView.Entry.SelectedTextStart, PlatformView.Entry.SelectedTextEnd);
#endif
		}

		void OnSelectionCleared(object? sender, EventArgs e)
		{
#if TIZEN
			if (VirtualView is null || PlatformView is null)
				return;

			VirtualView.ApplyCaret(PlatformView.Entry.PrimaryCursorPosition);
#endif
		}

		/// <remarks>
		/// Focus lands on the inner text field, not on the group. Reflecting it back keeps
		/// <see cref="IView.IsFocused"/> truthful - the base handler only observes focus on the
		/// platform view it owns, which for a composite control never receives it.
		/// </remarks>
		void OnEntryFocused(object? sender, EventArgs e)
		{
			if (VirtualView is not null)
				VirtualView.IsFocused = true;
		}

		void OnEntryUnfocused(object? sender, EventArgs e)
		{
			if (VirtualView is not null)
				VirtualView.IsFocused = false;
		}
	}
}
