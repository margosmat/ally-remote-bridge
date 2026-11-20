using System.Text;

namespace AllyRemoteBridge.Devices;

internal static class UsbIpProtocol
{
    internal const int AttachSize = 1120, ImportedSize = 1132, QueryHeaderSize = 1136;
    internal static byte[] Attach(string bus, string serial)
    {
        byte[] request = new byte[AttachSize];
        BitConverter.GetBytes(request.Length).CopyTo(request, 0);
        Write(request, 12, 32, bus); Write(request, 44, 32, "32451"); Write(request, 76, 1025, "127.0.0.1");
        Write(request, 1101, 16, serial);
        return request;
    }
    internal static IReadOnlyList<int> OwnedPorts(byte[] query, uint returned, string bus, string serial, int? expectedPort)
    {
        if (returned > query.Length || returned < 4 || (returned - 4) % ImportedSize != 0)
            throw new InvalidDataException("Unsupported USB/IP device-list ABI; no devices removed.");
        List<int> ports = [];
        for (int offset = 4; offset + ImportedSize <= returned; offset += ImportedSize)
        {
            int port = BitConverter.ToInt32(query, offset);
            string b = Read(query, offset + 8, 32), service = Read(query, offset + 40, 32), host = Read(query, offset + 72, 1025);
            string s = Read(query, offset + 1097, 16);
            if (b != bus || service != "32451" || host != "127.0.0.1" || s != serial || (expectedPort is { } p && p != port)) continue;
            if (port <= 0) throw new InvalidDataException("Invalid owned USB/IP port.");
            ports.Add(port);
        }
        return ports;
    }
    internal static byte[] Detach(int port)
    {
        if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port), "Detach-all is prohibited.");
        byte[] request = new byte[8];
        BitConverter.GetBytes(8).CopyTo(request, 0); BitConverter.GetBytes(port).CopyTo(request, 4);
        return request;
    }
    private static void Write(byte[] buffer, int offset, int size, string value)
    {
        if (value.Any(c => c > 127)) throw new ArgumentException("USB/IP identifiers must be ASCII.");
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length >= size) throw new ArgumentException("USB/IP field exceeds buffer.");
        bytes.CopyTo(buffer, offset);
    }
    private static string Read(byte[] buffer, int offset, int size)
    {
        int length = Array.IndexOf(buffer, (byte)0, offset, size);
        if (length < 0) throw new InvalidDataException("Unterminated USB/IP field.");
        return Encoding.ASCII.GetString(buffer, offset, length - offset);
    }
}
