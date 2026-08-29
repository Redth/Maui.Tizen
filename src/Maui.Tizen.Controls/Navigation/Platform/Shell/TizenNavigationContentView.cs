using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using NLayoutGroup = Tizen.NUI.LayoutGroup;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Platform view for the main content area (with title/toolbar slot) in a Shell.
	/// </summary>
	public class TizenNavigationContentView : NView, ITizenNavigationContentView, ITizenToolbarContainer
	{
		NView? _titleView;
		NView? _content;

		public TizenNavigationContentView() : base()
		{
			WidthSpecification = LayoutParamPolicies.MatchParent;
			HeightSpecification = LayoutParamPolicies.MatchParent;

			Layout = new LinearLayout
			{
				LinearOrientation = LinearLayout.Orientation.Vertical
			};
		}

		/// <inheritdoc />
		public NView? TargetView => this;

		/// <inheritdoc />
		public NView? TitleView
		{
			get => _titleView;
			set
			{
				if (_titleView != null)
					Remove(_titleView);

				_titleView = value;

				if (_titleView != null)
				{
					Add(_titleView);
					(_titleView.Layout as NLayoutGroup)?.ChangeLayoutSiblingOrder(0);
					_titleView.RaiseToTop();
				}
			}
		}

		void ITizenToolbarContainer.SetToolbar(TizenToolbarView toolbar) => SetToolbar(toolbar);

		internal void SetToolbar(TizenToolbarView toolbar)
		{
			ArgumentNullException.ThrowIfNull(toolbar);

			if (ReferenceEquals(_titleView, toolbar))
				return;

			ClearToolbar();
			_titleView = toolbar;
			Add(toolbar);
			(toolbar.Layout as NLayoutGroup)?.ChangeLayoutSiblingOrder(0);
			toolbar.RaiseToTop();
		}

		internal void ClearToolbar()
		{
			if (_titleView is null)
				return;

			var outgoing = _titleView;
			_titleView = null;
			Remove(outgoing);
			outgoing.Dispose();
		}

		/// <inheritdoc />
		public NView? Content
		{
			get => _content;
			set
			{
				if (_content != null)
					Remove(_content);

				_content = value;

				if (_content != null)
				{
					_content.HeightSpecification = LayoutParamPolicies.MatchParent;
					_content.WidthSpecification = LayoutParamPolicies.MatchParent;
					Add(_content);
					if (_titleView != null)
						_content.LowerBelow(_titleView);
				}
			}
		}
	}
}
