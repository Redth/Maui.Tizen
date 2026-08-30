using System.ComponentModel;
using System.Runtime.CompilerServices;
using GColor = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Observable appearance settings for Shell item views (tab bars, flyout items, etc.).
	/// </summary>
	/// <remarks>
	/// Bindings to these properties allow the adaptor to push color updates to all visible items
	/// without recreating them.
	/// </remarks>
	internal class TizenItemAppearance : INotifyPropertyChanged
	{
		GColor? _foregroundColor;
		GColor? _backgroundColor;
		GColor? _titleColor;
		GColor? _unselectedColor;

		/// <summary>
		/// Gets or sets the foreground/accent color.
		/// </summary>
		public GColor? ForegroundColor
		{
			get => _foregroundColor;
			set
			{
				if (_foregroundColor != value)
				{
					_foregroundColor = value;
					NotifyPropertyChanged();
				}
			}
		}

		/// <summary>
		/// Gets or sets the background color.
		/// </summary>
		public GColor? BackgroundColor
		{
			get => _backgroundColor;
			set
			{
				if (_backgroundColor != value)
				{
					_backgroundColor = value;
					NotifyPropertyChanged();
				}
			}
		}

		/// <summary>
		/// Gets or sets the selected/title color.
		/// </summary>
		public GColor? TitleColor
		{
			get => _titleColor;
			set
			{
				if (_titleColor != value)
				{
					_titleColor = value;
					NotifyPropertyChanged();
				}
			}
		}

		/// <summary>
		/// Gets or sets the unselected item color.
		/// </summary>
		public GColor? UnselectedColor
		{
			get => _unselectedColor;
			set
			{
				if (_unselectedColor != value)
				{
					_unselectedColor = value;
					NotifyPropertyChanged();
				}
			}
		}

		/// <inheritdoc />
		public event PropertyChangedEventHandler? PropertyChanged;

		void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
