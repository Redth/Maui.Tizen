// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using Tizen.UIExtensions.NUI;
#endif

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IPicker"/>.
	/// </summary>
	/// <remarks>
	/// Tizen has no drop-down control, so the picker is a read-only entry that opens an action
	/// sheet listing the items.
	/// </remarks>
	public class TizenPickerHandler : TizenViewHandler<IPicker, TizenPickerView>
	{
#if TIZEN
		bool _isOpen;
#endif

		/// <summary>The complete property mapper for <see cref="IPicker"/>.</summary>
		public static readonly IPropertyMapper<IPicker, TizenPickerHandler> Mapper =
			new PropertyMapper<IPicker, TizenPickerHandler>(TizenViewMappers.ViewMapper)
			{
				["Items"] = MapItems,
				[nameof(IPicker.Title)] = MapTitle,
				[nameof(IPicker.TitleColor)] = MapTitleColor,
				[nameof(IPicker.SelectedIndex)] = MapSelectedIndex,
				[nameof(IPicker.TextColor)] = MapTextColor,
				[nameof(IPicker.Font)] = MapFont,
				[nameof(IPicker.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(IPicker.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
				[nameof(IPicker.VerticalTextAlignment)] = MapVerticalTextAlignment,
				["IsOpen"] = MapIsOpen,
			};

		/// <summary>The complete command mapper for <see cref="IPicker"/>.</summary>
		public static readonly CommandMapper<IPicker, TizenPickerHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenPickerHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenPickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenPickerView CreatePlatformView() => new TizenPickerView();

		protected override void ConnectHandler(TizenPickerView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.TouchEvent += OnTouch;
			platformView.KeyEvent += OnKeyEvent;
#endif
		}

		protected override void DisconnectHandler(TizenPickerView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.TouchEvent -= OnTouch;
				platformView.KeyEvent -= OnKeyEvent;
			}
#endif
			base.DisconnectHandler(platformView);
		}

		/// <summary>Re-renders the displayed text after the item collection changed.</summary>
		public static void MapItems(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdatePicker(picker);
#endif
		}

		public static void MapTitle(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTitle(picker);
#endif
		}

		public static void MapTitleColor(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTitleColor(picker);
#endif
		}

		public static void MapSelectedIndex(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateSelectedIndex(picker);
#endif
		}

		public static void MapTextColor(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextColor(picker);
#endif
		}

		public static void MapFont(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTizenFont(picker, handler.GetService<IFontManager>());
#endif
		}

		public static void MapCharacterSpacing(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateCharacterSpacing(picker);
#endif
		}

		public static void MapHorizontalTextAlignment(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateHorizontalTextAlignment(picker);
#endif
		}

		public static void MapVerticalTextAlignment(TizenPickerHandler handler, IPicker picker)
		{
#if TIZEN
			handler.PlatformView?.UpdateVerticalTextAlignment(picker);
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// MAUI declares <c>IsOpen</c> as an internal member of <c>IPicker</c>, so an
		/// out-of-tree backend cannot read it; the key is mapped by string purely so this
		/// mapper remains a complete replacement for the neutral one. Tizen's action sheet also
		/// has no programmatic dismiss, so even a readable value could only be honoured in one
		/// direction. Deliberate no-op - see docs/wave-a-handlers.md.
		/// </remarks>
		public static void MapIsOpen(TizenPickerHandler handler, IPicker picker)
		{
		}

#if TIZEN
		/// <remarks>
		/// A touch opens the dialog on release, not on press, so that a scroll gesture that
		/// happens to start on the picker does not open it.
		/// </remarks>
		bool OnTouch(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			if (e.Touch.GetState(0) != global::Tizen.NUI.PointStateType.Up)
				return false;

			if (VirtualView is null)
				return false;

			OpenPopup();
			return true;
		}
#endif

#if TIZEN
		bool OnKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (!e.Key.IsAcceptKeyEvent())
				return false;

			OpenPopup();
			return true;
		}
#endif

#if TIZEN
		void OpenPopup() => _ = OpenPopupAsync();
#endif

#if TIZEN
		/// <remarks>
		/// <para>
		/// The action sheet returns the chosen item's text, which is matched back to an index.
		/// Duplicate item texts therefore resolve to the first match - a limitation of Tizen's
		/// text-based action sheet, not of the mapping.
		/// </para>
		/// <para>
		/// Cancellation surfaces as a faulted task from <c>Open</c>, which is why the result is
		/// discarded rather than treated as an error.
		/// </para>
		/// </remarks>
		async System.Threading.Tasks.Task OpenPopupAsync()
		{
			if (VirtualView is null || _isOpen)
				return;

			_isOpen = true;

			try
			{
				await this.GetModalHost().RunModalAsync(async () =>
				{
					var items = GetItems(VirtualView);

					using var popup = new global::Tizen.UIExtensions.NUI.ActionSheetPopup(
						VirtualView.Title,
						"Cancel",
						null,
						items);

					try
					{
						var chosen = await popup.Open().ConfigureAwait(false);
						var index = items.IndexOf(chosen);

						if (index >= 0)
						{
							// Writing the virtual view re-enters MAUI's property system, which
							// runs the mapper and touches NUI - so it has to happen on the main
							// loop, not on whatever thread the popup completed on.
							this.DispatchIfRequired(() => VirtualView.SelectedIndex = index);
						}
					}
					catch (OperationCanceledException)
					{
						// Dismissed without choosing; leave the selection alone.
					}
				}).ConfigureAwait(false);
			}
			finally
			{
				_isOpen = false;
			}
		}
#endif

#if TIZEN
		static List<string> GetItems(IPicker picker)
		{
			var count = picker.GetCount();
			var items = new List<string>(count);

			for (var i = 0; i < count; i++)
				items.Add(picker.GetItem(i) ?? string.Empty);

			return items;
		}
#endif
	}
}
