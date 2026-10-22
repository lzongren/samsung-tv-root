# QN90D local HDMI wake service (experimental)

This service selects Chromecast on HDMI1 after an ordinary remote wake and lets
Samsung's HDMI-CEC routing select the device that woke the TV (PS on HDMI4 in the
verified setup). It runs on the TV; a computer is needed for installation and
maintenance, not for everyday wake handling.

## Compatibility and verification

Observed device: QN50QN90DAFXZA, Tizen 9, ARMv7,
firmware `T-PTMDAKUC-0090-2130.0`, .NET runtime 6.0.9.
This is a firmware-specific experiment, not general QN90D support or a signed TPK.

- The installed v2 prototype survived a full reboot: the boot ID changed, the app
  started without a computer launching it, and HDMI4 changed to HDMI1.
- Both normal-remote and PS-controller wake paths were visually verified.
- The installed v3 prototype removed the 250 ms pre-switch delay and reduced
  polling from 200 to 50 ms. The owner reported no problem in the normal-wake test.
- The source in this PR reorganizes that v3 behavior for review and testing. It
  has **not** been redeployed to the TV; a fresh full-reboot test of this refactor
  remains necessary. The existing working installation was left alone.

This is a post-wake policy. It does not guarantee that the previous input will
never flash. Early/pre/wake/post event subscriptions returned handles in a
separate experiment but delivered no events during the test, so they are not
used here. CEC reason 91 is not unique to PlayStation: the service deliberately
preserves the TV's CEC selection instead of forcing every CEC wake to HDMI4.

## Source layout

- `payloads/qn90d-policy`: policy, native API adapter, bounded diagnostic log.
- `payloads/qn90d-service`: Tizen service lifecycle, dependency resolver and
  single-instance mutex. App ID `org.local.hdmiwake.service`;
  package ID `org.local.hdmiwake`.
- `payloads/qn90d-register`: narrow native manifest registration helper. It checks
  ARM32 architecture, exact parser-library SHA-256 hashes and manifest identity
  before applying the two missing parser defaults or registering the app.
- `tests/hdmi-wake`: host-only policy tests with a fake TV.

Wake reasons 0/1/32/68 select source 13 (HDMI1). Unknown reasons and CEC are left
alone. A readable source and stable power/count/reason are required before a
switch. The policy handles each observed wake once; it does not repeatedly fight
manual input changes or retry unsuccessful source requests. Source verification
occurs 1.5 seconds after a switch. App automatic crash restart is disabled.

## Build and host tests

Use the .NET 6 SDK (matching the TV's runtime) and Python 3.12+ for repository tests.

```sh
dotnet run --project tests/hdmi-wake/HdmiWake.Tests.csproj -c Release
dotnet build payloads/qn90d-policy/HdmiWakePolicy.csproj -c Release -o payloads/qn90d-policy/out
dotnet build payloads/qn90d-register/ManifestTool.csproj -c Release -o payloads/qn90d-register/out
```

The Tizen lifecycle assembly must be built against the target TV's
`Tizen.dll`, `Tizen.Applications.Common.dll`, and `Tizen.Applications.Service.dll`,
from `/usr/share/dotnet.tizen/framework`. Obtain these from your own device into
an external directory (or ignored `payloads/qn90d-service/refs`). They are not
redistributed in this repository or copied into the service output.

```sh
dotnet build payloads/qn90d-service/HdmiWakeService.csproj -c Release \
  -p:TizenFrameworkPath=/absolute/path/to/tizen-framework \
  -o payloads/qn90d-service/out
```

CI tests the policy and builds the policy/registration helper. The service build
requires those private reference assemblies and is verified locally, not in CI.
.NET 6 is retained for target compatibility, not as a recommendation for new host
applications.

## Installation on the inspected firmware

Installation requires an owner-authorized root session and access to the real TV
account database. The archive-root helper uses a private mount namespace; its
temporary passwd bind must be removed **inside that namespace** before invoking
package APIs. Do not alter the actual `/etc/passwd` or disable signature checks.

There is intentionally no unattended installer. Preserve a consistent SQLite
backup of `/opt/dbspace/.pkgmgr_parser.db` before registration. Keep any existing
package files and security-registration details for rollback. Do not restore the
entire database over later unrelated application changes.

1. Stage `HdmiWakeService.dll`, its `.deps.json` and `.runtimeconfig.json` under
   `/opt/usr/apps/org.local.hdmiwake/bin`, and the supplied `tizen-manifest.xml`
   at the package root. Provide `shared/res`. On the inspected TV, package owner
   is `tizenglobalapp` (UID201), group root; directories are mode755. Do not copy
   vendor framework assemblies into the package.
2. Copy the same manifest to `/opt/share/packages/org.local.hdmiwake.xml`.
3. In the authorized installation context, register **only this app** with public
   privileges and its own read-only path label:

   ```sh
   security-manager-cmd --manage-apps add \
     --app org.local.hdmiwake.service --pkg org.local.hdmiwake \
     --uid 5001 --tizen 6.0 --install-type global \
     --pkg-type core --pkg-privilege-level public \
     --path /opt/usr/apps/org.local.hdmiwake ro
   ```

4. Run `dotnet /path/to/ManifestTool.dll install` in the authorized package-manager
   context (the inspected root session used process SMACK label `System`). The
   helper refuses other library builds. `upgrade` is for an existing registration;
   neither operation grants security permissions. Do not bypass a hash failure.
5. Launch `app_launcher -s org.local.hdmiwake.service` and inspect logs/processes.
   Package caches may require a complete reboot to observe a new registration.
   An accepted launch request alone does not establish that the service is running.

The private parser leaves `light_user_switch_mode` null in both package and app
structures while its database requires a value. The helper fills `default` at
verified ARM32 offsets and then uses the native installer transaction. It does
not directly edit package SQL or install shell startup hooks. Its refactored
registration and removal paths require device validation before fresh use.

## Verify without accidentally starting the policy

After a complete reboot, first read the boot ID, application state and logs;
**do not launch the service from the computer** before collecting the evidence.

```sh
cat /proc/sys/kernel/random/boot_id
app_launcher -r org.local.hdmiwake.service
cat /tmp/hdmiwake-bootstrap.log
tail /home/owner/share/tmp/sdk_tools/hdmi-wake-policy.log
```

Check for a new boot ID in the bootstrap entry, a live service process, and actual
HDMI1 after previously selecting HDMI4. Then test PS standby + TV off + controller
wake (HDMI4), followed by TV-remote off/on (HDMI1). Firmware updates, hard power
loss, service crashes and other HDMI-CEC devices have not been qualified.

## Rollback

For a code-only update, keep a copy of the installed DLL before replacement. Stop
only the verified `org.local.hdmiwake.service` process, restore that DLL while
preserving its owner/mode/label, and relaunch the app. Registration need not change.

For full removal on the same supported firmware, stop the verified service and
run `ManifestTool remove` while its manifest still exists. Remove only this app's
security-manager registration using the device's supported removal syntax, then
remove its package directory and `/opt/share/packages/org.local.hdmiwake.xml`.
Verify absence after reboot. The removal path is provided for review but has not
been executed against the working TV. Never delete shared databases or other apps.

Device logs, database backups, vendor libraries, temporary signing artifacts and
superseded prototypes stay local and are excluded from Git.
