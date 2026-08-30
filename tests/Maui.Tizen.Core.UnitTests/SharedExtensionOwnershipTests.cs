using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the platform extension signatures that Core owns authoritatively.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Wave B independently wrote its own <c>UpdateVisibility</c>, <c>UpdateFlowDirection</c> and
	/// <c>ToPlatformVisibility</c> before Core's landed. Duplicate extension methods on the same
	/// receiver are not always a compile error: they only produce CS0121 when both namespaces are
	/// imported into the same file. Otherwise whichever namespace happens to be in scope wins, and
	/// two copies of "how a Tizen view becomes hidden" drift apart silently - which is how the
	/// scaling divergence in TizenScalingPolicy happened.
	/// </para>
	/// <para>
	/// These tests do two things a downstream wave can rely on. They fix the exact signatures Core
	/// publishes, so a wave deleting its copy knows precisely what it is deleting; and they assert
	/// each is declared exactly once inside Core, so Core cannot grow a second copy of its own.
	/// </para>
	/// <para>
	/// This cannot detect a duplicate in a repository that does not exist yet. What it can do is
	/// make Core's ownership explicit and machine-checked, so the deletion is verifiable rather
	/// than a matter of trust.
	/// </para>
	/// </remarks>
	public class SharedExtensionOwnershipTests
	{
		static string RepositoryRoot => MSBuildEvaluation.RepositoryRoot;

		/// <summary>
		/// Extension signatures other waves are expected to consume rather than reimplement.
		/// </summary>
		public static TheoryData<string> SharedSignatures => new()
		{
			"static Microsoft.Maui.Platforms.Tizen.TizenPlatformExtensions.UpdateVisibility(this Tizen.NUI.BaseComponents.View! platformView, Microsoft.Maui.IView! view) -> void",
			"static Microsoft.Maui.Platforms.Tizen.TizenPlatformExtensions.UpdateFlowDirection(this Tizen.NUI.BaseComponents.View! platformView, Microsoft.Maui.IView! view) -> void",
			"static Microsoft.Maui.Platforms.Tizen.TizenPlatformExtensions.ToPlatformVisibility(this Microsoft.Maui.Visibility visibility) -> bool",
		};

		static string[] Baseline => File.ReadAllLines(
			Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core/PublicAPI/slice/PublicAPI.Unshipped.txt"));

		[Theory]
		[MemberData(nameof(SharedSignatures))]
		public void CoreDeclaresTheSharedSignatureExactly(string signature)
		{
			// The exact text, not just the method name. A wave deleting its copy has to match the
			// receiver type and nullability too, and an incompatible change to Core's signature
			// would otherwise look like a harmless edit here while breaking every consumer.
			Assert.Contains(signature, Baseline, StringComparer.Ordinal);
		}

		[Theory]
		[InlineData("UpdateVisibility")]
		[InlineData("UpdateFlowDirection")]
		[InlineData("ToPlatformVisibility")]
		public void CoreDeclaresEachSharedExtensionExactlyOnce(string methodName)
		{
			// Core growing its own second copy is the failure this catches. Two overloads on the
			// same receiver would compile here and become ambiguous for consumers.
			var declarations = CompiledCoreSources()
				.SelectMany(path => Regex
					.Matches(File.ReadAllText(path), $@"public static [^\s]+ {Regex.Escape(methodName)}\s*\(\s*this\s")
					.Select(_ => path))
				.ToArray();

			Assert.Single(declarations);
		}

		[Fact]
		public void SharedSignaturesAreAllPresentInTheBaseline()
		{
			// Guards the guard: if the baseline were regenerated empty, or the slice path moved,
			// the per-signature assertions above would fail one by one with a confusing message.
			// This says plainly that the baseline is populated.
			Assert.True(Baseline.Length > 100, $"The slice baseline looks truncated ({Baseline.Length} lines).");
		}

		static IEnumerable<string> CompiledCoreSources() => MSBuildEvaluation
			.GetItems("src/Maui.Tizen.Core/Maui.Tizen.Core.csproj", "Compile")
			.Where(File.Exists);
	}
}
