using System.Text.RegularExpressions;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Pins the reason Wave C is not yet part of the API15 acceptance lane, and fails as soon as that
/// reason stops being true.
/// </summary>
/// <remarks>
/// <para>
/// Wave C is compiled by <c>tests/Maui.Tizen.Core.RefPackCompile</c> - the lane that type-checks
/// against the real net11 public MAUI packages and the Samsung.Tizen.Ref.API15 reference
/// assemblies - but only when <c>MauiTizenWaveCAcceptance=true</c>. It is off because Wave C
/// consumes two Tizen platform primitives that the net11 MAUI surface does not publish and that
/// Core has not yet re-homed.
/// </para>
/// <para>
/// A disabled gate with a comment explaining it rots the moment the blocker clears, and nobody
/// notices because the build is green either way. These tests exist so that cannot happen: the
/// blocked-type list is pinned, and <see cref="AcceptanceGateMustBeReopenedOnceCoreLandsThePrimitives"/>
/// fails the moment Core provides replacements.
/// </para>
/// </remarks>
public class WaveCAcceptanceGateTests
{
	/// <summary>
	/// The only two types keeping Wave C out of the acceptance lane, and the Core types expected to
	/// replace them.
	/// </summary>
	/// <remarks>
	/// Names confirmed by the Core session on 2026-08-26 (first landed in 163677d), under
	/// <c>Microsoft.Maui.Platforms.Tizen</c>. Wave C waits for Core's final <em>reviewed</em> head
	/// before rebasing, so these are recorded rather than adopted.
	/// </remarks>
	public static readonly (string InTreeType, string ExpectedCoreType)[] BlockedPrimitives =
	{
		("MauiToolbar", "TizenToolbarView"),
		("StackNavigationManager", "TizenStackNavigationManager"),

		// NaviPage is not referenced by Wave C directly - Core's stack manager owns it - but it is
		// part of the same agreed rename set, so it is tracked here to keep one list rather than two.
		("NaviPage", "TizenNaviPage"),
	};

	static string SourcesProps() => File.ReadAllText(RepoPaths.Combine("eng", "Maui.Tizen.WaveC.Sources.props"));

	[Fact]
	public void EveryWaveCSourceAndCatalogPageIsListedForTheAcceptanceLane()
	{
		// The gate is only defensible if the lane would compile *everything* once it is flipped on.
		// A file quietly missing from the list would be permanently unverified.
		var props = SourcesProps();

		var listed = Regex.Matches(props, @"Include=""\$\((?:MauiTizenNavigationDir|MauiTizenCatalogDir)\)([^""]+)""")
			.Select(m => m.Groups[1].Value.Replace('\\', '/'))
			.ToHashSet(StringComparer.Ordinal);

		var onDisk = new List<string>();

		foreach (var (root, prefix) in new[]
		{
			(RepoPaths.Combine("src", "Maui.Tizen.Controls.Navigation"), ""),
			(RepoPaths.Combine("samples", "Controls", "Catalog"), ""),
		})
		{
			if (!Directory.Exists(root))
			{
				continue;
			}

			onDisk.AddRange(Directory
				.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
				.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				.Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
				.Select(p => prefix + Path.GetRelativePath(root, p).Replace('\\', '/')));
		}

		var missing = onDisk.Where(f => !listed.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();

		Assert.True(
			missing.Count == 0,
			"These Wave C sources are not listed in eng/Maui.Tizen.WaveC.Sources.props, so the API15 "
				+ "acceptance lane would never compile them: " + string.Join(", ", missing));
	}

	[Fact]
	public void TheAcceptanceGateDocumentsExactlyWhichPrimitivesBlockIt()
	{
		var props = SourcesProps();

		foreach (var (inTree, expectedCore) in BlockedPrimitives)
		{
			Assert.Contains(inTree, props, StringComparison.Ordinal);
			Assert.Contains(expectedCore, props, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Fails once Core publishes the missing primitives, forcing the acceptance gate back open.
	/// </summary>
	/// <remarks>
	/// This is the expiry half of the gate. It looks for a Core-owned replacement for each blocked
	/// primitive; when one appears, the gate is no longer justified and this test says so, rather
	/// than leaving Wave C permanently unverified behind a flag nobody revisits.
	/// </remarks>
	[Fact]
	public void AcceptanceGateMustBeReopenedOnceCoreLandsThePrimitives()
	{
		var coreRoot = RepoPaths.Combine("src", "Maui.Tizen.Core");

		if (!Directory.Exists(coreRoot))
		{
			return;
		}

		var coreSource = string.Concat(Directory
			.EnumerateFiles(coreRoot, "Tizen*.cs", SearchOption.AllDirectories)
			.Select(File.ReadAllText));

		var landed = BlockedPrimitives
			.Where(p => Regex.IsMatch(coreSource, $@"\b(class|interface|record)\s+{Regex.Escape(p.ExpectedCoreType)}\b"))
			.Select(p => p.ExpectedCoreType)
			.ToList();

		Assert.True(
			landed.Count == 0,
			"Core now provides " + string.Join(", ", landed) + ". Re-point Wave C at it, delete the "
				+ "corresponding entry from WaveCAcceptanceGateTests.BlockedPrimitives, and turn the "
				+ "acceptance lane on by defaulting MauiTizenWaveCAcceptance to true in "
				+ "eng/Maui.Tizen.WaveC.Sources.props.");
	}

	/// <summary>
	/// Wave C must not work around the gap by declaring its own copy of a Core primitive.
	/// </summary>
	[Fact]
	public void WaveCDoesNotDeclareItsOwnCopyOfABlockedPrimitive()
	{
		var offenders = new List<string>();

		foreach (var file in WaveCSource.Files)
		{
			var text = File.ReadAllText(file);

			foreach (var (inTree, expectedCore) in BlockedPrimitives)
			{
				foreach (var name in new[] { inTree, expectedCore })
				{
					if (Regex.IsMatch(text, $@"\b(class|interface|record)\s+{Regex.Escape(name)}\b"))
					{
						offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)} declares {name}");
					}
				}
			}
		}

		Assert.True(
			offenders.Count == 0,
			"These would create a second authoritative copy of a Core-owned primitive: "
				+ string.Join(", ", offenders));
	}

	/// <summary>
	/// The head Wave C's integration check was last actually run against.
	/// </summary>
	/// <remarks>
	/// Recorded so that "Core is complete for Wave C" is always attributable to a specific commit.
	/// It was reported once against a head that had already moved on by 19 commits, which is an easy
	/// mistake to make and a hard one to notice.
	/// </remarks>
	public const string LastVerifiedCoreHead = "4e256f1271";

	/// <summary>
	/// Keeps the recorded verification honest about which head it applies to.
	/// </summary>
	/// <remarks>
	/// This does not try to detect drift - the test host has no reliable view of other branches, and
	/// a test that silently no-ops is worse than none. It pins the claim to a commit so a reviewer
	/// can compare it against the live head, and so the acceptance gate cannot be opened on the
	/// strength of a verification whose subject is no longer identifiable.
	/// </remarks>
	[Fact]
	public void TheRecordedIntegrationVerificationNamesTheHeadItWasRunAgainst()
	{
		var props = File.ReadAllText(
			RepoPaths.Combine("eng", "Maui.Tizen.WaveC.Sources.props"));

		Assert.Contains(LastVerifiedCoreHead, props, StringComparison.Ordinal);

		// And it must be described as stale rather than as standing approval, because the gate is
		// still closed and the predecessor heads have moved.
		Assert.Contains("STALE", props, StringComparison.Ordinal);
	}

}
