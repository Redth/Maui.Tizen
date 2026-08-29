using System;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using NAbsoluteLayout = Tizen.NUI.AbsoluteLayout;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for the flyout/drawer content area in a Shell.
	/// </summary>
	public class TizenNavigationView : NView, ITizenNavigationView
	{
		NView? _header;
		NView? _content;
		NView? _footer;

		public TizenNavigationView() : base()
		{
			WidthSpecification = LayoutParamPolicies.MatchParent;
			HeightSpecification = LayoutParamPolicies.MatchParent;

			Layout = new NavigationViewLayout
			{
				LayoutRequest = () => LayoutUpdated(),
			};
		}

		/// <inheritdoc />
		public NView? TargetView => this;

		/// <inheritdoc />
		public NView? Header
		{
			get => _header;
			set
			{
				RemoveView(_header);
				_header = value;
				AddView(_header);
			}
		}

		/// <inheritdoc />
		public NView? Footer
		{
			get => _footer;
			set
			{
				RemoveView(_footer);
				_footer = value;
				AddView(_footer);
			}
		}

		/// <inheritdoc />
		public NView? Content
		{
			get => _content;
			set
			{
				RemoveView(_content);

				_content = value;

				if (_content != null)
				{
					_content.WidthSpecification = LayoutParamPolicies.MatchParent;
					_content.HeightSpecification = LayoutParamPolicies.MatchParent;
					Add(_content);
				}
			}
		}

		void RemoveView(NView? view)
		{
			if (view != null)
				base.Remove(view);
		}

		void AddView(NView? view)
		{
			if (view != null)
			{
				view.WidthSpecification = LayoutParamPolicies.MatchParent;
				view.HeightSpecification = LayoutParamPolicies.WrapContent;
				Add(view);
			}
		}

		void LayoutUpdated()
		{
			var x = (int)Position.X;
			var y = (int)Position.Y;

			if (_header != null)
			{
				_header.Position2D = new Position2D(x, y);
			}

			if (_content != null)
			{
				var contentY = (_header != null) ? y + (int)_header.Size.Height : y;
				_content.Position2D = new Position2D(x, contentY);
			}

			if (_footer != null)
			{
				var footerY = (int)Size.Height - (int)(_footer.Size.Height);
				_footer.Position2D = new Position2D(x, footerY);
			}
		}

		class NavigationViewLayout : NAbsoluteLayout
		{
			public Action? LayoutRequest { get; set; }

			protected override void OnLayout(bool changed, LayoutLength left, LayoutLength top, LayoutLength right, LayoutLength bottom)
			{
				LayoutRequest?.Invoke();
				base.OnLayout(changed, left, top, right, bottom);
			}
		}
	}
}
