using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Presents the overflow ("more") list for secondary toolbar items.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Secondary toolbar items are rendered on Tizen as an action sheet pushed onto the modal
	/// stack. That presentation belongs to the alerts/dialogs area of the backend, not to the
	/// toolbar, so Wave C declares this seam and consumes it rather than shipping a second
	/// action-sheet implementation.
	/// </para>
	/// <para>
	/// Register an implementation with the app's service collection. When no implementation is
	/// registered the overflow button is not created at all, which is a visible no-op rather than
	/// a crash - see the <c>Unsupported</c> classification for <c>ToolbarItems (secondary)</c> in
	/// <c>Parity/MapperParity.json</c>.
	/// </para>
	/// </remarks>
	public interface IToolbarSecondaryActionPresenter
	{
		/// <summary>
		/// Shows <paramref name="actions"/> and resolves to the selected index, or <c>-1</c> when
		/// the user cancels.
		/// </summary>
		Task<int> PresentAsync(
			IReadOnlyList<string?> actions,
			string cancelLabel,
			CancellationToken cancellationToken = default);
	}
}
