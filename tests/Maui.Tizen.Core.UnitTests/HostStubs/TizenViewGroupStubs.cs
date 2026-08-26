using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Host-buildable stand-in for the NUI <c>ViewGroup</c> used by
	/// <see cref="Handlers.TizenContentViewHandler"/>.
	/// </summary>
	public class TizenContentViewGroup : TizenPlatformView
	{
		/// <summary>Initializes a new instance of the <see cref="TizenContentViewGroup"/> class.</summary>
		/// <param name="virtualView">The cross-platform view this group renders.</param>
		public TizenContentViewGroup(IView? virtualView) => VirtualView = virtualView;

		/// <summary>Gets the cross-platform view this group renders.</summary>
		public IView? VirtualView { get; }

		/// <summary>Gets or sets the cross-platform measure callback.</summary>
		public Func<double, double, Size>? CrossPlatformMeasure { get; set; }

		/// <summary>Gets or sets the cross-platform arrange callback.</summary>
		public Func<Rect, Size>? CrossPlatformArrange { get; set; }

		/// <summary>Flags the group as needing a new measure pass.</summary>
		public void SetNeedMeasureUpdate()
		{
		}
	}

	/// <summary>
	/// Host-buildable stand-in for the NUI <c>ViewGroup</c> used by
	/// <see cref="Handlers.TizenLayoutHandler"/>.
	/// </summary>
	public class TizenLayoutViewGroup : TizenContentViewGroup
	{
		/// <summary>Initializes a new instance of the <see cref="TizenLayoutViewGroup"/> class.</summary>
		/// <param name="virtualView">The cross-platform view this group renders.</param>
		public TizenLayoutViewGroup(IView? virtualView)
			: base(virtualView)
		{
		}

		/// <summary>Gets or sets a value indicating whether input passes through this group.</summary>
		public bool InputTransparent { get; set; }
	}
}
