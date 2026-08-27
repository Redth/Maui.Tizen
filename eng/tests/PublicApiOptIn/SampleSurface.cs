using System;

namespace Maui.Tizen.PublicApiOptIn;

/// <summary>
/// A deliberately tiny public surface that exists so the PublicAPI analyzer wiring is
/// proven by a real compilation rather than asserted about in prose.
/// </summary>
public class SampleSurface
{
	/// <summary>Returns a fixed description.</summary>
	public string Describe() => "sample";
}
