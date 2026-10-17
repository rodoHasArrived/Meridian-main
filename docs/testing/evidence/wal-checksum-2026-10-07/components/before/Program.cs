using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

class Program {
static readonly SHA256 CachedHash = SHA256.Create();
static readonly IncrementalHash CachedIncremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
static readonly DateTime Timestamp = new DateTime(2024,1,15,14,30,0, DateTimeKind.Utc);
static byte[] Encoded = [];
static string Payload = "";
static int Sink;
static void Main() {
Console.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; CPUs {Environment.ProcessorCount}");
foreach(var size in new[]{64,900,4096}) {
Payload=new string('x',size);
Encoded=Encoding.UTF8.GetBytes($"1|{Timestamp:O}|L2Snapshot|{Payload}");
foreach(var method in new Action[]{Original, Format, OneShot, Reused, Incremental, ReuseRecord, IncrementalRecord}) {
for(int i=0;i<20000;i++) method();
var times=new List<double>(); long allocated=0;
for(int sample=0;sample<8;sample++) {
var bytes=GC.GetAllocatedBytesForCurrentThread(); var start=Stopwatch.GetTimestamp();
for(int i=0;i<50000;i++) method();
var ticks=Stopwatch.GetTimestamp()-start; allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;
times.Add(ticks*1e9/Stopwatch.Frequency/50000);
}
Console.WriteLine($"{size} {method.Method.Name} mean_ns={times.Average():F2} min_ns={times.Min():F2} max_ns={times.Max():F2} bytes_per_op={allocated/400000.0:F2} samples=[{string.Join(",",times.Select(x=>x.ToString("F2")))}]");
}
} Console.WriteLine($"sink={Sink}");
}
static void Original(){Span<byte> dest=stackalloc byte[32];ComputeChecksumCore(1,Timestamp,"L2Snapshot",Payload,dest); Sink ^= dest[0];}
static void Format(){Span<byte> dest=stackalloc byte[32];FormatOnly(1,Timestamp,"L2Snapshot",Payload,dest); Sink ^= dest[0];}
static void OneShot(){Span<byte> dest=stackalloc byte[32];SHA256.TryHashData(Encoded,dest,out _); Sink ^= dest[0];}
static void Reused(){Span<byte> dest=stackalloc byte[32];CachedHash.TryComputeHash(Encoded,dest,out _); Sink ^= dest[0];}
static void Incremental(){Span<byte> dest=stackalloc byte[32];CachedIncremental.AppendData(Encoded);CachedIncremental.TryGetHashAndReset(dest,out _); Sink ^= dest[0];}
static void ReuseRecord(){Span<byte> dest=stackalloc byte[32];ReusedCore(1,Timestamp,"L2Snapshot",Payload,dest); Sink ^= dest[0];}
static void IncrementalRecord(){Span<byte> dest=stackalloc byte[32];IncrementalCore(1,Timestamp,"L2Snapshot",Payload,dest); Sink ^= dest[0];}
    private static void ComputeChecksumCore(long sequence, DateTime timestamp, string recordType, string payload, Span<byte> destination)
    {
        var recordTypeByteCount = Encoding.UTF8.GetByteCount(recordType);
        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        var totalByteCount = 20 + 33 + 3 + recordTypeByteCount + payloadByteCount;

        byte[]? rented = null;
        var buffer = totalByteCount <= 4608
            ? stackalloc byte[4608]
            : (rented = ArrayPool<byte>.Shared.Rent(totalByteCount));

        var recordBytes = buffer[..totalByteCount];

        try
        {
            var written = 0;

            if (!Utf8Formatter.TryFormat(sequence, recordBytes[written..], out var sequenceWritten))
            {
                throw new InvalidOperationException("Failed to format WAL sequence.");
            }

            written += sequenceWritten;
            recordBytes[written++] = (byte)'|';

            if (!Utf8Formatter.TryFormat(timestamp, recordBytes[written..], out var timestampWritten, 'O'))
            {
                throw new InvalidOperationException("Failed to format WAL timestamp.");
            }

            written += timestampWritten;
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, recordBytes[written..]);
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, recordBytes[written..]);

