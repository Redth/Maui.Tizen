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
		/// Everything is registered with <c>TryAdd</c>, so a host or a later migration wave can
		/// substitute its own implementation by registering first.
		/// </para>
		/// <para>
		/// <see cref="ITizenModalHost"/> is expected to be replaced by the navigation wave with one
		/// that pushes onto the window's modal stack; the default opens popups directly and so does
		/// not participate in back navigation. See <see cref="TizenDirectModalHost"/>.
		/// </para>
		/// </remarks>
		/// <param name="services">The service collection.</param>
		/// <returns>The service collection, for chaining.</returns>
		public static IServiceCollection AddTizenControlServices(this IServiceCollection services)
		{
			ArgumentNullException.ThrowIfNull(services);

			services.TryAddSingleton<ITizenFontManager, TizenFontManager>();
			services.TryAddSingleton<IFontManager>(static sp => sp.GetRequiredService<ITizenFontManager>());
			services.TryAddSingleton<ITizenModalHost, TizenDirectModalHost>();

			return services;
		}
	}
}
