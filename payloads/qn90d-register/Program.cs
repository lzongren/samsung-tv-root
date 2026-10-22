using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HdmiWake;

// Private ARM32 parser ABI, restricted to the exact libraries inspected on QN90D.
// Never relax these guards to make an unsupported firmware install succeed.
internal static class Program
{
    private const string Manifest = "/opt/usr/apps/org.local.hdmiwake/tizen-manifest.xml";
    private const string PackageId = "org.local.hdmiwake";
    private const string AppId = "org.local.hdmiwake.service";
    private const int PackageNameOffset = 0x04;
    private const int PackageSwitchModeOffset = 0x88;
    private const int ApplicationsOffset = 0xac;
    private const int AppSwitchModeOffset = 0xc8;

    [DllImport("libpkgmgr_parser.so.0")]
    private static extern IntPtr pkgmgr_parser_process_manifest_xml(string path);
    [DllImport("libpkgmgr_parser.so.0")]
    private static extern int pkgmgr_parser_free_manifest_xml(IntPtr manifest);
    [DllImport("libpkgmgr_parser.so.0")]
    private static extern int pkgmgr_parser_process_manifest_x_for_installation(IntPtr manifest);
    [DllImport("libpkgmgr_parser.so.0")]
    private static extern int pkgmgr_parser_process_manifest_x_for_upgrade(IntPtr manifest);
    [DllImport("libpkgmgr_parser.so.0")]
    private static extern int pkgmgr_parser_parse_manifest_for_uninstallation(string path, IntPtr tags);
    [DllImport("libc.so.6")]
    private static extern IntPtr strdup(string value);

    private static void CheckLibrary(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        string actual = Convert.ToHexString(sha.ComputeHash(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported library: {path}");
    }

    private static void CheckFirmware()
    {
        if (IntPtr.Size != 4 || RuntimeInformation.ProcessArchitecture != Architecture.Arm)
            throw new PlatformNotSupportedException("This helper requires the inspected ARM32 TV firmware.");
        CheckLibrary("/usr/lib/libpkgmgr_parser.so.0", "e0802597c5926218a9ee3245078029ae3a93ca0ebbd5633ade2e27045361cb64");
        CheckLibrary("/usr/lib/libpkgmgr-info.so.0", "04d869c5c950056e85f0d79740ccd21e97a5bd526c05ff53004b52734e64bf93");
    }

    private static void SetDefault(IntPtr structure, int offset)
    {
        IntPtr value = strdup("default");
        if (value == IntPtr.Zero)
            throw new OutOfMemoryException();
        // Ownership passes to the parsed manifest and its native destructor.
        Marshal.WriteIntPtr(structure, offset, value);
    }

    private static void FillMissingDefaults(IntPtr package)
    {
        if (Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(package, PackageNameOffset)) != PackageId)
            throw new InvalidOperationException("Unexpected package ID.");
        IntPtr list = Marshal.ReadIntPtr(package, ApplicationsOffset);
        // ARM32 GList: data, next, previous. This manifest must contain one app.
        if (list == IntPtr.Zero || Marshal.ReadIntPtr(list, 4) != IntPtr.Zero)
            throw new InvalidOperationException("Expected exactly one application.");
        IntPtr app = Marshal.ReadIntPtr(list);
        if (app == IntPtr.Zero || Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(app)) != AppId)
            throw new InvalidOperationException("Unexpected application ID.");
        if (Marshal.ReadIntPtr(package, PackageSwitchModeOffset) != IntPtr.Zero ||
            Marshal.ReadIntPtr(app, AppSwitchModeOffset) != IntPtr.Zero)
            throw new InvalidOperationException("Unexpected parser defaults; refusing to overwrite.");
        // The firmware parser leaves fields NULL although the package DB requires values.
        SetDefault(package, PackageSwitchModeOffset);
        SetDefault(app, AppSwitchModeOffset);
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("install" or "upgrade" or "remove"))
        {
            Console.Error.WriteLine("Usage: ManifestTool install|upgrade|remove");
            return 2;
        }
        try
        {
            CheckFirmware();
            // Validate identity before any native registration or removal operation.
            var xml = System.Xml.Linq.XDocument.Load(Manifest);
            var root = xml.Root ?? throw new InvalidOperationException("Missing manifest root.");
            var apps = root.Elements(root.Name.Namespace + "service-application");
            if ((string)root.Attribute("package") != PackageId ||
                System.Linq.Enumerable.Count(apps) != 1 ||
                (string)System.Linq.Enumerable.Single(apps).Attribute("appid") != AppId)
                throw new InvalidOperationException("Manifest identity mismatch.");
            if (args[0] == "remove")
                return Report(pkgmgr_parser_parse_manifest_for_uninstallation(Manifest, IntPtr.Zero));
            IntPtr parsed = pkgmgr_parser_process_manifest_xml(Manifest);
            if (parsed == IntPtr.Zero)
                throw new InvalidOperationException("Manifest parse failed.");
            try
            {
                FillMissingDefaults(parsed);
                return Report(args[0] == "install"
                    ? pkgmgr_parser_process_manifest_x_for_installation(parsed)
                    : pkgmgr_parser_process_manifest_x_for_upgrade(parsed));
            }
            finally { pkgmgr_parser_free_manifest_xml(parsed); }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Report(int result)
    {
        Console.WriteLine($"manifest_result={result}");
        return result == 0 ? 0 : 1;
    }
}
