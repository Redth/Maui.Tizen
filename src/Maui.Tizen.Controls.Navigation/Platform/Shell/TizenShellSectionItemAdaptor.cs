using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Tizen.UIExtensions.NUI;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Adapts Shell section items (ShellItem's ShellSections) to the Tizen CollectionView adaptor model.
	/// Used for the bottom tab bar.
	/// </summary>
	public class TizenShellSectionItemAdaptor : TizenItemTemplateAdaptor
	{
		readonly Dictionary<NView, View> _shellNativeMauiTable = new();
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
			var native = view.ToPlatform(MauiContext);

			// Register native-to-MAUI mapping for selection state tracking
			_shellNativeMauiTable[native] = view;
			ItemSelectionState.TrackEnabledState(view);

			return native;
		}

		public override void UpdateViewState(NView view, ViewHolderState state)
		{
			base.UpdateViewState(view, state);
			if (_shellNativeMauiTable.TryGetValue(view, out View? formsView))
			{
				switch (state)
				{
					case ViewHolderState.Focused:
						ItemSelectionState.SetItemFocused(formsView, true);
						break;
					case ViewHolderState.Normal:
						ItemSelectionState.Reset(formsView);
						break;
					case ViewHolderState.Selected:
						ItemSelectionState.SetItemSelectedAndUnfocused(formsView, true);
						break;
				}
			}
		}

		public override void RemoveNativeView(NView native)
		{
			if (_shellNativeMauiTable.TryGetValue(native, out View? view))
			{
				ItemSelectionState.UntrackEnabledState(view);
				_shellNativeMauiTable.Remove(native);

				if (view.Handler is ITizenPlatformViewHandler handler)
				{
					handler.Dispose();
					view.Handler = null;
				}
			}
			base.RemoveNativeView(native);
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
