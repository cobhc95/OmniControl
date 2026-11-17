using System.Runtime.InteropServices;
using OmniControl.Core.Native;
using static OmniControl.Core.Native.MediaFoundationNative;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// The MP4's video samples, reused instead of made anew for every frame (about 33 MB each at 4K): Media Foundation's video sample
/// allocator hands out a sample with its buffer, the Sink Writer keeps it while the encoder needs it, and its last release puts it back
/// in the pool, so nothing has to guess when the encoder is done. With a DXGI device manager the buffers are textures on the graphics
/// card; without one they are in system memory. Released on <see cref="Dispose"/> (samples still held go when their holder lets go).
/// </summary>
internal sealed class SamplePool : IDisposable
{
    /// <summary>Samples made at the start, and the most there may be: enough for the frames an encoder keeps in flight, no more memory than that.</summary>
    private const uint Initial = 2, Maximum = 8;

    /// <summary>Texture use the encoder's colour converter needs (read as a shader input or drawn to).</summary>
    private const uint TextureBinding = D3D11Native.BindShaderResource | D3D11Native.BindRenderTarget;

    private IntPtr _allocator;

    private SamplePool(IntPtr allocator) => _allocator = allocator;

    /// <summary>
    /// A pool of samples of the video <paramref name="type"/> (frame size and colour format), on the graphics card of
    /// <paramref name="manager"/> or in system memory when it is zero. Throws <see cref="COMException"/> when Windows refuses.
    /// </summary>
    public static SamplePool Create(IntPtr type, IntPtr manager)
    {
        var iid = IidSampleAllocatorEx;
        Check(MFCreateVideoSampleAllocatorEx(ref iid, out var allocator));
        IntPtr attributes = IntPtr.Zero;
        try
        {
            if (manager != IntPtr.Zero)
            {
                SetDirectXManager(allocator, manager);
                Check(MFCreateAttributes(out attributes, 1));
                SetUInt32(attributes, SampleBindFlags, TextureBinding);
            }
            InitializeSampleAllocatorEx(allocator, Initial, Maximum, attributes, type);
            return new SamplePool(allocator);
        }
        catch
        {
            Release(allocator);
            throw;
        }
        finally
        {
            Release(attributes);
        }
    }

    /// <summary>A free sample with one buffer (the caller releases it after handing it on), or zero while the encoder holds them all.</summary>
    public IntPtr Take()
    {
        var hr = AllocateSample(_allocator, out var sample);
        if (hr == SampleAllocatorEmpty) return IntPtr.Zero;
        Check(hr);
        return sample;
    }

    public void Dispose()
    {
        if (_allocator == IntPtr.Zero) return;
        UninitializeSampleAllocator(_allocator); // its result does not matter: the release below frees the pool either way
        Release(_allocator);
        _allocator = IntPtr.Zero;
    }
}
