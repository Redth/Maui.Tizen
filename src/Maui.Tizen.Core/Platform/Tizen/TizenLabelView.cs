using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Platform view used by <see cref="Handlers.TizenLabelHandler"/>.
	/// </summary>
	/// <remarks>
	/// dotnet/maui uses <c>Tizen.UIExtensions.NUI.Label</c> directly. This backend owns a derived
	/// type instead so the handler's platform view type belongs to this package, which keeps the
	/// generic handler signatures stable if the UIExtensions type ever changes shape.
	/// </remarks>
	public class TizenLabelView : Label
	{
	}
}