            if (!SHA256.TryHashData(recordBytes[..written], destination, out _))
            {
                throw new InvalidOperationException("Failed to compute WAL checksum.");
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
    private static void FormatOnly(long sequence, DateTime timestamp, string recordType, string payload, Span<byte> destination)
    {
        var recordTypeByteCount = Encoding.UTF8.GetByteCount(recordType);
        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        var totalByteCount = 20 + 33 + 3 + recordTypeByteCount + payloadByteCount;

        byte[]? rented = null;
        var buffer = totalByteCount <= 4608
            ? stackalloc byte[4608]
            : (rented = ArrayPool<byte>.Shared.Rent(totalByteCount));

        var recordBytes = buffer[..totalByteCount];

        try
        {
            var written = 0;

            if (!Utf8Formatter.TryFormat(sequence, recordBytes[written..], out var sequenceWritten))
            {
                throw new InvalidOperationException("Failed to format WAL sequence.");
            }

            written += sequenceWritten;
            recordBytes[written++] = (byte)'|';

            if (!Utf8Formatter.TryFormat(timestamp, recordBytes[written..], out var timestampWritten, 'O'))
            {
                throw new InvalidOperationException("Failed to format WAL timestamp.");
            }

            written += timestampWritten;
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, recordBytes[written..]);
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, recordBytes[written..]);

            destination[0] = recordBytes[0];
            if (written == 0)
            {
                throw new InvalidOperationException("Failed to compute WAL checksum.");
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
    private static void ReusedCore(long sequence, DateTime timestamp, string recordType, string payload, Span<byte> destination)
    {
        var recordTypeByteCount = Encoding.UTF8.GetByteCount(recordType);
        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        var totalByteCount = 20 + 33 + 3 + recordTypeByteCount + payloadByteCount;

        byte[]? rented = null;
        var buffer = totalByteCount <= 4608
            ? stackalloc byte[4608]
            : (rented = ArrayPool<byte>.Shared.Rent(totalByteCount));

        var recordBytes = buffer[..totalByteCount];

        try
        {
            var written = 0;

            if (!Utf8Formatter.TryFormat(sequence, recordBytes[written..], out var sequenceWritten))
            {
                throw new InvalidOperationException("Failed to format WAL sequence.");
            }

            written += sequenceWritten;
            recordBytes[written++] = (byte)'|';

            if (!Utf8Formatter.TryFormat(timestamp, recordBytes[written..], out var timestampWritten, 'O'))
            {
                throw new InvalidOperationException("Failed to format WAL timestamp.");
            }

            written += timestampWritten;
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, recordBytes[written..]);
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, recordBytes[written..]);

            if (!CachedHash.TryComputeHash(recordBytes[..written], destination, out _))
            {
                throw new InvalidOperationException("Failed to compute WAL checksum.");
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
    private static void IncrementalCore(long sequence, DateTime timestamp, string recordType, string payload, Span<byte> destination)
    {
        var recordTypeByteCount = Encoding.UTF8.GetByteCount(recordType);
        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        var totalByteCount = 20 + 33 + 3 + recordTypeByteCount + payloadByteCount;

        byte[]? rented = null;
        var buffer = totalByteCount <= 4608
            ? stackalloc byte[4608]
            : (rented = ArrayPool<byte>.Shared.Rent(totalByteCount));

        var recordBytes = buffer[..totalByteCount];

        try
        {
            var written = 0;

            if (!Utf8Formatter.TryFormat(sequence, recordBytes[written..], out var sequenceWritten))
            {
                throw new InvalidOperationException("Failed to format WAL sequence.");
            }

            written += sequenceWritten;
            recordBytes[written++] = (byte)'|';

            if (!Utf8Formatter.TryFormat(timestamp, recordBytes[written..], out var timestampWritten, 'O'))
            {
                throw new InvalidOperationException("Failed to format WAL timestamp.");
            }

            written += timestampWritten;
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, recordBytes[written..]);
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, recordBytes[written..]);

            CachedIncremental.AppendData(recordBytes[..written]);
            if (!CachedIncremental.TryGetHashAndReset(destination, out _))
            {
                throw new InvalidOperationException("Failed to compute WAL checksum.");
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

}
