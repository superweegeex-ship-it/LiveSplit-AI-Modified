using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace LiveSplit.View;

/// <summary>Keeps modal settings responsive without changing the saved refresh rate.</summary>
internal sealed class ModalRefreshScheduler : Component
{
    private readonly Func<int> refreshRate;
    private readonly Action refresh;
    private readonly Timer timer = new();
    private volatile bool active;
    private bool refreshing;
    private bool disposed;

    public bool Active => active;

    public ModalRefreshScheduler(IContainer container, Func<int> refreshRate, Action refresh)
    {
        this.refreshRate = refreshRate;
        this.refresh = refresh;
        container.Add(this);
        timer.Tick += Tick;
    }

    private int GetInterval() => (int)Math.Ceiling(1000d / Math.Max(1, Math.Min(30, refreshRate())));

    public void SetActive(bool value)
    {
        if (disposed)
        {
            return;
        }

        active = value;
        if (active)
        {
            timer.Interval = GetInterval();
            timer.Start();
        }
        else
        {
            timer.Stop();
        }
    }

    private void Tick(object sender, EventArgs e)
    {
        if (!active || refreshing)
        {
            return;
        }

        refreshing = true;
        var elapsed = Stopwatch.StartNew();
        try
        {
            refresh();
        }
        finally
        {
            refreshing = false;
            if (active)
            {
                // WM_TIMER yields to input; expensive refreshes also get recovery time.
                timer.Interval = Math.Max(GetInterval(), (int)Math.Min(int.MaxValue, Math.Ceiling(elapsed.Elapsed.TotalMilliseconds)));
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            active = false;
            disposed = true;
            timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
