// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Maui.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins that <see cref="ControlsRemap.Force"/> genuinely performs the Controls remaps the parity
	/// tests are measured against.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exists because the obvious implementation is wrong in a way nothing else catches. Only
	/// <c>Label</c> and <c>CheckBox</c> call <c>RemapForControls</c> from a static constructor; the
	/// other thirteen Wave A controls are remapped by
	/// <c>Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions.ConfigureControls</c> when a
	/// <c>MauiApp</c> is built. A <c>Force</c> that only ran class constructors left most mappers
	/// un-remapped, so parity was really measured against Core in isolation - and the result then
	/// depended on whether some unrelated test had built a Controls app earlier in the run.
	/// </para>
	/// <para>
	/// Asserting on keys that <i>only</i> Controls contributes is what makes this test able to fail.
	/// </para>
	/// </remarks>
	public class ControlsRemapTests
	{
		/// <summary>
		/// Keys contributed exclusively by a <c>RemapForControls</c> that runs from
		/// <c>ConfigureControls</c> rather than a static constructor.
		/// </summary>
		[Theory]
		[InlineData(typeof(PickerHandler), "ItemsSource")]
		[InlineData(typeof(StepperHandler), "Increment")]
		[InlineData(typeof(ButtonHandler), "IsInAccessibleTree")]
		[InlineData(typeof(EntryHandler), "IsInAccessibleTree")]
		public void ForceAppliesRemapsThatOnlyRunWhenAControlsAppIsBuilt(Type handlerType, string key)
		{
			ControlsRemap.Force();

			var keys = MapperKeys(handlerType);

			Assert.True(
				keys.Contains(key),
				$"{handlerType.Name}.Mapper has no '{key}'. ControlsRemap.Force() did not perform the " +
				"Controls remap, so every parity number in this suite describes Microsoft.Maui.Core " +
				"in isolation rather than what an application sees. Force() must build a MauiApp - " +
				"running class constructors is not enough for this control.");
		}

		/// <summary>
		/// The two controls that <i>do</i> remap from a static constructor still do so.
		/// </summary>
		[Theory]
		[InlineData(typeof(LabelHandler), "FormattedText")]
		[InlineData(typeof(CheckBoxHandler), "Color")]
		public void ForceAppliesRemapsThatRunFromStaticConstructors(Type handlerType, string key)
		{
			ControlsRemap.Force();

			Assert.Contains(key, MapperKeys(handlerType));
		}

		/// <summary>
		/// <see cref="ControlsRemap.Force"/> is called from most tests in this assembly, so it has to
		/// be safe to call repeatedly and from several tests at once.
		/// </summary>
		[Fact]
		public void ForceIsIdempotent()
		{
			ControlsRemap.Force();
			var first = MapperKeys(typeof(PickerHandler)).Count;

			ControlsRemap.Force();
			ControlsRemap.Force();

			Assert.Equal(first, MapperKeys(typeof(PickerHandler)).Count);
		}

		static System.Collections.Generic.IReadOnlyCollection<string> MapperKeys(Type handlerType)
		{
			var field = handlerType.GetField("Mapper", BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException($"{handlerType.Name} has no public static Mapper.");

			var mapper = (IPropertyMapper)field.GetValue(null)!;

			return mapper.GetKeys().Distinct(StringComparer.Ordinal).ToList();
		}
	}
}
