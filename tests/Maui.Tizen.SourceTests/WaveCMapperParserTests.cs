namespace Maui.Tizen.SourceTests;

public class WaveCMapperParserTests
{
	[Fact]
	public void ParserSeparatesPropertyAndCommandKeysAndResolvesMemberAccess()
	{
		var handler = WaveBSource.Parse(RepoPaths.Combine(
			"tests", "Maui.Tizen.SourceTests", "Fixtures", "MapperParser.member-access.txt"))
			.Single(source => source.TypeName == "TizenParserFixture");

		Assert.Equal(
			["Literal", "Name", "TabBarIsVisible"],
			handler.PropertyMappers.Select(mapper => mapper.Key).Order(StringComparer.Ordinal));
		Assert.Equal(["DoThing"], handler.CommandMappers.Select(mapper => mapper.Key));
	}

	[Fact]
	public void DrawerToggleIsNotReportedAsANeutralMapperKey()
	{
		var toolbar = WaveCSource.Handlers.Single(handler => handler.TypeName == "TizenToolbarHandler");
		Assert.DoesNotContain(toolbar.PropertyMappers, entry => entry.Key == "DrawerToggleVisible");
		var neutral = NeutralMaui.FindHandler("ToolbarHandler");
		Assert.NotNull(neutral);
		Assert.DoesNotContain("DrawerToggleVisible", NeutralMaui.MapperKeys(neutral!, "Mapper"));
	}

	[Fact]
	public void NavigationCommandMapperCoversNeutralCommands()
	{
		var handler = WaveCSource.Handlers.Single(source => source.TypeName == "TizenNavigationViewHandler");
		var neutral = NeutralMaui.FindHandler("NavigationViewHandler");
		Assert.NotNull(neutral);

		var neutralCommands = NeutralMaui.MapperKeys(neutral!, "CommandMapper");
		Assert.Contains("RequestNavigation", neutralCommands);
		Assert.Contains(handler.CommandMappers, mapper => mapper.Key == "RequestNavigation");
	}
}
