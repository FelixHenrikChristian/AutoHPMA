using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace AutoHPMA.Capture.Native;

/// <summary>
/// Direct3D11 与 WinRT <see cref="IDirect3DDevice"/>/<see cref="IDirect3DSurface"/> 的互操作封装。
/// </summary>
internal static class Direct3D11Interop
{
    private static readonly Guid IID_ID3D11Device = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>使用硬件加速创建 D3D11 设备并将其包装为 WinRT <see cref="IDirect3DDevice"/>。</summary>
    public static (SharpDX.Direct3D11.Device sharpDxDevice, IDirect3DDevice winRtDevice) CreateDevice()
    {
        var d3dDevice = new SharpDX.Direct3D11.Device(
            SharpDX.Direct3D.DriverType.Hardware,
            SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);

        using var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device3>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pUnknown);
        if (hr != 0 || pUnknown == IntPtr.Zero)
        {
            d3dDevice.Dispose();
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed (HRESULT 0x{hr:X8}).");
        }

        try
        {
            var winrt = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
            return (d3dDevice, winrt);
        }
        finally
        {
            Marshal.Release(pUnknown);
        }
    }

    /// <summary>从 WinRT 表面解出 SharpDX 纹理，调用方负责释放纹理。</summary>
    public static SharpDX.Direct3D11.Texture2D ToSharpDxTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var texturePtr = access.GetInterface(IID_ID3D11Texture2D);
        return new SharpDX.Direct3D11.Texture2D(texturePtr);
    }
}
