using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Serialises every test class that touches MAUI's static mappers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// MAUI's mappers are process-wide mutable statics, and Controls' <c>RemapForControls</c>
	/// mutates them the first time a Controls type is constructed. xUnit runs test collections in
	/// parallel, so a class forcing that remap can be rewriting <c>ViewHandler.ViewMapper</c> at the
	/// exact moment another class is resolving a key from the mapper that chains it.
	/// </para>
	/// <para>
	/// That produced an intermittent failure in
	/// <c>BackgroundTransitionTests.OrdinaryViewStillClearsAfterTransitionToNull</c> - a test whose
	/// own code never changed - which reproduced roughly one run in three and passed in isolation
	/// every time. The mapper's internal key lookup is not safe against concurrent mutation, so the
	/// Tizen <c>Background</c> entry was transiently invisible.
	/// </para>
	/// <para>
	/// Sharing one collection makes these classes run one at a time. It costs a little wall clock
	/// and removes a whole class of order-dependent failure that would otherwise show up as random
	/// CI redness - which is precisely the kind of flake that teaches people to hit rerun.
	/// </para>
	/// </remarks>
	[CollectionDefinition(Name, DisableParallelization = true)]
	public sealed class StaticMapperCollection
	{
		public const string Name = "MAUI static mappers";
	}

	/// <summary>
	/// Serialises every test class that mutates the process-wide display density override.
	/// </summary>
	/// <remarks>
	/// <see cref="TizenDisplayDensity.SetDensityOverride"/> is global mutable state, so two classes
	/// setting it in parallel see each other's values. That produced an intermittent failure in
	/// DisplayDensityTests once ScalingPolicyTests started using the same override - the density
	/// was correct when set and something else by the time it was read.
	///
	/// This is the second flake of exactly this shape in this suite. Process-wide state and xUnit's
	/// parallel collections do not mix, and the fix is to say so explicitly rather than to make the
	/// assertions looser.
	/// </remarks>
	[CollectionDefinition(DisplayDensityCollection.Name, DisableParallelization = true)]
	public sealed class DisplayDensityCollection
	{
		public const string Name = "Tizen display density";
	}
}
