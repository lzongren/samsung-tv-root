using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;

namespace HdmiWake;

internal static class Bootstrap
{
    private static void Main(string[] args)
    {
        using var instance = new Mutex(false, "org.local.hdmiwake.service");
        bool acquired;
        try { acquired = instance.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
            return;

        try
        {
            Trace($"entry {Environment.ProcessId} boot={File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()}");
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                string path = Path.Combine("/usr/share/dotnet.tizen/framework", name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            };
            Start(args);
        }
        catch (Exception error)
        {
            Trace(error.ToString());
            throw;
        }
        finally { instance.ReleaseMutex(); }
    }

    internal static void Trace(string message)
    {
        try { File.AppendAllText("/tmp/hdmiwake-bootstrap.log", $"{DateTime.UtcNow:o} {message}\n"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Register the resolver before the JIT resolves ServiceApplication dependencies.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start(string[] args) => ServiceHost.Launch(args);
}
