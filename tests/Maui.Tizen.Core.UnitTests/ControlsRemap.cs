// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Forces MAUI Controls' handler remaps to run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Controls calls <c>RemapForControls</c> for each control, mutating MAUI's static handler
	/// mappers in place. Until that has happened, <c>LabelHandler.Mapper</c> has no
	/// <c>FormattedText</c>, <c>PickerHandler.Mapper</c> has no <c>ItemsSource</c>,
	/// <c>StepperHandler.Mapper</c> has no <c>Increment</c>, and none of the accessibility keys
	/// exist.
	/// </para>
	/// <para>
	/// The mechanism matters, and it is not the obvious one. Only <c>Label</c> and <c>CheckBox</c>
	/// remap from a <b>static constructor</b>; the other thirteen Wave A controls remap from
	/// <c>Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions.ConfigureControls</c>, which runs
	/// when a <see cref="MauiApp"/> is <b>built</b>. Running class constructors alone therefore
	/// leaves most mappers un-remapped, and a parity measurement taken then silently describes Core
	/// in isolation rather than what an application sees. Worse, it makes the result depend on
	/// whether some other test in the run happened to build a Controls app first.
	/// </para>
	/// <para>
	/// So this builds a real Controls host once, and additionally runs the two static constructors
	/// so the guarantee does not rest on <c>ConfigureControls</c> keeping them in its list.
	/// </para>
	/// </remarks>
	public static class ControlsRemap
	{
		static readonly object _gate = new();
		static bool _forced;

		/// <summary>
		/// Performs every Controls handler remap exactly once.
		/// </summary>
		public static void Force()
		{
			lock (_gate)
			{
				if (_forced)
					return;

				// Marked before the work, not after: ConfigureTizen registers handlers whose static
				// initializers chain the very mappers being remapped, so this re-enters.
				_forced = true;

				foreach (var type in ControlTypes)
					RuntimeHelpers.RunClassConstructor(type.TypeHandle);

				// The load-bearing step: ConfigureControls runs RemapForControls for the controls
				// that do not do it from a static constructor.
				var builder = MauiApp.CreateBuilder();
				builder.UseMauiApp<RemapApp>();
				builder.ConfigureTizen();
				builder.Build().Dispose();
			}
		}

		sealed class RemapApp : Controls.Application
		{
		}

		/// <summary>
		/// The Controls types whose static constructors remap a Wave A handler.
		/// </summary>
		/// <remarks>
		/// Kept as an explicit list, but see <c>ControlsRemapTests</c>: running
		/// these alone is <i>not</i> sufficient, which is the fact this file exists to encode.
		/// </remarks>
		public static IReadOnlyList<Type> ControlTypes { get; } =
		[
			typeof(Controls.Button),
			typeof(Controls.CheckBox),
			typeof(Controls.DatePicker),
			typeof(Controls.Editor),
			typeof(Controls.Entry),
			typeof(Controls.Label),
			typeof(Controls.Picker),
			typeof(Controls.ProgressBar),
			typeof(Controls.RadioButton),
			typeof(Controls.SearchBar),
			typeof(Controls.Slider),
			typeof(Controls.Stepper),
			typeof(Controls.Switch),
			typeof(Controls.TimePicker),
			typeof(Controls.ActivityIndicator),
		];
	}
}
