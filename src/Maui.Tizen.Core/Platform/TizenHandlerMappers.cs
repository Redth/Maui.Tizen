// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Composes a Tizen handler's mappers on top of MAUI's own.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every Tizen handler's mapper is built from two layers, and both are load-bearing:
	/// </para>
	/// <list type="number">
	/// <item><description>
	/// <b>MAUI's static <c>XHandler.Mapper</c> is chained.</b> This is what makes the backend
	/// reachable from MAUI Controls. <c>Microsoft.Maui.Controls</c> calls
	/// <c>RemapForControls</c> in each control's static constructor, which mutates those static
	/// mappers in place - adding <c>FormattedText</c>, <c>TextType</c>, <c>LineBreakMode</c>,
	/// <c>MaxLines</c>, <c>TextTransform</c>, <c>CheckBox.Color</c>, the accessibility keys and
	/// so on. Chaining is <em>live</em> rather than a snapshot, so a mapper built before the
	/// remap still picks it up; a backend that does not chain simply never receives those
	/// properties.
	/// </description></item>
	/// <item><description>
	/// <b>The Tizen view mappings are then re-applied over the top.</b> Chaining MAUI's mapper
	/// also inherits its <em>bodies</em>, and this backend resolves the neutral
	/// <c>net11.0</c> assembly, where those bodies are the <c>Standard</c> no-ops compiled with
	/// <c>PlatformView</c> aliased to <see cref="object"/>. Without this second layer every
	/// common view property - size, visibility, enabled, opacity, transforms - would resolve and
	/// do nothing. A later key shadows an earlier one, so the Tizen body wins.
	/// </description></item>
	/// </list>
	/// <para>
	/// Getting layer 2 wrong is invisible to a key-presence test, which is why
	/// <c>ControlMapperBehaviorTests</c> asserts that the mappings actually reach the platform
	/// view.
	/// </para>
	/// </remarks>
	public static class TizenHandlerMappers
	{
		/// <summary>
		/// Builds the chain source for a Tizen handler's property mapper.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Returned as a chain <em>source</em> rather than a ready-made mapper so the handler can
		/// still use an object initializer for its own keys, and so those keys land <b>after</b>
		/// the Tizen view mappings. The ordering is load-bearing: a handler that deliberately
		/// overrides a common view key - <c>Entry.Background</c>, for instance, which has to
		/// re-evaluate the container first - must win over the generic implementation.
		/// </para>
		/// <para>
		/// The resulting precedence, lowest to highest, is: MAUI's static mapper (including
		/// anything Controls remapped into it), then the Tizen view mappings, then the handler's
		/// own keys.
		/// </para>
		/// </remarks>
		/// <param name="mauiMapper">MAUI's static mapper for this control.</param>
		/// <returns>A mapper to pass as the chain source of the handler's own mapper.</returns>
		public static IPropertyMapper Chain(IPropertyMapper mauiMapper)
		{
			ArgumentNullException.ThrowIfNull(mauiMapper);

			var mapper = new PropertyMapper<IView, IViewHandler>(mauiMapper);

			// Re-apply the Tizen bodies over MAUI's inherited no-ops. Copied by key rather than
			// listed literally so this cannot drift from TizenViewMappers as the base evolves.
			foreach (var key in TizenViewMappers.ViewMapper.GetKeys())
			{
				var tizenKey = key;
				mapper[tizenKey] = (handler, view) =>
					TizenViewMappers.ViewMapper.UpdateProperty(handler, view, tizenKey);
			}

			return mapper;
		}

		/// <summary>
		/// The view commands the Tizen base command mapper implements.
		/// </summary>
		/// <remarks>
		/// Listed literally because <see cref="CommandMapper"/> exposes no key enumeration, only
		/// <c>GetCommand</c>. <c>TizenHandlerMapperTests.ViewCommandKeysMatchTheTizenBaseMapper</c>
		/// asserts this list stays in step with what the base mapper actually resolves, so the
		/// duplication cannot drift silently.
		/// </remarks>
		public static readonly string[] ViewCommandKeys =
		[
			nameof(IView.InvalidateMeasure),
			nameof(IView.Frame),
			nameof(IView.Focus),
			nameof(IView.Unfocus),
		];

		/// <summary>
		/// Creates a command mapper chaining <paramref name="mauiCommandMapper"/> with the Tizen
		/// view commands layered over it.
		/// </summary>
		/// <param name="mauiCommandMapper">MAUI's static command mapper for this control.</param>
		/// <returns>A command mapper to pass as the chain source of the handler's own.</returns>
		public static CommandMapper ChainCommands(CommandMapper mauiCommandMapper)
		{
			ArgumentNullException.ThrowIfNull(mauiCommandMapper);

			var mapper = new CommandMapper<IView, IViewHandler>(mauiCommandMapper);

			foreach (var key in ViewCommandKeys)
			{
				var tizenKey = key;
				mapper[tizenKey] = (handler, view, args) =>
					TizenViewMappers.ViewCommandMapper.GetCommand(tizenKey)?.Invoke(handler, view, args);
			}

			return mapper;
		}
	}
}
