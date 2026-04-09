using System.Runtime.InteropServices;

namespace AutoHPMA.Helpers;

/// <summary>
/// 通过 SetThreadExecutionState 在应用运行期间推迟显示器关闭与因空闲导致的系统睡眠。
/// </summary>
internal static class PowerSaveHelper
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    private const uint PreventFlags = EsContinuous | EsSystemRequired | EsDisplayRequired;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public static void SetPreventSleepWhileRunning(bool enabled)
    {
        if (enabled)
            SetThreadExecutionState(PreventFlags);
        else
            SetThreadExecutionState(EsContinuous);
    }
}
