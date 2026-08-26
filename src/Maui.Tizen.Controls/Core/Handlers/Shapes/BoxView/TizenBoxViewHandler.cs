// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.BoxViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named BoxViewHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Controls.Handlers
{
	/// <summary>Tizen handler for <see cref="BoxView"/>.</summary>
	/// <remarks>
	/// BoxView adds no shape-specific properties over <see cref="IShapeView"/>; it exists so the
	/// control can be registered independently, exactly as upstream.
	/// </remarks>
	public class TizenBoxViewHandler : TizenShapeViewHandler
	{
		public TizenBoxViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenBoxViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenBoxViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
