using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using CspPaletteCompanion.Companion;

namespace CspPaletteCompanion.App;

internal enum MuxHandoffStatus
{
    Live,
    Absent,
    Malformed,
    VersionTooNew,
    Stale,
    Unverifiable,
    NotLoopback,
    PortNotOwned,
}

internal readonly record struct MuxHandoffResult(
    MuxHandoffStatus Status,
    CompanionPairingInfo? Pairing);

/// <summary>
/// Reads the file the Mux publishes while it is sharing, and refuses it unless every
/// check passes. <see cref="TryRead"/> and <see cref="ReadAtConnect"/> are total: both
/// run from a 2-second <c>DispatcherTimer</c> handler, and an escaping exception there
/// is a crash loop at 0.5 Hz rather than a one-off failure.
/// The Companion never writes, deletes or creates anything under the Mux's directory.
/// </summary>
internal sealed class MuxHandoffReader
{
    private MuxHandoffResult _cached = new(MuxHandoffStatus.Absent, null);
    private MuxSessionDocument? _document;
    private DateTime _writtenUtc;
    private long _length = -1;
    private bool _existed;

    /// <summary>
    /// The polling read. A file whose mtime, length and existence are unchanged is not
    /// re-parsed; a cached <see cref="MuxHandoffStatus.Live"/> still re-runs the process
    /// and port-ownership checks, which is what lets the strip leave S0 within one tick
    /// when the Mux's listener stops without its process exiting.
    /// </summary>
    internal MuxHandoffResult TryRead() => Read(useCache: true);

    /// <summary>
    /// The authoritative evaluation, run at the moment of commitment. Everything the
    /// poll produced is a hint that drives wording; this is what a credential is sent on.
    /// </summary>
    internal MuxHandoffResult ReadAtConnect() => Read(useCache: false);

    private MuxHandoffResult Read(bool useCache)
    {
        bool exists;
        long length;
        DateTime writtenUtc;
        try
        {
            var info = new FileInfo(MuxHandoffContract.FilePath);
            exists = info.Exists;
            length = exists ? info.Length : -1;
            writtenUtc = exists ? info.LastWriteTimeUtc : default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Remember(new MuxHandoffResult(MuxHandoffStatus.Malformed, null), null, false, -1, default);
        }

        var unchanged = exists == _existed && length == _length && writtenUtc == _writtenUtc;
        if (useCache && unchanged)
        {
            if (_cached.Status != MuxHandoffStatus.Live ||
                _document is null ||
                _cached.Pairing is null)
            {
                // A malformed file does not become live without changing.
                return _cached;
            }

            var revalidated = VerifyProcess(_document)
                ?? (OwnsAnyListener(_document.ProcessId, _cached.Pairing)
                    ? MuxHandoffStatus.Live
                    : MuxHandoffStatus.PortNotOwned);
            _cached = new MuxHandoffResult(revalidated, revalidated == MuxHandoffStatus.Live ? _cached.Pairing : null);
            return _cached;
        }

        var result = Evaluate(exists, out var document);
        return Remember(result, document, exists, length, writtenUtc);
    }

    private static MuxHandoffResult Evaluate(bool exists, out MuxSessionDocument? document)
    {
        document = null;
        if (!exists)
        {
            return new MuxHandoffResult(MuxHandoffStatus.Absent, null);
        }

        MuxSessionDocument? parsed;
        try
        {
            // FileShare.Delete is required, not optional: without it the Mux's own
            // replace-on-publish and its delete-on-stop both fail with a sharing
            // violation for as long as this reader holds the file open.
            using var stream = new FileStream(
                MuxHandoffContract.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // The bound is taken from the opened handle. A pre-open stat measures the
            // previous file, which is a check rather than a cap: the Mux replaces this
            // path at every session start.
            if (stream.Length > MuxHandoffContract.MaximumFileBytes)
            {
                return new MuxHandoffResult(MuxHandoffStatus.Malformed, null);
            }

            parsed = JsonSerializer.Deserialize<MuxSessionDocument>(stream, MuxHandoffContract.Json);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MuxHandoffResult(MuxHandoffStatus.Malformed, null);
        }

        // Deserialize returns null for the JSON literal "null" even when every member is
        // required, and an explicit "pairingUrl": null binds without a JsonException.
        // Nullable reference types make this worse, not better: the compiler guarantees
        // PairingUrl is non-null, so neither check is written unless it is written here.
        if (parsed is null)
        {
            return new MuxHandoffResult(MuxHandoffStatus.Malformed, null);
        }

        if (parsed.SchemaVersion > MuxHandoffContract.SchemaVersion)
        {
            return new MuxHandoffResult(MuxHandoffStatus.VersionTooNew, null);
        }

        if (parsed.SchemaVersion < MuxHandoffContract.SchemaVersion ||
            string.IsNullOrWhiteSpace(parsed.PairingUrl) ||
            parsed.ProcessId <= 0)
        {
            return new MuxHandoffResult(MuxHandoffStatus.Malformed, null);
        }

        var liveness = VerifyProcess(parsed);
        if (liveness is { } failed)
        {
            return new MuxHandoffResult(failed, null);
        }

        CompanionPairingInfo pairing;
        try
        {
            pairing = CompanionPairingCodec.Decode(parsed.PairingUrl);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return new MuxHandoffResult(MuxHandoffStatus.Malformed, null);
        }
        catch (InvalidOperationException)
        {
            return new MuxHandoffResult(MuxHandoffStatus.NotLoopback, null);
        }

        // Every address, not any: the file path has no human confirmation step, so it
        // must be tighter than the codec's private-or-local rule.
        if (pairing.Addresses.Count == 0 || !pairing.Addresses.All(IPAddress.IsLoopback))
        {
            return new MuxHandoffResult(MuxHandoffStatus.NotLoopback, null);
        }

        if (!OwnsAnyListener(parsed.ProcessId, pairing))
        {
            return new MuxHandoffResult(MuxHandoffStatus.PortNotOwned, null);
        }

        document = parsed;
        return new MuxHandoffResult(MuxHandoffStatus.Live, pairing);
    }

    private static MuxHandoffStatus? VerifyProcess(MuxSessionDocument document)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(document.ProcessId);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return MuxHandoffStatus.Stale;
        }

        using (process)
        {
            if (!string.Equals(
                    process.ProcessName,
                    MuxHandoffContract.MuxProcessName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return MuxHandoffStatus.Stale;
            }

            DateTime started;
            try
            {
                started = process.StartTime.ToUniversalTime();
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Fails closed. An elevated Mux denies the query, and the QR path still
                // works across integrity levels because it is a screen capture.
                return MuxHandoffStatus.Unverifiable;
            }

            // The two sides are exactly equal today. The tolerance is kept so a future
            // format change degrades into a one-second window rather than a permanent,
            // undiagnosable refusal.
            var drift = started - document.ProcessStartTimeUtc;
            return Math.Abs(drift.Ticks) > MuxHandoffContract.StartTimeTolerance.Ticks
                ? MuxHandoffStatus.Stale
                : null;
        }
    }

    private static bool OwnsAnyListener(int processId, CompanionPairingInfo pairing) =>
        pairing.Addresses.Any(address =>
            NativeMethods.OwnsListener(processId, address, pairing.Port));

    private MuxHandoffResult Remember(
        MuxHandoffResult result,
        MuxSessionDocument? document,
        bool exists,
        long length,
        DateTime writtenUtc)
    {
        _cached = result;
        _document = document;
        _existed = exists;
        _length = length;
        _writtenUtc = writtenUtc;
        return result;
    }
}
