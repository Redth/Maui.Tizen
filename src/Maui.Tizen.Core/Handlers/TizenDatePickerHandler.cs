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
				[nameof(IDatePicker.IsOpen)] = MapIsOpen,
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
			_popupLifecycle.CancelOnUiThread();
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
			var originatingView = VirtualView;

			TizenCleanup.Run(
				_popupLifecycle.CancelOnUiThread,
				() =>
				{
					if (originatingView is not null &&
						ReferenceEquals(VirtualView, originatingView) &&
						ReferenceEquals(PlatformView, platformView) &&
						originatingView.IsOpen)
					{
						originatingView.IsOpen = false;
					}
				},
				() =>
				{
					if (platformView.HasBody())
						platformView.TouchEvent -= OnTouch;
				},
				() =>
				{
					if (platformView.HasBody())
						platformView.KeyEvent -= OnKeyEvent;
				},
				() => base.DisconnectHandler(platformView));
#else
			base.DisconnectHandler(platformView);
#endif
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

		/// <summary>Opens or closes the active popup from <see cref="IDatePicker.IsOpen"/>.</summary>
		public static void MapIsOpen(IDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			var concrete = AsHandler(handler);

			if (!datePicker.IsOpen)
			{
				concrete._popupLifecycle.CancelAsync().FireAndForget(handler);
				return;
			}

			if (Platform(handler) is { } platformView)
			{
				concrete.OpenPopupAsync(
					datePicker,
					platformView,
					TizenDispatchExtensions.CaptureDispatcher(handler)).FireAndForget(handler);
			}
#endif
		}

#if TIZEN
		bool OnTouch(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			if (e.Touch.GetState(0) != global::Tizen.NUI.PointStateType.Up)
				return false;

			if (VirtualView is not { } datePicker)
				return false;

			datePicker.IsOpen = true;
			return true;
		}
#endif

#if TIZEN
		bool OnKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (!e.Key.IsAcceptKeyEvent())
				return false;

			if (VirtualView is not { } datePicker)
				return false;

			datePicker.IsOpen = true;
			return true;
		}
#endif

#if TIZEN
		async Task OpenPopupAsync(
			IDatePicker virtualView,
			TizenPickerView platformView,
			Func<Action, Task> dispatchOnUiThread)
		{
			var date = virtualView.Date ?? DateTime.Today;
			var minimum = virtualView.MinimumDate;
			var maximum = virtualView.MaximumDate;

			await this.GetModalHost().RunModalAsync(() =>
				_popupLifecycle.RunAsync(
					virtualView,
					platformView,
					() => VirtualView,
					() => PlatformView,
					static picker => picker.IsOpen,
					() => new TizenDateTimePicker(date, isTimePicker: false, minimum, maximum),
					static (popup, _) => popup.Open(),
					static popup => popup.Close(),
					dispatchOnUiThread,
					static (picker, selected) => picker.Date = selected,
					picker =>
					{
						if (ReferenceEquals(VirtualView, picker) &&
							ReferenceEquals(PlatformView, platformView) &&
							picker.IsOpen)
						{
							picker.IsOpen = false;
						}
					}));
		}
#endif
	}
}
