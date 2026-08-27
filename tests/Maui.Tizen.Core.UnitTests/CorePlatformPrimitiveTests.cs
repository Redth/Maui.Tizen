using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Source-level coverage for the Core-owned NUI primitives consumed by the Wave C handlers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="TizenToolbarView"/>, <see cref="TizenStackNavigationManager"/> and
	/// <see cref="TizenNaviPage"/> all derive from real NUI types, so they cannot be instantiated
	/// on the host - there are no stand-ins deep enough to construct a <c>Tizen.NUI</c> view. Their
	/// behaviour is type-checked against the real TizenFX reference assemblies by
	/// <c>tests/Maui.Tizen.Core.RefPackCompile</c>.
	/// </para>
	/// <para>
	/// What these tests pin is the part that a compile cannot: that the types exist under the
	/// agreed names and namespace, are wired into the build lanes and the public API baseline, and
	/// that the raw imported originals stay uncompiled. Those are exactly the things that would
	/// silently regress and break Wave C.
	/// </para>
	/// </remarks>
	public class CorePlatformPrimitiveTests
	{
		static string RepositoryRoot
		{
			get
			{
				var dir = new DirectoryInfo(AppContext.BaseDirectory);
				while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Maui.Tizen.slnx")))
					dir = dir.Parent;

				return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
			}
		}

		static string SourcesProps => File.ReadAllText(
			Path.Combine(RepositoryRoot, "eng/Maui.Tizen.Core.Sources.props"));

		static string[] ProductBaseline => File.ReadAllLines(
			Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Unshipped.txt"));

		[Theory]
		[InlineData("Platform/Tizen/TizenToolbarView.cs")]
		[InlineData("Platform/Tizen/TizenStackNavigationManager.cs")]
		[InlineData("Platform/Tizen/TizenNaviPage.cs")]
		[InlineData("Platform/Tizen/TizenFlyoutView.cs")]
		[InlineData("Platform/Tizen/TizenFlyoutViewExtensions.cs")]
		public void PrimitiveSourceExists(string relativePath) =>
			Assert.True(
				File.Exists(Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core", relativePath)),
				$"{relativePath} is missing.");

		[Theory]
		[InlineData("TizenToolbarView.cs")]
		[InlineData("TizenStackNavigationManager.cs")]
		[InlineData("TizenNaviPage.cs")]
		[InlineData("TizenFlyoutView.cs")]
		[InlineData("TizenFlyoutViewExtensions.cs")]
		public void PrimitiveIsCompiledByTheProductAndRefPackLanes(string fileName)
		{
			// Sources are listed explicitly rather than globbed, so a new file that is never added
			// here compiles nowhere and fails only when Wave C tries to use it.
			Assert.Contains(fileName, SourcesProps, StringComparison.Ordinal);
		}

		[Theory]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenToolbarView")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.ITizenToolbarContainer")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenStackNavigationManager")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenNaviPage")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenFlyoutView")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenTVFlyoutView")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenFlyoutViewExtensions")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenFlyoutBehaviorExtensions")]
		public void PrimitiveIsDeclaredInThePublicApiBaseline(string typeName) =>
			Assert.Contains(ProductBaseline, e => e.Contains(typeName, StringComparison.Ordinal));

		[Theory]
		// The toolbar contract Wave C's toolbar handler drives.
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenToolbarView.Expand() -> void")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenToolbarView.Collapse() -> void")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenToolbarView.SendIconPressed() -> void")]
		// The container contract, implemented by the navigation manager.
		[InlineData("Microsoft.Maui.Platforms.Tizen.ITizenToolbarContainer.SetToolbar(")]
		// The navigation contract Wave C's navigation handler drives.
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenStackNavigationManager.Connect(")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenStackNavigationManager.Disconnect() -> void")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenStackNavigationManager.RequestNavigation(")]
		// Toolbar title/menu, which Wave C's toolbar handler maps.
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenToolbarView.UpdateTitle(")]
		[InlineData("Microsoft.Maui.Platforms.Tizen.TizenToolbarView.UpdateMenuButton(")]
		// The full DrawerView surface Wave C's flyout handler maps.
		[InlineData("TizenFlyoutViewExtensions.UpdateFlyout(")]
		[InlineData("TizenFlyoutViewExtensions.UpdateDetail(")]
		[InlineData("TizenFlyoutViewExtensions.UpdateIsPresented(")]
		[InlineData("TizenFlyoutViewExtensions.UpdateFlyoutBehavior(")]
		[InlineData("TizenFlyoutViewExtensions.UpdateFlyoutWidth(")]
		[InlineData("TizenFlyoutViewExtensions.UpdateIsGestureEnabled(")]
		[InlineData("TizenFlyoutBehaviorExtensions.ToTizenDrawerBehavior(")]
		public void WaveCFacingMemberIsPublished(string signatureFragment)
		{
			// These are the exact members Wave C codes against. Pinning them here means a rename
			// breaks this build rather than Wave C's.
			Assert.Contains(ProductBaseline, e => e.Contains(signatureFragment, StringComparison.Ordinal));
		}

		[Theory]
		[InlineData("MauiToolbar.cs")]
		[InlineData("StackNavigationManager.cs")]
		[InlineData("NaviPage.cs")]
		[InlineData("MauiFlyoutView.cs")]
		[InlineData("MauiTVFlyoutView.cs")]
		[InlineData("FlyoutViewExtensions.cs")]
		[InlineData("ToolbarExtensions.cs")]
		public void SupersededImportedSourceIsNotCompiled(string fileName)
		{
			// The whole point of owning these types: the raw imported originals must stay
			// uncompiled, or they would collide with the ported ones.
			var compiled = SourcesProps
				.Split('\n')
				.Where(l => l.Contains("Include=", StringComparison.Ordinal))
				.ToArray();

			Assert.DoesNotContain(compiled, l =>
				l.Contains($"/{fileName}", StringComparison.Ordinal) ||
				l.Contains($"\\{fileName}", StringComparison.Ordinal));
		}

		[Fact]
		public void PortedPrimitivesDoNotReuseNeutralMauiTypeNames()
		{
			// MauiToolbar, StackNavigationManager and NaviPage all exist in the net*-tizen build of
			// Microsoft.Maui.dll. Reusing those names would be a CS0433 hazard for any consumer
			// referencing both assemblies, which is why each was renamed.
			foreach (var forbidden in new[]
			{
				"class MauiToolbar",
				"class StackNavigationManager",
				"class NaviPage",
				"class MauiFlyoutView",
				"class MauiTVFlyoutView",
				"class FlyoutViewExtensions",
			})
			{
				foreach (var file in new[]
				{
					"Platform/Tizen/TizenToolbarView.cs",
					"Platform/Tizen/TizenStackNavigationManager.cs",
					"Platform/Tizen/TizenNaviPage.cs",
					"Platform/Tizen/TizenFlyoutView.cs",
					"Platform/Tizen/TizenFlyoutViewExtensions.cs",
				})
				{
					var text = File.ReadAllText(Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core", file));
					Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
				}
			}
		}

		[Theory]
		[InlineData("OnNavigationFinished")]
		[InlineData("CreateNavigationItem")]
		[InlineData("OnPageRemoved")]
		[InlineData("InitializeStack")]
		public void NavigationManagerExposesTheOverrideSeamWaveCNeeds(string member)
		{
			// Wave C derives from TizenStackNavigationManager rather than re-implementing it, so
			// these seams are contract. Losing one silently forces a fork of the navigation logic.
			var text = File.ReadAllText(Path.Combine(
				RepositoryRoot, "src/Maui.Tizen.Core/Platform/Tizen/TizenStackNavigationManager.cs"));

			Assert.Contains($"protected virtual", text, StringComparison.Ordinal);
			Assert.Contains(member, text, StringComparison.Ordinal);
		}

		[Theory]
		[InlineData("TizenFlyoutView")]
		[InlineData("TizenTVFlyoutView")]
		public void FlyoutViewForwardsToolbarToItsContent(string typeName)
		{
			// A flyout's toolbar belongs to the detail page, so both drawers must be pass-through
			// toolbar containers rather than hosting the toolbar themselves.
			var text = File.ReadAllText(Path.Combine(
				RepositoryRoot, "src/Maui.Tizen.Core/Platform/Tizen/TizenFlyoutView.cs"));

			Assert.Contains($"class {typeName}", text, StringComparison.Ordinal);
			Assert.Contains("ITizenToolbarContainer", text, StringComparison.Ordinal);
		}

		[Fact]
		public void NavigationManagerImplementsTheToolbarContainerContract()
		{
			var text = File.ReadAllText(Path.Combine(
				RepositoryRoot, "src/Maui.Tizen.Core/Platform/Tizen/TizenStackNavigationManager.cs"));

			Assert.Contains("ITizenToolbarContainer", text, StringComparison.Ordinal);
		}

		[Fact]
		public void NoWaveCHandlersLeakedIntoCore()
		{
			// Core owns the primitives; the handlers that drive them belong to Wave C.
			var handlerDir = Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/Handlers");
			var compiledHandlers = SourcesProps;

			foreach (var waveC in new[]
			{
				"TizenToolbarHandler", "TizenNavigationViewHandler", "TizenShellHandler", "TizenFlyoutViewHandler",
			})
			{
				Assert.False(
					File.Exists(Path.Combine(handlerDir, $"{waveC}.cs")),
					$"{waveC} belongs to Wave C, not core.");

				Assert.DoesNotContain($"{waveC}.cs", compiledHandlers, StringComparison.Ordinal);
			}
		}
	}
}
