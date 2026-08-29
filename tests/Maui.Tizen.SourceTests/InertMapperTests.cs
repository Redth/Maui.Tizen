using Microsoft.Maui;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Detects mapper keys that dispatch cleanly but whose body does nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the second half of the hazard behind the dispatch tests, and the more insidious one. A
/// hard cast at least throws. An inert body is silent: the key is present, parity reports it as
/// covered, dispatch succeeds, and the property still never reaches the screen.
/// </para>
/// <para>
/// The cause is structural. MAUI 11 ships no Tizen target framework, so this repository consumes
/// the NEUTRAL <c>net11.0</c> assembly, where <c>PlatformView</c> is <see cref="object"/> and the
/// platform half of each mapper does not exist. Upstream, these same keys had real Tizen bodies.
/// </para>
/// </remarks>
public class InertMapperTests
{
	/// <summary>
	/// Keys reachable through <c>ViewMapper</c> whose neutral body is empty.
	/// </summary>
	/// <remarks>
	/// Recorded rather than merely asserted, so the list is reviewable and any NEW inert key fails
	/// the test instead of quietly joining the set. Every entry here is a property that silently
	/// does nothing on Tizen today.
	/// </remarks>
	static readonly string[] KnownInertViewMapperKeys =
	{
		// REGRESSION against upstream. Upstream's net*-tizen build routed every one of these
		// through TransformationExtensions.UpdateTransformation, which really did move, scale and
		// rotate the NUI view. Consuming the neutral assembly loses those bodies, so transforms
		// currently do nothing on Tizen. ViewMapper is chained by core, Wave A and Wave B alike,
		// so the fix belongs in the shared Tizen view handler, not in any one wave.
		"AnchorX",
		"AnchorY",
		"Rotation",
		"RotationX",
		"RotationY",
		"Scale",
		"ScaleX",
		"ScaleY",
		"TranslationX",
		"TranslationY",

		// NOT a regression: upstream's own Tizen ViewExtensions.UpdateToolTip is an empty body,
		// so Tizen has never shown tooltips. Listed for completeness, not as a defect.
		"ToolTip",
	};

	[Fact]
	public void InertViewMapperKeysMatchTheRecordedSet()
	{
		ControlsHost.EnsureBuilt();

		var inert = ControlsHost.AllMappings
			.Where(m => m is { Owner: "ViewHandler", Field: "ViewMapper" })
			.Where(m => m.HasInertBody)
			.Select(m => m.Key)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(k => k, StringComparer.Ordinal)
			.ToList();

		var expected = KnownInertViewMapperKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();

		// A NEW inert key means MAUI moved something else behind a platform guard, and a Tizen body
		// is needed for it. A key LEAVING the set means the gap closed and the record should shrink.
		Assert.Equal(expected, inert);
	}

	/// <summary>
	/// The keys Wave B declares itself must not be inert unless deliberately documented.
	/// </summary>
	/// <remarks>
	/// Wave B's own no-ops are recorded in docs/wave-b-mapper-parity.json with a justification, and
	/// a separate test requires each to explain itself. This checks the complementary property: a
	/// Wave B key that is <em>supposed</em> to do something must have a real body behind it.
	/// </remarks>
	[Fact]
	public void WaveBDeclaredKeysAreNotAccidentallyInert()
	{
		var offenders = WaveBSource.Handlers
			.SelectMany(h => h.PropertyMappers.Concat(h.CommandMappers).Select(m => (Handler: h, Mapper: m)))
			.Where(x => x.Mapper.IsNoOp && string.IsNullOrWhiteSpace(x.Mapper.Reason))
			.Select(x => $"{x.Handler.TypeName}.{x.Mapper.Method}")
			.ToList();

		Assert.Empty(offenders);
	}
}
