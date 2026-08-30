using System;
using Microsoft.Maui.Accessibility;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="ISemanticScreenReader"/>, backed by NUI accessibility.
	/// </summary>
	public sealed class TizenSemanticScreenReader : ISemanticScreenReader
	{
		/// <inheritdoc/>
		public void Announce(string text)
		{
			ArgumentNullException.ThrowIfNull(text);

			global::Tizen.NUI.Accessibility.Accessibility.Say(text, true);
		}
	}
}
