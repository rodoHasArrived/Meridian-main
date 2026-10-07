# Attribution for archived K4os/LZ4 research sources

These notices accompany third-party source snapshots retained as diagnostic evidence for the rejected LZ4 checksum candidate. They do not introduce source into Meridian production or change the accepted checksum implementation.

The scratch compression project references **K4os.Compression.LZ4 1.3.8**. Its cached NuGet specification is retained byte-for-byte as `K4os.Compression.LZ4-1.3.8.nuspec`; it identifies the original repository and links its root license. GitHub tag `1.3.8` resolves to commit `f5a25b7d72e2e41550fe20662597169ff11c3b60` in [MiloszKrajewski/K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4/tree/f5a25b7d72e2e41550fe20662597169ff11c3b60).

The following files are unmodified bytes fetched from that commit:

- `K4os.Compression.LZ4-1.3.8-LICENSE.txt`: repository root MIT license and Milosz Krajewski copyright notice.
- `LZ4-library-LICENSE.txt`: original `orig/lib/LICENSE`, the upstream LZ4 library BSD 2-Clause notice and Yann Collet copyright notice.
- `LZ4-license-scope.txt`: original `orig/LICENSE`, explaining the upstream library versus programs/tests/examples license boundary. Those programs, tests and examples are not retained in this evidence packet.

`provenance.json` records the exact fetch URLs, release commit, content sizes and SHA-256 hashes. It also records release-source hashes for these K4os paths:

- `src/K4os.Compression.LZ4/LZ4Codec.cs`, archived as `components/candidates/LZ4Codec.cs`.
- `src/K4os.Compression.LZ4/Engine/LLxx.cs`, archived as `components/candidates/LLxx.cs`.
- `src/K4os.Compression.LZ4/Engine/x32/LL32.fast.cs`.
- `src/K4os.Compression.LZ4/Engine/x64/LL64.fast.cs`.

The first two archived snapshots match the release bytes exactly. The last two encoder paths were referenced during investigation but are absent from the current staging packet; their release-source hashes are included to identify them if a separate copy is later retained. The K4os MIT notice covers the K4os source snapshots; the original LZ4 library notice is retained alongside it for the translated compression implementation's attribution.
