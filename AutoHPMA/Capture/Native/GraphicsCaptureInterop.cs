using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace AutoHPMA.Capture.Native;

/// <summary>
/// 将 HWND 提升为 <see cref="GraphicsCaptureItem"/> 所需的 COM 互操作。
/// </summary>
internal static class GraphicsCaptureInterop
{
    private static readonly Guid IID_GraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    public static GraphicsCaptureItem CreateForWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) throw new ArgumentException("窗口句柄无效。", nameof(hWnd));

        const string runtimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClassName, runtimeClassName.Length, out var classId));

        IntPtr factoryPtr = IntPtr.Zero;
        try
        {
            var iid = IID_IGraphicsCaptureItemInterop;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId, ref iid, out factoryPtr));

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetTypedObjectForIUnknown(
                factoryPtr, typeof(IGraphicsCaptureItemInterop));

            var itemIid = IID_GraphicsCaptureItem;
            var itemPtr = interop.CreateForWindow(hWnd, ref itemIid);
            if (itemPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法为该窗口创建 GraphicsCaptureItem (窗口可能不支持捕获)。");
            }

            try
            {
                return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            WindowsDeleteString(classId);
        }
    }
}
