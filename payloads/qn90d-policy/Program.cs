using System;
using System.IO;
using System.Threading;

namespace HdmiWake;

internal static class Program
{
    private const string LogPath = "/home/owner/share/tmp/sdk_tools/hdmi-wake-policy.log";

    internal static void Log(string message)
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 65536)
                File.WriteAllText(LogPath, "");
            File.AppendAllText(LogPath, $"{DateTime.UtcNow:o} {message}\n");
        }
        catch (IOException) { } // A diagnostic failure must not disable the policy.
        catch (UnauthorizedAccessException) { }
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "run")
        {
            Console.Error.WriteLine("Usage: HdmiWakePolicy run");
            return 2;
        }
        Run();
        return 0;
    }

    internal static void Run()
    {
        var television = new NativeTelevision();
        var policy = new WakePolicy();
        Log($"started v3 settle_ms=0 poll_ms={WakePolicy.PollMilliseconds}");
        while (true)
        {
            policy.Poll(television, Log, Thread.Sleep);
            Thread.Sleep(WakePolicy.PollMilliseconds);
        }
    }
}
