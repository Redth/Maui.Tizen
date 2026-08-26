using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IClipboard"/>.
	/// </summary>
	/// <remarks>
	/// Tizen exposes clipboard access only through the UI toolkit's per-window copy/paste
	/// (<c>Ecore</c>/<c>NUI</c> selection buffers), not through a headless system clipboard service.
	/// There is therefore no implementation that can satisfy this contract from an Essentials
	/// service, so every member throws instead of pretending the clipboard is empty.
	/// </remarks>
	public sealed class TizenClipboard : IClipboard
	{
		const string Reason =
			"Tizen has no headless system clipboard API; copy and paste is scoped to the focused " +
			"NUI/EFL window's selection buffer.";

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public bool HasText =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IClipboard)}.{nameof(HasText)}", Reason);

		/// <inheritdoc/>
		/// <remarks>Never raised: Tizen exposes no clipboard change notification.</remarks>
		public event EventHandler<EventArgs> ClipboardContentChanged
		{
			add { }
			remove { }
		}

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task<string?> GetTextAsync() =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IClipboard)}.{nameof(GetTextAsync)}", Reason);

		/// <inheritdoc/>
		/// <exception cref="FeatureNotSupportedException">Always thrown.</exception>
		public Task SetTextAsync(string? text) =>
			throw TizenEssentialsSupport.NotSupported($"{nameof(IClipboard)}.{nameof(SetTextAsync)}", Reason);
	}
}
