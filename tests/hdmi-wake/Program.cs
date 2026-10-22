using System;
using System.Collections.Generic;
using HdmiWake;

// No TV, native libraries, or test framework packages required.
internal sealed class FakeTelevision : ITelevision
{
    public int WakeCount { get; set; } = 1;
    public int PowerState { get; set; }
    private int wakeReason = 68;
    internal readonly Queue<int> Reasons = new();
    public int WakeReason
    {
        get => Reasons.Count > 0 ? Reasons.Dequeue() : wakeReason;
        set => wakeReason = value;
    }
    internal int Source = 16;
    internal bool Readable = true;
    internal readonly List<int> Connections = new();
    internal Action OnRead;
    public bool TryGetSource(out int source)
    {
        source = Source;
        OnRead?.Invoke();
        return Readable;
    }
    public int ConnectSource(int source) { Connections.Add(source); Source = source; return 1; }
}

internal static class Tests
{
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
    private static void Poll(WakePolicy policy, FakeTelevision tv) =>
        policy.Poll(tv, _ => { }, _ => { });

    private static void Main()
    {
        foreach (int reason in new[] { 0, 1, 32, 68 })
        {
            var tv = new FakeTelevision { WakeReason = reason };
            var policy = new WakePolicy();
            var order = new List<string>();
            tv.OnRead = () => order.Add("read");
            policy.Poll(tv, _ => order.Add("log"), ms =>
            {
                Check(tv.Connections.Count == 1, "No delay before source selection");
                order.Add("sleep");
            });
            Check(tv.Connections.Count == 1 && tv.Source == 13, "Normal wake selects HDMI1");
            Check(order[0] == "read" && order[1] == "sleep", "No log before selection");
            Poll(policy, tv);
            Check(tv.Connections.Count == 1, "No repeated switch while awake");
            tv.Source = 16;
            tv.WakeCount++;
            Poll(policy, tv);
            Check(tv.Connections.Count == 2, "New wake count triggers selection");
        }
        foreach (int reason in new[] { -1, 5, 50, 52, 83, 91, 125, 127 })
        {
            var tv = new FakeTelevision { WakeReason = reason };
            Poll(new WakePolicy(), tv);
            Check(tv.Connections.Count == 0 && tv.Source == 16, "Preserve CEC/unknown source");
        }
        foreach (var tv in new[] {
            new FakeTelevision { PowerState = 2 },
            new FakeTelevision { WakeCount = -1 },
            new FakeTelevision { Readable = false },
            new FakeTelevision { Source = 13 } })
        {
            Poll(new WakePolicy(), tv);
            Check(tv.Connections.Count == 0, "Skip off/unreadable/already selected state");
        }
        var resumed = new FakeTelevision { PowerState = 2 };
        var resumedPolicy = new WakePolicy();
        Poll(resumedPolicy, resumed);
        resumed.PowerState = 0;
        Poll(resumedPolicy, resumed);
        Check(resumed.Connections.Count == 1, "Power transition works without count change");
        var changing = new FakeTelevision();
        changing.Reasons.Enqueue(68);
        changing.Reasons.Enqueue(91);
        Poll(new WakePolicy(), changing);
        Check(changing.Connections.Count == 0, "Skip when wake reason changes before selection");
        Console.WriteLine("HDMI wake policy tests passed.");
    }
}
