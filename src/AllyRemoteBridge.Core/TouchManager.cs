namespace AllyRemoteBridge.Core;

public sealed class TouchManager
{
    private readonly object gate = new();
    private readonly uint?[] ids = new uint?[2];
    private readonly TouchContact[] contacts = new TouchContact[2];
    private readonly bool[] seen = new bool[2], released = new bool[2];
    public void Update(uint id, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(x) || !double.IsFinite(y)) return;
        lock (gate)
        {
            int slot = Array.IndexOf(ids, (uint?)id);
            if (slot < 0) slot = Array.IndexOf(ids, null);
            if (slot < 0) return;
            if (ids[slot] is null) seen[slot] = released[slot] = false;
            ids[slot] = id;
            contacts[slot] = new((ushort)Math.Clamp(Math.Round(x / width * 1920), 0, 1920),
                (ushort)Math.Clamp(Math.Round(y / height * 942), 0, 942), true);
        }
    }
    public void Release(uint id, bool canceled = false)
    {
        lock (gate)
        {
            int slot = Array.IndexOf(ids, (uint?)id);
            if (slot < 0) return;
            // Preserve a tap that starts and ends between two controller polls.
            if (canceled || seen[slot]) Clear(slot);
            else released[slot] = true;
        }
    }
    private void Clear(int slot)
    {
        ids[slot] = null;
        contacts[slot] = default;
        seen[slot] = released[slot] = false;
    }
    public void Reset() { lock (gate) { Clear(0); Clear(1); } }
    public TouchState Read()
    {
        lock (gate)
        {
            var state = new TouchState(contacts[0], contacts[1]);
            for (int slot = 0; slot < contacts.Length; slot++)
            {
                if (released[slot]) Clear(slot);
                else seen[slot] = contacts[slot].Active;
            }
            return state;
        }
    }
}
