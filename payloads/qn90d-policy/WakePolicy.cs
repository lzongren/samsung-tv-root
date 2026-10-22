using System;

namespace HdmiWake;

internal interface ITelevision
{
    int WakeCount { get; }
    int PowerState { get; }
    int WakeReason { get; }
    bool TryGetSource(out int source);
    int ConnectSource(int source);
}

// Source IDs and wake reasons are observed QN90D firmware values, not public API enums.
internal sealed class WakePolicy
{
    internal const int Hdmi1 = 13;
    internal const int PollMilliseconds = 50;
    private int lastCount = -1;
    private int lastState = -1;

    internal static bool IsNormalWake(int reason) => reason is 0 or 1 or 32 or 68;

    internal void Poll(ITelevision television, Action<string> log, Action<int> sleep)
    {
        int count = television.WakeCount;
        int state = television.PowerState;
        if (count >= 0 && state == 0 && (count != lastCount || lastState != 0))
        {
            int reason = television.WakeReason;
            if (!IsNormalWake(reason))
            {
                // CEC (91) may come from any HDMI device. Let the TV choose its source.
                log($"wake count={count} reason={reason} preserving CEC/other source");
            }
            else if (television.PowerState != 0 || television.WakeCount != count ||
                     television.WakeReason != reason)
            {
                log("wake changed; skipped");
            }
            else if (!television.TryGetSource(out int source))
            {
                log("source unreadable; skipped");
            }
            else if (source != Hdmi1)
            {
                // Keep logging off the critical path. Do not add a pre-switch delay.
                int result = television.ConnectSource(Hdmi1);
                sleep(1500);
                string actual = television.TryGetSource(out source) ? source.ToString() : "unknown";
                log($"wake count={count} reason={reason} select HDMI1 result={result} actual={actual}");
            }
            else
            {
                log("HDMI1 already selected");
            }
        }
        lastCount = count;
        lastState = state;
    }
}
