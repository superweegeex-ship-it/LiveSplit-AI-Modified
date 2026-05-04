using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace LiveSplit.UI;

public enum UiPaintProfilerSection
{
    Paint,
    Background,
    Components,
    Labels,
}

public static class UiPaintProfiler
{
    private const int SectionCount = 4;
    private static readonly SectionAccumulator[] Sections =
    [
        new SectionAccumulator(),
        new SectionAccumulator(),
        new SectionAccumulator(),
        new SectionAccumulator(),
    ];
    private static readonly object SnapshotLock = new();
    private static long snapshotStartTicks = Stopwatch.GetTimestamp();
    private static volatile bool enabled;
    private static ProfilerSnapshot latestSnapshot;

    public static bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            Reset();
        }
    }

    public static long Begin()
    {
        return enabled ? Stopwatch.GetTimestamp() : 0;
    }

    public static void Record(UiPaintProfilerSection section, long startTicks)
    {
        if (!enabled || startTicks == 0)
        {
            return;
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        if (elapsedTicks <= 0)
        {
            return;
        }

        SectionAccumulator accumulator = Sections[(int)section];
        Interlocked.Add(ref accumulator.TotalTicks, elapsedTicks);
        Interlocked.Increment(ref accumulator.Count);
        SetMax(ref accumulator.MaxTicks, elapsedTicks);
        RotateSnapshotIfNeeded();
    }

    public static string GetOverlayText()
    {
        if (!enabled)
        {
            return string.Empty;
        }

        RotateSnapshotIfNeeded(forceIfStale: true);
        ProfilerSnapshot snapshot = latestSnapshot;
        if (!snapshot.HasData)
        {
            return "UI Paint" + Environment.NewLine + "(collecting...)";
        }

        var sb = new StringBuilder(256);
        sb.Append("UI Paint (last ")
            .Append(snapshot.WindowSeconds.ToString("0.0", CultureInfo.InvariantCulture))
            .AppendLine("s)");
        AppendCallLine(sb, "Paint", snapshot.Sections[(int)UiPaintProfilerSection.Paint]);
        AppendCallLine(sb, "Background", snapshot.Sections[(int)UiPaintProfilerSection.Background]);
        AppendCallLine(sb, "Components", snapshot.Sections[(int)UiPaintProfilerSection.Components]);
        AppendLabelLine(sb, snapshot.Sections[(int)UiPaintProfilerSection.Labels], snapshot.Sections[(int)UiPaintProfilerSection.Paint].Count);
        return sb.ToString();
    }

    private static void AppendCallLine(StringBuilder sb, string label, SectionSnapshot section)
    {
        sb.Append(label)
            .Append(": ")
            .Append(section.AvgMs.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" avg / ")
            .Append(section.MaxMs.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" max ms (")
            .Append(section.CallsPerSecond.ToString("0.0", CultureInfo.InvariantCulture))
            .AppendLine("/s)");
    }

    private static void AppendLabelLine(StringBuilder sb, SectionSnapshot labels, long paintCount)
    {
        double msPerPaint = paintCount <= 0 ? 0.0 : labels.TotalMs / paintCount;
        sb.Append("Labels: ")
            .Append(msPerPaint.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" ms/paint, ")
            .Append(labels.AvgMs.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" avg / ")
            .Append(labels.MaxMs.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" max ms (")
            .Append(labels.CallsPerSecond.ToString("0.0", CultureInfo.InvariantCulture))
            .Append("/s)");
    }

    private static void RotateSnapshotIfNeeded(bool forceIfStale = false)
    {
        long now = Stopwatch.GetTimestamp();
        long start = Volatile.Read(ref snapshotStartTicks);
        long elapsed = now - start;
        if (elapsed < Stopwatch.Frequency && (!forceIfStale || elapsed < Stopwatch.Frequency / 2))
        {
            return;
        }

        lock (SnapshotLock)
        {
            start = snapshotStartTicks;
            elapsed = now - start;
            if (elapsed <= 0 || (elapsed < Stopwatch.Frequency && (!forceIfStale || elapsed < Stopwatch.Frequency / 2)))
            {
                return;
            }

            double seconds = elapsed / (double)Stopwatch.Frequency;
            var snapshots = new SectionSnapshot[SectionCount];
            bool hasData = false;
            for (int i = 0; i < SectionCount; i++)
            {
                SectionAccumulator accumulator = Sections[i];
                long totalTicks = Interlocked.Exchange(ref accumulator.TotalTicks, 0);
                long count = Interlocked.Exchange(ref accumulator.Count, 0);
                long maxTicks = Interlocked.Exchange(ref accumulator.MaxTicks, 0);
                snapshots[i] = SectionSnapshot.From(totalTicks, count, maxTicks, seconds);
                hasData |= count > 0;
            }

            latestSnapshot = new ProfilerSnapshot
            {
                HasData = hasData,
                WindowSeconds = seconds,
                Sections = snapshots,
            };
            snapshotStartTicks = now;
        }
    }

    private static void Reset()
    {
        lock (SnapshotLock)
        {
            for (int i = 0; i < SectionCount; i++)
            {
                SectionAccumulator accumulator = Sections[i];
                Interlocked.Exchange(ref accumulator.TotalTicks, 0);
                Interlocked.Exchange(ref accumulator.Count, 0);
                Interlocked.Exchange(ref accumulator.MaxTicks, 0);
            }

            latestSnapshot = default;
            snapshotStartTicks = Stopwatch.GetTimestamp();
        }
    }

    private static void SetMax(ref long location, long value)
    {
        long current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    private sealed class SectionAccumulator
    {
        public long TotalTicks;
        public long Count;
        public long MaxTicks;
    }

    private struct ProfilerSnapshot
    {
        public bool HasData;
        public double WindowSeconds;
        public SectionSnapshot[] Sections;
    }

    private struct SectionSnapshot
    {
        public long Count;
        public double TotalMs;
        public double AvgMs;
        public double MaxMs;
        public double CallsPerSecond;

        public static SectionSnapshot From(long totalTicks, long count, long maxTicks, double seconds)
        {
            double totalMs = totalTicks * 1000.0 / Stopwatch.Frequency;
            return new SectionSnapshot
            {
                Count = count,
                TotalMs = totalMs,
                AvgMs = count <= 0 ? 0.0 : totalMs / count,
                MaxMs = maxTicks * 1000.0 / Stopwatch.Frequency,
                CallsPerSecond = seconds <= 0.0 ? 0.0 : count / seconds,
            };
        }
    }
}
