namespace AllyRemoteBridge.Core;

public sealed class TouchManager
{
    private readonly object gate = new();
    private readonly uint?[] ids = new uint?[2];
    private readonly TouchContact[] contacts = new TouchContact[2];
    public void Update(uint id, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(x) || !double.IsFinite(y)) return;
        lock (gate)
        {
            int slot = Array.IndexOf(ids, (uint?)id);
            if (slot < 0) slot = Array.IndexOf(ids, null);
            if (slot < 0) return;
            ids[slot] = id;
            contacts[slot] = new((ushort)Math.Clamp(Math.Round(x / width * 1920), 0, 1920),
                (ushort)Math.Clamp(Math.Round(y / height * 942), 0, 942), true);
        }
    }
    public void Release(uint id)
    {
        lock (gate) { int slot = Array.IndexOf(ids, (uint?)id); if (slot >= 0) { ids[slot] = null; contacts[slot] = default; } }
    }
    public void Reset() { lock (gate) { Array.Clear(ids); Array.Clear(contacts); } }
    public TouchState Read() { lock (gate) return new(contacts[0], contacts[1]); }
}
