using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using NLayoutGroup = Tizen.NUI.LayoutGroup;
using NLinearLayout = Tizen.NUI.LinearLayout;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// A single page within <see cref="TizenStackNavigationManager"/>: an optional title view
	/// stacked above the page content.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.NaviPage</c> in dotnet/maui. Behaviour is preserved.
	/// </para>
	/// <para>
	/// Renamed to <c>TizenNaviPage</c> because upstream declares <c>NaviPage</c> in the
	/// <b>neutral</b> <c>Microsoft.Maui</c> namespace, which is exactly the kind of name that would
	/// collide (CS0433) for a consumer referencing both assemblies.
	/// </para>
	/// <para>
	/// This came in with the navigation manager: it is required by it, and porting it here is what
	/// lets the raw imported <c>NaviPage</c> stay uncompiled.
	/// </para>
	/// </remarks>
	public class TizenNaviPage : NView, IContainable<NView>
	{
		readonly ObservableCollection<NView> _children = new();

		TitleView? _titleView;
		NView? _content;

		/// <summary>Initializes a new instance of the <see cref="TizenNaviPage"/> class.</summary>
		public TizenNaviPage()
		{
			HeightSpecification = LayoutParamPolicies.MatchParent;
			WidthSpecification = LayoutParamPolicies.MatchParent;

			Layout = new NLinearLayout
			{
				LinearOrientation = NLinearLayout.Orientation.Vertical,
			};

			_children.CollectionChanged += OnCollectionChanged;
		}

		/// <summary>
		/// Gets or sets the title view shown above the content. Assigning replaces and disposes any
		/// previous one.
		/// </summary>
		public TitleView? TitleView
		{
			get => _titleView;
			set
			{
				if (_titleView is not null)
				{
					_titleView.Unparent();
					_titleView.Dispose();
					_titleView = null;
				}

				_titleView = value;

				if (_titleView is not null)
				{
					Add(_titleView);

					// The title must sit above the content in the vertical layout.
					(_titleView.Layout as NLayoutGroup)?.ChangeLayoutSiblingOrder(0);
				}
			}
		}

		/// <summary>
		/// Gets or sets the page content. Assigning replaces and disposes any previous content.
		/// </summary>
		public NView? Content
		{
			get => _content;
			set
			{
				if (_content is not null)
				{
					_content.Unparent();
					_content.Dispose();
					_content = null;
				}

				_content = value;

				if (_content is not null)
				{
					_content.HeightSpecification = LayoutParamPolicies.MatchParent;
					_content.WidthSpecification = LayoutParamPolicies.MatchParent;
					Add(_content);
				}
			}
		}

		IList<NView> IContainable<NView>.Children => _children;

		void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
				Content = e.NewItems[0] as NView;
			else if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
				Content = null;
		}
	}
}
