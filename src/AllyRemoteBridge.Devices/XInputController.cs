using System.Runtime.InteropServices;
using AllyRemoteBridge.Core;

namespace AllyRemoteBridge.Devices;

public sealed class XInputController(uint? requestedIndex) : IController
{
    private uint? selected;
    public static uint[] ConnectedSlots() => Enumerable.Range(0, 4).Select(i => (uint)i).Where(i => XInputGetState(i, out _) == 0).ToArray();
    public uint? SelectedIndex => selected;
    public void Prepare()
    {
        var slots = ConnectedSlots();
        if (requestedIndex is { } index)
        {
            if (!slots.Contains(index)) throw new InvalidOperationException($"Controller slot {index} is disconnected. Enable ASUS Gamepad mode and check settings.");
            selected = index;
        }
        else if (slots.Length == 1) selected = slots[0];
        else throw new InvalidOperationException(slots.Length == 0
            ? "No XInput controller detected. Enable Gamepad mode in Armoury Crate."
            : "Multiple controllers detected. Select the Ally controller slot in settings.");
    }
    public bool TryRead(out ControllerState state)
    {
        state = default;
        // Never silently switch to another controller when the selected slot disconnects.
        if (selected is not { } index || XInputGetState(index, out var value) != 0) return false;
        var g = value.Gamepad;
        state = new((XboxButtons)g.Buttons, g.LT, g.RT, g.LX, g.LY, g.RX, g.RY);
        return true;
    }
    public void Rumble(byte small, byte large)
    {
        if (selected is not { } index) return;
        var vibration = new Vibration { Left = (ushort)(large * 257), Right = (ushort)(small * 257) };
        XInputSetState(index, ref vibration);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Gamepad
    {
        public ushort Buttons;
        public byte LT, RT;
        public short LX, LY, RX, RY;
    }
    [StructLayout(LayoutKind.Sequential)] private struct State { public uint Packet; public Gamepad Gamepad; }
    [StructLayout(LayoutKind.Sequential)] private struct Vibration { public ushort Left, Right; }
    [DllImport("xinput1_4.dll", ExactSpelling = true)] private static extern uint XInputGetState(uint index, out State state);
    [DllImport("xinput1_4.dll", ExactSpelling = true)] private static extern uint XInputSetState(uint index, ref Vibration vibration);
}
