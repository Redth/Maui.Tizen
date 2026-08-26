using System;
using System.Collections;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell content items (ShellSection's ShellContents) to the Tizen CollectionView adaptor model.
	/// Used for the top tab bar.
	/// </summary>
	public class TizenShellContentItemAdaptor : TizenItemTemplateAdaptor
	{
		TizenItemAppearance? _itemAppearance;

		public TizenShellContentItemAdaptor(ShellSection shellSection, IEnumerable items) :
			base(shellSection, items, GetContentItemTemplate())
		{
			ShellSection = shellSection;
		}

		public ShellSection ShellSection { get; }

		public TizenItemAppearance? ItemAppearance
		{
			get => _itemAppearance;
			set => _itemAppearance = value;
		}

		protected override bool IsSelectable => true;

		public override NView CreateNativeView(int index)
		{
			var item = this[index];
			var view = TizenShellContentItemView.GetContentItemView(item!, MauiContext);
			view.Parent = ShellSection;
			view.BindingContext = item;
			return view.ToPlatform(MauiContext);
		}

		static DataTemplate GetContentItemTemplate()
		{
			return new DataTemplate(() =>
			{
				return new Controls.StackLayout();
			});
		}
	}
}
