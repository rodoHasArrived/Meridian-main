# Meridian Setup

Build-time bootstrapper for the public `Meridian-Setup.exe` artifact. The release build
embeds x64 and ARM64 payloads, selects the current architecture, installs under the
current user's local Programs directory, registers repair/uninstall, and preserves
application data during uninstall. Production artifacts must be Authenticode-signed.
