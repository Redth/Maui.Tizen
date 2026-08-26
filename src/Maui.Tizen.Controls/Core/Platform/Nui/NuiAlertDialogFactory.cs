using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using global::Tizen.NUI;
using global::Tizen.UIExtensions.NUI;
using NView = global::Tizen.NUI.BaseComponents.View;
using TWindow = global::Tizen.NUI.Window;
using TKeyboard = global::Tizen.UIExtensions.Common.Keyboard;

namespace Microsoft.Maui.Platforms.Tizen.Nui
{
	/// <summary>
	/// Adapts a <see cref="Popup{T}"/> to the <see cref="ITizenAlertDialog{TResult}"/> contract.
	/// </summary>
	/// <typeparam name="TResult">The value produced when the popup is dismissed.</typeparam>
	internal sealed class NuiAlertDialog<TResult> : ITizenAlertDialog<TResult>
	{
		readonly Popup<TResult> _popup;
		bool _disposed;

		public NuiAlertDialog(Popup<TResult> popup) =>
			_popup = popup ?? throw new ArgumentNullException(nameof(popup));

		public Task<TResult> OpenAsync() => _popup.Open();

		public void Close()
		{
			if (_disposed || !_popup.IsOpen)
			{
				return;
			}

			// Closing an open popup cancels its pending Open() task, which the alert
			// infrastructure translates into the documented cancellation result.
			_popup.Close();
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_popup.Dispose();
		}
	}

	/// <summary>
	/// The modal busy indicator, ported from the NUI <c>BusyPopup</c> in dotnet/maui.
	/// </summary>
	internal sealed class NuiBusyIndicator : ITizenBusyIndicator
	{
		readonly BusyPopup _popup = new();
		bool _disposed;

		public bool IsOpen => !_disposed && _popup.IsOpen;

		public void Open()
		{
			if (_disposed || _popup.IsOpen)
			{
				return;
			}

			_popup.Open();
		}

		public void Close()
		{
			if (_disposed || !_popup.IsOpen)
			{
				return;
			}

			_popup.Close();
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_popup.Dispose();
		}

		sealed class BusyPopup : Popup
		{
			public BusyPopup()
			{
				BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
				Layout = new LinearLayout
				{
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
				};
				Content = new global::Tizen.UIExtensions.NUI.GraphicsView.ActivityIndicator
				{
					SizeWidth = 100,
					SizeHeight = 100,
					IsRunning = true,
				};
			}

			// The busy indicator is not user-dismissible; swallowing back keeps it modal.
			protected override bool OnBackButtonPressed() => true;
		}
	}

	/// <summary>
	/// Builds the NUI popups that service alert, action sheet and prompt requests.
	/// </summary>
	/// <remarks>
	/// This is the presentation half of the NUI <c>AlertRequestHelper</c> from dotnet/maui. The
	/// routing rules that used to live alongside it - window affinity, modal coordination and
	/// cancellation mapping - are in <c>TizenAlertManagerSubscription</c>, which has no dependency
	/// on NUI and is unit tested on the host.
	/// </remarks>
	public sealed class NuiAlertDialogFactory : ITizenAlertDialogFactory
	{
		/// <inheritdoc/>
		public ITizenAlertDialog<bool> CreateAlertDialog(AlertArguments arguments)
		{
			ArgumentNullException.ThrowIfNull(arguments);

			var popup = arguments.Accept is not null
				? new MessagePopup(arguments.Title, arguments.Message, arguments.Accept, arguments.Cancel)
				: new MessagePopup(arguments.Title, arguments.Message, arguments.Cancel);

			return new NuiAlertDialog<bool>(popup);
		}

		/// <inheritdoc/>
		public ITizenAlertDialog<string?> CreateActionSheetDialog(ActionSheetArguments arguments)
		{
			ArgumentNullException.ThrowIfNull(arguments);

			var popup = new ActionSheetPopup(
				arguments.Title,
				arguments.Cancel,
				destruction: arguments.Destruction,
				buttons: arguments.Buttons);

			return new NuiAlertDialog<string?>(popup!);
		}

		/// <inheritdoc/>
		public ITizenAlertDialog<string?> CreatePromptDialog(PromptArguments arguments)
		{
			ArgumentNullException.ThrowIfNull(arguments);

			var popup = new PromptPopup(
				arguments.Title,
				arguments.Message,
				arguments.Accept,
				arguments.Cancel,
				// An empty placeholder breaks the popup layout, so fall back to a single space.
				string.IsNullOrEmpty(arguments.Placeholder) ? " " : arguments.Placeholder,
				arguments.MaxLength,
				ToPlatformKeyboard(arguments.Keyboard),
				arguments.InitialValue);

			return new NuiAlertDialog<string?>(popup!);
		}

