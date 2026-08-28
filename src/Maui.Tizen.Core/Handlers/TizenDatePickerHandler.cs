// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using Tizen.UIExtensions.NUI;
#endif

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IDatePicker"/>.
	/// </summary>
	/// <remarks>
	/// Presented as a read-only entry that opens <see cref="TizenDateTimePicker"/>.
	/// </remarks>
	public class TizenDatePickerHandler : TizenViewHandler<IDatePicker, TizenPickerView>, IDatePickerHandler
	{
#if TIZEN
		readonly TizenPopupLifecycle<TizenDateTimePicker> _popupLifecycle = new();
#endif

		/// <summary>The complete property mapper for <see cref="IDatePicker"/>.</summary>
		public static readonly IPropertyMapper<IDatePicker, IDatePickerHandler> Mapper =
			new PropertyMapper<IDatePicker, IDatePickerHandler>(TizenHandlerMappers.Chain(DatePickerHandler.Mapper))
			{
				[nameof(IDatePicker.Format)] = MapFormat,
				[nameof(IDatePicker.Date)] = MapDate,
				[nameof(IDatePicker.MinimumDate)] = MapMinimumDate,
				[nameof(IDatePicker.MaximumDate)] = MapMaximumDate,
				[nameof(IDatePicker.Font)] = MapFont,
				[nameof(IDatePicker.TextColor)] = MapTextColor,
				[nameof(IDatePicker.CharacterSpacing)] = MapCharacterSpacing,
				["IsOpen"] = MapIsOpen,
			};

		/// <summary>The complete command mapper for <see cref="IDatePicker"/>.</summary>
		public static readonly CommandMapper<IDatePicker, IDatePickerHandler> CommandMapper =
			new CommandMapper<IDatePicker, IDatePickerHandler>(TizenHandlerMappers.ChainCommands(DatePickerHandler.CommandMapper));

		public TizenDatePickerHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenDatePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IDatePicker IDatePickerHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="IDatePickerHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object IDatePickerHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="IDatePickerHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenPickerView? Platform(IDatePickerHandler handler) => handler.PlatformView as TizenPickerView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenDatePickerHandler AsHandler(IDatePickerHandler handler) => (TizenDatePickerHandler)handler;

		protected override TizenPickerView CreatePlatformView() => new TizenPickerView();

		protected override void ConnectHandler(TizenPickerView platformView)
		{
#if TIZEN
			_popupLifecycle.CancelOnUiThread(static popup => popup.Close());
#endif
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.TouchEvent += OnTouch;
			platformView.KeyEvent += OnKeyEvent;
#endif
		}

		protected override void DisconnectHandler(TizenPickerView platformView)
		{
#if TIZEN
			_popupLifecycle.CancelOnUiThread(static popup => popup.Close());

			if (platformView.HasBody())
			{
				platformView.TouchEvent -= OnTouch;
				platformView.KeyEvent -= OnKeyEvent;
			}
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapFormat(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateFormat(datePicker);
#endif
		}

		public static void MapDate(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateDate(datePicker);
#endif
		}

		/// <summary>
		/// Re-renders the date so it reflects a changed lower bound.
		/// </summary>
		/// <remarks>
		/// Upstream leaves the bounds unimplemented. Tizen's date dialog cannot be given a
		/// range, so the bound is enforced by clamping instead: on display here, and again when
		/// the dialog's result is accepted. That makes the limit real rather than advisory.
		/// </remarks>
		public static void MapMinimumDate(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateDate(datePicker);
#endif
		}

		/// <summary>Re-renders the date so it reflects a changed upper bound.</summary>
		/// <remarks>See <see cref="MapMinimumDate"/>.</remarks>
		public static void MapMaximumDate(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateDate(datePicker);
#endif
		}

		public static void MapFont(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateTizenFont(datePicker, handler.GetService<IFontManager>());
#endif
		}

		public static void MapTextColor(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateTextColor(datePicker);
#endif
		}

		public static void MapCharacterSpacing(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateCharacterSpacing(datePicker);
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// <c>IsOpen</c> is internal to <c>IDatePicker</c>, so an out-of-tree backend cannot
		/// read it. Deliberate no-op; the key is present so the mapper stays complete.
		/// </remarks>
		public static void MapIsOpen(IDatePickerHandler handler, IDatePicker datePicker)
		{
		}

#if TIZEN
		bool OnTouch(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			if (e.Touch.GetState(0) != global::Tizen.NUI.PointStateType.Up)
				return false;

			if (VirtualView is null)
				return false;

			OpenPopupAsync().FireAndForget(this);
			return true;
		}
#endif

#if TIZEN
		bool OnKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (!e.Key.IsAcceptKeyEvent())
				return false;

			OpenPopupAsync().FireAndForget(this);
			return true;
		}
#endif

#if TIZEN
		async Task OpenPopupAsync()
		{
			var virtualView = VirtualView;
			var platformView = PlatformView;

			if (virtualView is null || platformView is null)
				return;

			var date = virtualView.Date ?? DateTime.Today;
			var minimum = virtualView.MinimumDate;
			var maximum = virtualView.MaximumDate;

			await this.GetModalHost().RunModalAsync(() =>
				_popupLifecycle.RunAsync(
					virtualView,
					platformView,
					() => VirtualView,
					() => PlatformView,
					() => new TizenDateTimePicker(date, isTimePicker: false, minimum, maximum),
					static (popup, _) => popup.Open(),
					static popup => popup.Close(),
					this.DispatchIfRequiredAsync,
					static (picker, selected) => picker.Date = selected));
		}
#endif
	}
}
