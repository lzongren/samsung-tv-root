using System.Threading;
using Tizen.Applications;

namespace HdmiWake;

internal sealed class ServiceHost : ServiceApplication
{
    protected override void OnCreate()
    {
        base.OnCreate();
        Bootstrap.Trace("OnCreate");
        new Thread(Program.Run) { IsBackground = true, Name = "HdmiWakePolicy" }.Start();
    }

    internal static void Launch(string[] args)
    {
        using var app = new ServiceHost();
        app.Run(args);
    }
}
