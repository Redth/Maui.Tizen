using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Maui.Tizen.SourceTests;

public class WaveCItemAppearanceTests
{
	[Fact]
	public void SharedAppearanceRaisesOnlyChangedProperties()
	{
		var appearance = new TizenItemAppearance();
		var changes = new List<string?>();
		appearance.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

		appearance.TitleColor = Colors.Red;
		appearance.TitleColor = Colors.Red;
		appearance.UnselectedColor = Colors.Gray;
		appearance.ForegroundColor = Colors.Blue;
		appearance.BackgroundColor = Colors.White;

		Assert.Equal(
			[
				nameof(TizenItemAppearance.TitleColor),
				nameof(TizenItemAppearance.UnselectedColor),
				nameof(TizenItemAppearance.ForegroundColor),
				nameof(TizenItemAppearance.BackgroundColor),
			],
			changes);
	}
}
