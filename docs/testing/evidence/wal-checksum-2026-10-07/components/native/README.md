# Native SHA-256 component diagnostics

This standalone C diagnostic isolates OpenSSL provider setup from SHA-256 compression. It is not production code and is not linked into Meridian.

`native_sha256.c` compares four paths: a new EVP context per call (the same native lifecycle as .NET 10's Linux one-shot path), `EVP_Digest` one-shot, one reused EVP context, and deprecated low-level `SHA256_Init/Update/Final`. The last path establishes a compression performance floor; it bypasses provider policy and is not proposed as an application implementation. The diagnostic omits .NET's managed/native transitions, concurrency guards, and explicit OpenSSL error-queue clearing.

It measures raw 64, 900, and 4096 byte inputs, plus the full encoded WAL benchmark records containing those payloads. All methods are compared against OpenSSL's SHA-256 result before timing. Outputs retain every repetition, encoded length, iteration count, mean time, and throughput.

```sh
cc -O3 -Wall -Wextra -Wno-deprecated-declarations native_sha256.c -lcrypto -o native_sha256
./native_sha256 100000 5 > measurements.csv 2> run.log
```

Run after the portable baseline completes, with no concurrent CPU-intensive work. These component measurements explain the bottleneck and do not replace the portable BenchmarkDotNet stages or change their budgets.
