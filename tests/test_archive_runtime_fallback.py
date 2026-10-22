"""Samsung launchers without the standard .NET host runtime listing command."""

import pytest

from samsung_tv_root import archive_root


class Client:
    def connect(self):
        pass

    def require_device(self):
        pass

    def require_shell_injection(self):
        pass


@pytest.mark.parametrize(
    ("versions", "expected"),
    [("6.0.9", "6.0"), ("3.1.3", "3.1"), ("junk\n6.0.9\n6.0", "6.0"), ("", None)],
)
def test_runtime_fallback_requires_verified_runtime_files(monkeypatch, versions, expected):
    commands = []

    def capture(client, command, timeout=15):
        commands.append(command)
        if command.startswith("getcap"):
            return (
                "/usr/bin/dotnet cap_setgid,cap_sys_admin=ei\n"
                "/usr/sbin/sdbd-tarlauncher cap_setgid,cap_setuid=eip"
            )
        if "--list-runtimes" in command:
            raise archive_root.ArchiveRootError("Unknown option --list-runtimes")
        if command.startswith("for d in"):
            assert 'test -f "$d/libcoreclr.so"' in command
            assert 'test -f "$d/System.Private.CoreLib.dll"' in command
            return versions
        if "tar --help" in command:
            return "--to-command"
        return "unchanged hashes"

    monkeypatch.setattr(archive_root, "_capture", capture)
    if expected is None:
        with pytest.raises(archive_root.ArchiveRootError, match="no supported"):
            archive_root._check_target(Client())
    else:
        assert archive_root._check_target(Client()).runtime == expected
    assert sum(command.startswith("for d in") for command in commands) == 1


def test_runtime_listing_failure_is_not_masked(monkeypatch):
    def capture(client, command, timeout=15):
        if command.startswith("getcap"):
            return (
                "/usr/bin/dotnet cap_setgid,cap_sys_admin=ei\n"
                "/usr/sbin/sdbd-tarlauncher cap_setgid,cap_setuid=eip"
            )
        if "--list-runtimes" in command:
            raise archive_root.ArchiveRootError("permission denied")
        pytest.fail("Unexpected fallback after an unrelated runtime error")

    monkeypatch.setattr(archive_root, "_capture", capture)
    with pytest.raises(archive_root.ArchiveRootError, match="permission denied"):
        archive_root._check_target(Client())
