// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Registers the Tizen control handlers and the services they resolve.
	/// </summary>
	/// <remarks>
	/// Kept separate from <see cref="TizenHandlerCollectionExtensions"/> so a host can adopt the
	/// controls without the application/window slice, or vice versa, while the migration is still
	/// in progress. Registration is explicit; nothing here uses reflection over private members.
	/// </remarks>
	public static class TizenControlHandlerCollectionExtensions
	{
		/// <summary>
		/// Adds the Tizen handlers for the simple controls: button, entry, editor, check box,
		/// switch, slider, progress bar, activity indicator, picker, date picker, time picker,
		/// search bar, stepper and radio button.
		/// </summary>
		/// <param name="handlers">The handler collection.</param>
		/// <returns>The handler collection, for chaining.</returns>
		public static IMauiHandlersCollection AddTizenControlHandlers(this IMauiHandlersCollection handlers)
		{
			ArgumentNullException.ThrowIfNull(handlers);

			handlers.AddHandler<IActivityIndicator, TizenActivityIndicatorHandler>();
			handlers.AddHandler<IButton, TizenButtonHandler>();
			handlers.AddHandler<ICheckBox, TizenCheckBoxHandler>();
			handlers.AddHandler<IDatePicker, TizenDatePickerHandler>();
			handlers.AddHandler<IEditor, TizenEditorHandler>();
			handlers.AddHandler<IEntry, TizenEntryHandler>();
			handlers.AddHandler<IPicker, TizenPickerHandler>();
			handlers.AddHandler<IProgress, TizenProgressBarHandler>();
			handlers.AddHandler<IRadioButton, TizenRadioButtonHandler>();
			handlers.AddHandler<ISearchBar, TizenSearchBarHandler>();
			handlers.AddHandler<ISlider, TizenSliderHandler>();
			handlers.AddHandler<IStepper, TizenStepperHandler>();
			handlers.AddHandler<ISwitch, TizenSwitchHandler>();
			handlers.AddHandler<ITimePicker, TizenTimePickerHandler>();

			return handlers;
		}

		/// <summary>
		/// Registers the services the control handlers resolve at runtime.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b><see cref="IFontManager"/> is registered with <c>Replace</c>, not <c>TryAdd</c>, and
		/// the distinction is load-bearing.</b> <see cref="MauiApp.CreateBuilder(bool)"/> defaults
		/// to <c>useDefaults: true</c>, which runs MAUI's <c>ConfigureFonts</c> and registers
		/// <c>Microsoft.Maui.FontManager</c> before this ever runs. A <c>TryAdd</c> here is
		/// therefore a silent no-op and the neutral manager keeps answering.
		/// </para>
		/// <para>
		/// The consequence is subtle rather than loud, which is why it survived review.
		/// <see cref="TizenTextExtensions.GetTizenFontFamily"/> pattern matches the resolved
		/// <see cref="IFontManager"/> to <see cref="ITizenFontManager"/> and falls back to the raw
		/// family name when it does not match - so with the neutral manager winning, every font
		/// alias registered through <c>ConfigureFonts</c> (<c>"OpenSansRegular"</c> and friends)
		/// reached NUI unresolved. Text still rendered, in the wrong font, with nothing thrown and
		/// nothing logged. This mirrors the dispatcher and ticker registrations in
		/// <c>ConfigureTizen</c>, which are replaced for exactly the same reason.
		/// </para>
		/// <para>
		/// <see cref="ITizenFontManager"/> and <see cref="ITizenModalHost"/> keep <c>TryAdd</c>:
		/// MAUI registers neither, so nothing shadows them, and a host or later wave can substitute
		/// its own by registering first. <see cref="ITizenModalHost"/> is expected to be replaced by
		/// the navigation wave with one that pushes onto the window's modal stack; the default opens
		/// popups directly and so does not participate in back navigation. See
		/// <see cref="TizenDirectModalHost"/>.
		/// </para>
		/// <para>
		/// <c>IEmbeddedFontLoader</c> is registered by Wave B's platform-content hook rather than
		/// here, because its directory provider needs TizenFX. That hook uses <c>Replace</c> too -
		/// MAUI registers a neutral default before this backend runs.
		/// </para>
		/// </remarks>
		/// <param name="services">The service collection.</param>
		/// <returns>The service collection, for chaining.</returns>
		public static IServiceCollection AddTizenControlServices(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.TryAddSingleton<ITizenFontManager, TizenFontManager>();

			// Replace, not TryAdd - see the remarks. MAUI's ConfigureFonts got here first.
			services.Replace(ServiceDescriptor.Singleton<IFontManager>(
				static sp => sp.GetRequiredService<ITizenFontManager>()));

			services.TryAddSingleton<ITizenModalHost, TizenDirectModalHost>();

			return services;
		}
	}
}
