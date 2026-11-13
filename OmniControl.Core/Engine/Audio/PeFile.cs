using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>
/// Reads a Windows binary's headers straight from the file, never loading it (ported from TabForge's <c>PeImage</c> and
/// <c>PeExports</c>, same owner): the machine it is built for and the names it exports. Bounded: small reads, capped counts.
/// </summary>
internal static class PeFile
{
    public const ushort MachineX64 = 0x8664, MachineX86 = 0x014C;

    private const ushort Pe32 = 0x10B, Pe32Plus = 0x20B;
    private const int MaxSections = 96, MaxExports = 4096, MaxNameBytes = 64;

    /// <summary>The machine type, 0 when the file is not a Windows binary, null when it can't be read now.</summary>
    public static ushort? Machine(string file)
    {
        try
        {
            using var reader = Open(file);
            return Header(reader) is { } header ? header.Machine : (ushort)0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The names the binary exports (empty for a file that is not a readable Windows binary).</summary>
    public static IReadOnlySet<string> Exports(string file)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var reader = Open(file);
            if (Header(reader) is not { DataDirectories: > 0 } header) return names;
            var stream = reader.BaseStream;
            stream.Position = header.DataDirectories; // the first directory is the export table
            var exportRva = reader.ReadUInt32();
            if (exportRva == 0 || reader.ReadUInt32() == 0) return names;
            stream.Position = header.Offset(exportRva) + 24;
            var count = Math.Min(reader.ReadUInt32(), MaxExports);
            stream.Position += 4; // the address table
            stream.Position = header.Offset(reader.ReadUInt32());
            var offsets = new uint[count];
            for (var i = 0; i < count; i++) offsets[i] = reader.ReadUInt32();
            var buffer = new byte[MaxNameBytes];
            foreach (var rva in offsets)
            {
                stream.Position = header.Offset(rva);
                var read = stream.Read(buffer, 0, buffer.Length);
                var end = Array.IndexOf(buffer, (byte)0, 0, read);
                if (end > 0) names.Add(Encoding.ASCII.GetString(buffer, 0, end));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            // Not a well-formed binary, or unreadable: it exports nothing OmniControl could use (expected for most DLLs in a folder).
        }
        return names;
    }

    private static BinaryReader Open(string file) =>
        new(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

    private sealed record Image(ushort Machine, long DataDirectories, IReadOnlyList<(uint Va, uint Size, uint Raw)> Sections)
    {
        /// <summary>The file offset of a relative address; throws when no section holds it.</summary>
        public long Offset(uint rva)
        {
            foreach (var (va, size, raw) in Sections)
                if (rva >= va && rva < (long)va + size) return raw + (long)(rva - va);
            throw new InvalidDataException("Address outside the sections.");
        }
    }

    /// <summary>The headers, or null when the file is not a PE image of a known kind.</summary>
    private static Image? Header(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) return null; // "MZ"
        stream.Position = 0x3C;
        var pe = reader.ReadInt32();
        if (pe <= 0 || pe > stream.Length - 24) return null;
        stream.Position = pe;
        if (reader.ReadUInt32() != 0x00004550) return null; // "PE\0\0"
        var machine = reader.ReadUInt16();
        var sections = reader.ReadUInt16();
        stream.Position += 12;
        var optionalSize = reader.ReadUInt16();
        stream.Position += 2;
        var optional = stream.Position;
        if (optionalSize == 0) return new Image(machine, 0, Array.Empty<(uint, uint, uint)>());
        var magic = reader.ReadUInt16();
        if (magic is not (Pe32 or Pe32Plus)) return new Image(machine, 0, Array.Empty<(uint, uint, uint)>());
        var directories = optional + (magic == Pe32Plus ? 112 : 96);
        stream.Position = optional + optionalSize;
        var table = new List<(uint, uint, uint)>();
        for (var i = 0; i < Math.Min((int)sections, MaxSections); i++)
        {
            stream.Position += 8; // name
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            stream.Position += 16;
            table.Add((virtualAddress, Math.Max(virtualSize, rawSize), rawPointer));
        }
        return new Image(machine, directories, table);
    }
}
