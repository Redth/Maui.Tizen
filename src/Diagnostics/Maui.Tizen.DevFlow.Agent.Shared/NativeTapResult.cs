namespace Maui.Tizen.DevFlow.Agent;

/// <summary>Normalizes platform activation results to DevFlow's native tap routing contract.</summary>
public static class NativeTapResult
{
    public const string Success = "ok";

    public static string FromError(string? error) => error ?? Success;
}