		/// <inheritdoc/>
		public ITizenBusyIndicator CreateBusyIndicator() => new NuiBusyIndicator();

		/// <summary>
		/// Maps a .NET MAUI keyboard onto its Tizen equivalent.
		/// </summary>
		/// <remarks>
		/// This mirrors <c>Microsoft.Maui.Platform.KeyboardExtensions.ToPlatform</c> from the Tizen
		/// Core layer. Replace it with a call to that extension once Maui.Tizen.Core exposes it, so
		/// there is a single mapping rather than two that can drift.
		/// </remarks>
		static TKeyboard ToPlatformKeyboard(Keyboard? keyboard)
		{
			if (keyboard == Keyboard.Numeric)
			{
				return TKeyboard.Numeric;
			}

			if (keyboard == Keyboard.Telephone)
			{
				return TKeyboard.PhoneNumber;
			}

			if (keyboard == Keyboard.Email)
			{
				return TKeyboard.Email;
			}

			if (keyboard == Keyboard.Url)
			{
				return TKeyboard.Url;
			}

			if (keyboard == Keyboard.Date || keyboard == Keyboard.Time)
			{
				return TKeyboard.DateTime;
			}

			if (keyboard == Keyboard.Password)
			{
				return TKeyboard.Password;
			}

			return TKeyboard.Normal;
		}
	}

	/// <summary>
	/// Coordinates NUI dialogs with the Tizen modal navigation stack.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.NavigationStackExtensions.PushDummyPopupPage</c> in
	/// dotnet/maui. A placeholder page is pushed behind the popup so the modal stack knows
	/// something modal is on screen, and popped once the dialog closes.
	/// </para>
	/// <para>
	/// One deliberate deviation from the original: exceptions are not swallowed. The original
	/// published the dialog result from inside this scope, so swallowing was preferable to
	/// crashing but left the awaiting caller pending forever. The placeholder is still always
	/// popped, but the failure now propagates so the alert subscription can fault the caller
	/// instead of hanging it.
	/// </para>
	/// </remarks>
	public sealed class NuiModalHost : ITizenModalHost
	{
		readonly IServiceProvider _windowServices;
		readonly ILogger<NuiModalHost>? _logger;
		bool _warnedAboutMissingStack;

		/// <summary>
		/// Initializes a new instance of the <see cref="NuiModalHost"/> class.
		/// </summary>
		/// <param name="windowServices">
		/// The window-scoped services the Tizen window handler registers the window's
		/// <see cref="NavigationStack"/> into.
		/// </param>
		/// <param name="logger">Optional logger.</param>
		public NuiModalHost(IServiceProvider windowServices, ILogger<NuiModalHost>? logger = null)
		{
			_windowServices = windowServices ?? throw new ArgumentNullException(nameof(windowServices));
			_logger = logger;
		}

		/// <inheritdoc/>
		public async Task RunModalAsync(Func<Task> dialogOperation)
		{
			ArgumentNullException.ThrowIfNull(dialogOperation);

			if (_windowServices.GetService(typeof(NavigationStack)) is not NavigationStack stack)
			{
				if (!_warnedAboutMissingStack)
				{
					_warnedAboutMissingStack = true;
					_logger?.LogWarning(
						"No NavigationStack is registered for this window, so dialogs run without modal-stack coordination. " +
						"The Tizen window handler is expected to register the window's NavigationStack in the window scope.");
				}

				await dialogOperation().ConfigureAwait(true);
				return;
			}

			var placeholder = new NView();

			stack.ShownBehindPage = true;
			_ = stack.Push(placeholder, false);
			stack.ShownBehindPage = false;

			try
			{
				await dialogOperation().ConfigureAwait(true);
			}
			finally
			{
				// Always unwind, otherwise the modal stack is left permanently unbalanced.
				if (ReferenceEquals(stack.Top, placeholder))
				{
					_ = stack.Pop(false);
				}
				else
				{
					stack.Pop(placeholder);
				}
			}
		}
	}

	/// <summary>
	/// Resolves the native NUI window for a context.
	/// </summary>
	/// <remarks>
	/// The Tizen window handler is expected to register the window's <see cref="Window"/> in the
	/// window scope. Falling back to <see cref="ITizenWindowContext"/> keeps this working for
	/// hosts that only call <c>TizenWindowContext.AttachTo</c>.
	/// </remarks>
	public sealed class NuiPlatformWindowProvider : ITizenPlatformWindowProvider
	{
		readonly TizenPlatformWindowProvider _fallback = new();

		/// <inheritdoc/>
		public object? GetPlatformWindow(IMauiContext? context) =>
			context?.Services?.GetService(typeof(TWindow)) as TWindow ?? _fallback.GetPlatformWindow(context);
	}
}
