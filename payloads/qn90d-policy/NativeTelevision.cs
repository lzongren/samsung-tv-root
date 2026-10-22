using System.Runtime.InteropServices;

namespace HdmiWake;

internal sealed class NativeTelevision : ITelevision
{
    [DllImport("libdeviced.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern int device_power_get_wakeup_reason();
    [DllImport("libdeviced.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern int device_power_get_wakeup_count();
    [DllImport("libdeviced.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern int device_power_get_state();
    [DllImport("libsource-api.so", CallingConvention = CallingConvention.Cdecl)]
    private static extern int connect_source(int source);
    [DllImport("libvconf.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int vconf_get_int(string key, out int value);

    public int WakeReason => device_power_get_wakeup_reason();
    public int WakeCount => device_power_get_wakeup_count();
    public int PowerState => device_power_get_state();
    public int ConnectSource(int source) => connect_source(source);
    public bool TryGetSource(out int source) =>
        vconf_get_int("memory/system/source_type", out source) == 0;
}
