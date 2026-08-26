using System;
using System.Collections;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell section items (ShellItem's ShellSections) to the Tizen CollectionView adaptor model.
	/// Used for the bottom tab bar.
	/// </summary>
	public class TizenShellSectionItemAdaptor : TizenItemTemplateAdaptor
	{
		TizenItemAppearance? _itemAppearance;

		public TizenShellSectionItemAdaptor(ShellItem shellItem, IEnumerable items) :
			base(shellItem, items, GetSectionItemTemplate())
		{
			ShellItem = shellItem;
		}

		public ShellItem ShellItem { get; }

		public TizenItemAppearance? ItemAppearance
		{
			get => _itemAppearance;
			set => _itemAppearance = value;
		}

		protected override bool IsSelectable => true;

		public override NView CreateNativeView(int index)
		{
			var item = this[index];
			var view = TizenShellSectionItemView.GetSectionItemView(item!, MauiContext);
			view.Parent = ShellItem;
			view.BindingContext = item;
			return view.ToPlatform(MauiContext);
		}

		static DataTemplate GetSectionItemTemplate()
		{
			return new DataTemplate(() =>
			{
				return new Controls.StackLayout();
			});
		}
	}
}
