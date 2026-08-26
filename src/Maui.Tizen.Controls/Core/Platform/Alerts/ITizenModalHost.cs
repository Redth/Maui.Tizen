using System;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Coordinates dialogs with the Tizen modal navigation stack.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tizen dialogs are not rendered by the modal stack itself; they are native NUI popups that
	/// float above the current page. The modal stack still has to be told that something modal is
	/// on screen so that back-button handling, page appearing/disappearing notifications and the
	/// modal page ordering stay correct while a dialog is open.
	/// </para>
	/// <para>
	/// The default implementation pushes a placeholder popup page onto the Tizen navigation stack
	/// for the duration of the dialog, matching the behaviour of the original NUI backend.
	/// </para>
	/// </remarks>
	public interface ITizenModalHost
	{
		/// <summary>
		/// Runs <paramref name="dialogOperation"/> while the modal stack is holding a placeholder
		/// entry for the dialog, and removes that entry once the operation completes.
		/// </summary>
		/// <param name="dialogOperation">The dialog interaction to run.</param>
		/// <remarks>
		/// The returned task completes after the placeholder entry has been popped. Implementations
		/// must pop the placeholder even when <paramref name="dialogOperation"/> faults, otherwise
		/// the modal stack would be left permanently unbalanced.
		/// </remarks>
		Task RunModalAsync(Func<Task> dialogOperation);
	}

	/// <summary>
	/// Resolves the native Tizen window that a <see cref="IMauiContext"/> belongs to.
	/// </summary>
	/// <remarks>
	/// Alert requests are routed per window. The alert subscription compares the window that owns
	/// the requesting page against the window it was created for, so that a dialog raised on one
	/// window is never presented on another. This contract exists so that window affinity can be
	/// exercised without a native NUI window.
	/// </remarks>
	public interface ITizenPlatformWindowProvider
	{
		/// <summary>
		/// Gets an object identifying the native window backing <paramref name="context"/>,
		/// or <see langword="null"/> when the context is not attached to a window.
		/// </summary>
		/// <param name="context">The context to resolve. May be <see langword="null"/>.</param>
		object? GetPlatformWindow(IMauiContext? context);
	}
}
