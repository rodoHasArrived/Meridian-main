# Meridian Setup

Build-time bootstrapper for the public `Meridian-Setup.exe` artifact. The release build
embeds x64 and ARM64 payloads, selects the current architecture, installs under the
current user's local Programs directory, registers repair/uninstall, and preserves
application data during uninstall. Upgrades are extracted into a same-volume sibling stage,
verified against a SHA-256 manifest, and promoted as a complete directory; the previous complete
installation is retained as one-generation rollback state. An installed bootstrapper relaunches
from a temporary detached location before promotion so its live directory is never overwritten.
Production artifacts must be Authenticode-signed.
