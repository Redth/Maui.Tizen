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
}
