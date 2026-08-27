namespace Maui.Tizen.DevFlow.Agent;

/// <summary>What an activation request can do with a given element.</summary>
public enum NativeActivationOutcome
{
    /// <summary>No element was supplied.</summary>
    NoElement,

    /// <summary>The element exposes a real activation path and can be invoked.</summary>
    Activate,

    /// <summary>The element cannot be activated. It must NOT be reported as tapped.</summary>
    NotActivatable,
}

/// <param name="Reason">Explanation for a non-activatable element; null when activation is possible.</param>
public sealed record NativeActivationDecision(NativeActivationOutcome Outcome, string? Reason)
{
    public bool CanActivate => Outcome == NativeActivationOutcome.Activate;
}

/// <summary>
/// Decides whether an element can genuinely be activated.
/// </summary>
/// <remarks>
/// <para>
/// Kept separate from the Tizen-only input code, and free of Tizen types, so the decision can be
/// tested on a hosted runner. The behaviour it encodes is a correctness rule, not a detail:
/// </para>
/// <para>
/// <b>Focusing is not activation.</b> An earlier version fell back to moving focus and reported
/// success. A driver would see "tap succeeded" while the control's command never ran, and the
/// resulting assertion failure would point at the control's logic instead of at the tap that never
/// happened. A false success is worse than an honest 501, because it sends the investigation to the
/// wrong place.
/// </para>
/// </remarks>
public static class NativeActivationPolicy
{
    /// <param name="hasElement">Whether an element was supplied at all.</param>
    /// <param name="isButton">Whether it exposes a managed activation path, i.e. <c>IButton</c>.</param>
    /// <param name="isFocusable">Whether it can take focus. Never sufficient for activation.</param>
    /// <param name="typeName">Type name used in the explanation.</param>
    /// <param name="syntheticInputAvailable">
    /// Whether synthesised input could tap it for real; changes the advice, never the verdict.
    /// </param>
    public static NativeActivationDecision Decide(
        bool hasElement,
        bool isButton,
        bool isFocusable,
        string typeName,
        bool syntheticInputAvailable)
    {
        if (!hasElement)
            return new NativeActivationDecision(NativeActivationOutcome.NoElement, "No element was supplied.");

        if (isButton)
            return new NativeActivationDecision(NativeActivationOutcome.Activate, null);

        var focusNote = isFocusable
            ? " (it is focusable, but focusing is not activation)"
            : string.Empty;

        var advice = syntheticInputAvailable
            ? "Tap it by coordinate instead, which exercises real hit-testing."
            : $"Grant '{TizenPrivileges.InputGenerator}' so it can be tapped for real, or register " +
              "the MAUI element instead of the platform view.";

        return new NativeActivationDecision(
            NativeActivationOutcome.NotActivatable,
            $"'{typeName}' has no managed activation path{focusNote}. {advice}");
    }
}
