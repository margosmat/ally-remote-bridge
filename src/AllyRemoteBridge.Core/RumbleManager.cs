namespace AllyRemoteBridge.Core;

public sealed class RumbleManager(bool enabled, IController controller, Action<string> log)
{
    private readonly object gate = new();
    private bool active, motorsRunning;
    public void SetActive(bool value)
    {
        lock (gate)
        {
            active = enabled && value;
            if (!active && motorsRunning) Send(0, 0);
        }
    }
    public void Forward(byte small, byte large)
    {
        lock (gate) { if (active) Send(small, large); }
    }
    private void Send(byte small, byte large)
    {
        try { controller.Rumble(small, large); motorsRunning = small != 0 || large != 0; }
        catch (Exception e) { log($"Rumble unavailable: {e.GetType().Name}"); }
    }
}
