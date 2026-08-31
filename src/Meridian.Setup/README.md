# Meridian Setup

Build-time bootstrapper for the public `Meridian-Setup.exe` artifact. The release build
appends x64 and ARM64 payloads to the finished executable, selects the current architecture,
installs under the current user's local Programs directory, registers repair/uninstall, and preserves
application data during uninstall. Upgrades are extracted into a same-volume sibling stage,
verified against a SHA-256 manifest, and promoted as a complete directory; the previous complete
installation is retained as one-generation rollback state. An installed bootstrapper relaunches
from a temporary detached location before promotion so its live directory is never overwritten.
Production artifacts must be Authenticode-signed.

## Payload delivery

The payload is a ZIP archive appended to the published executable, followed by a fixed 138-byte
ASCII trailer recording its offset, length, and SHA-256. `PayloadPackage` documents the layout and
reads it back; `build/scripts/install/build-consumer-setup.ps1` writes it.

It is deliberately not an `EmbeddedResource`. Roslyn serialises resources into the PE image's
mapped field data and overflows on a payload this size, so the release build failed outright with
`ArgumentOutOfRangeException (mappedFieldDataStreamRva)` and no consumer setup could be produced at
all.

Appending runs before signing, so the payload falls inside the Authenticode hash. Signing then adds
the certificate table after the trailer, which is why the trailer is located by scanning backwards
for its magic rather than by reading from the end of the file.

Run `Meridian-Setup.exe --verify-payload` to check a download without installing anything: it exits
0 when the payload is present and matches its recorded digest, and 2 otherwise. The release build
runs it against every artifact it produces.
