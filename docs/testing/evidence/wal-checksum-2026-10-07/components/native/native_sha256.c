/* Diagnostic only: compare provider setup with SHA-256 compression cost.
 * Never linked into Meridian. Low-level SHA APIs are deprecated and bypass
 * OpenSSL provider selection; they establish a performance floor only.
 * Build: cc -O3 -Wall -Wextra -Wno-deprecated-declarations native_sha256.c -lcrypto -o native_sha256
 * Run: ./native_sha256 [iterations=100000] [repetitions=5]
 */
#define _POSIX_C_SOURCE 200809L
#include <openssl/crypto.h>
#include <openssl/evp.h>
#include <openssl/sha.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

enum method { EVP_NEW_CONTEXT, EVP_DIGEST, EVP_REUSE, SHA_LOW_LEVEL };
static const char *names[] = {
    "evp_new_context_dotnet_equivalent", "evp_digest_one_shot",
    "evp_reused_context", "sha256_low_level_diagnostic_only"
};
static volatile unsigned output_sink;

static uint64_t nanos(void)
{
    struct timespec ts;
    if (clock_gettime(CLOCK_MONOTONIC_RAW, &ts) != 0) {
        perror("clock_gettime");
        exit(2);
    }
    return (uint64_t)ts.tv_sec * UINT64_C(1000000000) + (uint64_t)ts.tv_nsec;
}

static int digest(enum method method, EVP_MD_CTX *ctx, const EVP_MD *md,
                  const unsigned char *data, size_t size, unsigned char *out)
{
    unsigned written = 0;
    int ok;
    switch (method) {
    case EVP_NEW_CONTEXT: {
        EVP_MD_CTX *fresh = EVP_MD_CTX_new();
        if (!fresh) return 0;
        ok = EVP_DigestInit_ex(fresh, md, NULL) &&
             EVP_DigestUpdate(fresh, data, size) &&
             EVP_DigestFinal_ex(fresh, out, &written);
        EVP_MD_CTX_free(fresh);
        return ok && written == SHA256_DIGEST_LENGTH;
    }
    case EVP_DIGEST:
        return EVP_Digest(data, size, out, &written, md, NULL) &&
               written == SHA256_DIGEST_LENGTH;
    case EVP_REUSE:
        return EVP_DigestUpdate(ctx, data, size) &&
               EVP_DigestFinal_ex(ctx, out, &written) &&
               EVP_DigestInit_ex(ctx, md, NULL) &&
               written == SHA256_DIGEST_LENGTH;
    case SHA_LOW_LEVEL: {
        SHA256_CTX direct;
        return SHA256_Init(&direct) && SHA256_Update(&direct, data, size) &&
               SHA256_Final(out, &direct);
    }
    }
    return 0;
}

static void run_case(const char *kind, size_t payload_size, const char *prefix,
                     unsigned iterations, unsigned repetitions)
{
    size_t prefix_size = strlen(prefix);
    size_t size = prefix_size + payload_size;
    unsigned char *data = malloc(size);
    unsigned char expected[SHA256_DIGEST_LENGTH];
    unsigned char output[SHA256_DIGEST_LENGTH];
    const EVP_MD *md = EVP_sha256();
    EVP_MD_CTX *ctx = EVP_MD_CTX_new();
    if (!data || !ctx || !md) exit(2);
    memcpy(data, prefix, prefix_size);
    memset(data + prefix_size, 'x', payload_size);
    if (!SHA256(data, size, expected) || !EVP_DigestInit_ex(ctx, md, NULL)) exit(2);

    for (enum method method = EVP_NEW_CONTEXT; method <= SHA_LOW_LEVEL; ++method) {
        for (unsigned warm = 0; warm < 1000; ++warm) {
            if (!digest(method, ctx, md, data, size, output)) exit(2);
        }
        if (memcmp(expected, output, sizeof(expected)) != 0) {
            fprintf(stderr, "SHA256 mismatch for %s\n", names[method]);
            exit(3);
        }
        for (unsigned rep = 0; rep < repetitions; ++rep) {
            uint64_t start = nanos();
            for (unsigned i = 0; i < iterations; ++i) {
                if (!digest(method, ctx, md, data, size, output)) exit(2);
                output_sink ^= output[0];
            }
            uint64_t elapsed = nanos() - start;
            printf("%s,%zu,%zu,%s,%u,%u,%.3f,%.3f\n", kind, payload_size,
                   size, names[method], rep + 1, iterations,
                   (double)elapsed / iterations,
                   (double)size * iterations / elapsed);
            fflush(stdout);
        }
    }
    EVP_MD_CTX_free(ctx);
    free(data);
}

int main(int argc, char **argv)
{
    unsigned iterations = argc > 1 ? (unsigned)strtoul(argv[1], NULL, 10) : 100000;
    unsigned repetitions = argc > 2 ? (unsigned)strtoul(argv[2], NULL, 10) : 5;
    if (!iterations || !repetitions) return 2;
    fprintf(stderr, "OpenSSL=%s; iterations=%u; repetitions=%u; clock=MONOTONIC_RAW\n",
            OpenSSL_version(OPENSSL_VERSION), iterations, repetitions);
    printf("input_kind,payload_bytes,encoded_bytes,method,repetition,iterations,mean_ns,GB_per_second\n");
    const size_t sizes[] = { 64, 900, 4096 };
    for (size_t i = 0; i < sizeof(sizes) / sizeof(sizes[0]); ++i)
        run_case("raw_payload", sizes[i], "", iterations, repetitions);
    run_case("wal_record", 64, "1|2024-01-15T14:30:00.0000000Z|Trade|", iterations, repetitions);
    run_case("wal_record", 900, "2|2024-01-15T14:30:00.0000000Z|L2Snapshot|", iterations, repetitions);
    run_case("wal_record", 4096, "3|2024-01-15T14:30:00.0000000Z|L2Snapshot|", iterations, repetitions);
    return output_sink == UINT32_MAX ? 1 : 0;
}
