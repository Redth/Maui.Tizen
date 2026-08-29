namespace Maui.Tizen.DevFlow.Agent;

/// <summary>Starts or rebinds an agent when the platform application lifecycle becomes active.</summary>
public sealed class AgentLifecycleStartup<TApplication>(
    Func<TApplication?> resolveApplication,
    Func<bool> isRunning,
    Func<bool> isApplicationBound,
    Action<TApplication> start,
    Action<TApplication> bind)
    where TApplication : class
{
    public bool OnApplicationActive()
    {
        var application = resolveApplication();
        if (application is null)
            return false;

        if (!isRunning())
            start(application);
        else if (!isApplicationBound())
            bind(application);

        return true;
    }
}
