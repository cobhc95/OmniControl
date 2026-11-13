using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>
/// A VST2 plug-in's saved settings as OmniControl keeps them: its program chunk (the plug-in's own bytes, for plug-ins that have
/// chunks), or else every parameter's name and value. Both are what Equalizer APO's <c>VSTPlugin:</c> line can carry (<c>ChunkData</c>,
/// or name and value pairs), so the APO writer reads the same settings OmniControl's own engine restores.
/// </summary>
/// <param name="Chunk">The program chunk, or null for a plug-in without chunks.</param>
/// <param name="Values">Each parameter's name and normalised value, in the plug-in's order (empty for a chunk).</param>
public sealed record Vst2Settings(byte[]? Chunk, IReadOnlyList<(string Name, float Value)> Values)
{
    private static readonly byte[] ChunkMark = "OCV2CHK1"u8.ToArray(), ValuesMark = "OCV2PRM1"u8.ToArray();

    /// <summary>More parameters than this are not read back (a damaged file must not make OmniControl allocate without end).</summary>
    private const int MaxValues = 65536;

    internal byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (Chunk is not null)
        {
            writer.Write(ChunkMark);
            writer.Write(Chunk);
        }
        else
        {
            writer.Write(ValuesMark);
            writer.Write(Values.Count);
            foreach (var (name, value) in Values)
            {
                var bytes = Encoding.UTF8.GetBytes(name);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                writer.Write(value);
            }
        }
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>The settings in <paramref name="state"/>, or null when it is not a VST2 state OmniControl wrote (or is damaged).</summary>
    public static Vst2Settings? Read(byte[] state)
    {
        if (state.AsSpan().StartsWith(ChunkMark)) return new Vst2Settings(state[ChunkMark.Length..], Array.Empty<(string, float)>());
        if (!state.AsSpan().StartsWith(ValuesMark)) return null;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(state, ValuesMark.Length, state.Length - ValuesMark.Length));
            var count = reader.ReadInt32();
            if (count is < 0 or > MaxValues) return null;
            var values = new List<(string, float)>(count);
            for (var i = 0; i < count; i++)
            {
                var length = reader.ReadInt32();
                if (length is < 0 or > Vst2Abi.TextBytes) return null;
                var name = Encoding.UTF8.GetString(reader.ReadBytes(length));
                values.Add((name, reader.ReadSingle()));
            }
            return new Vst2Settings(null, values);
        }
        catch (EndOfStreamException)
        {
            return null; // cut short: treated as no saved settings
        }
    }
}
