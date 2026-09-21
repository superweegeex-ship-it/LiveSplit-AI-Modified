using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LiveSplit.View;
using Xunit;

namespace LiveSplit.Tests.UI;

public class ModalRefreshSchedulerMust
{
    [Theory]
    [InlineData(60, 0)]
    [InlineData(300, 80)]
    [InlineData(int.MaxValue, 120)]
    public void AllowModalCloseUnderRefreshLoad(int requestedRate, int workMilliseconds)
    {
        OnStaThread(() =>
        {
            using var owner = HiddenForm();
            using var components = new Container();
            int calls = 0;
            using var scheduler = CreateScheduler(owner, components, () => requestedRate, () =>
            {
                calls++;
                Thread.Sleep(workMilliseconds);
            });
            owner.Show();
            Assert.False(IsActive(scheduler));
            for (int session = 0; session < 3; session++)
            {
                using var dialog = HiddenForm();
                var latency = new Stopwatch();
                System.Threading.Timer close = null;
                int before = calls;
                Console.WriteLine($"Rate {requestedRate}, work {workMilliseconds}, session {session}");
                dialog.Shown += (_, _) =>
                {
                    Assert.True(IsActive(scheduler));
                    IntPtr handle = dialog.Handle;
                    close = new System.Threading.Timer(_ =>
                    {
                        latency.Start();
                        Console.WriteLine($"Posted close: {PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero)}, calls {calls}"); // WM_CLOSE
                    }, null, 350, Timeout.Infinite);
                };
                dialog.ShowDialog(owner);
                latency.Stop();
                Console.WriteLine($"Closed in {latency.ElapsedMilliseconds} ms, calls {calls}");
                close?.Dispose();
                Assert.True(calls > before, "Live refresh must continue while editing.");
                Assert.True(latency.ElapsedMilliseconds < 1500, "Closing was starved by refresh work.");
                Assert.False(IsActive(scheduler));
            }
            owner.Close();
        });
    }

    [Fact]
    public void KeepOuterDialogThrottledAfterNestedDialogCloses()
    {
        OnStaThread(() =>
        {
            using var owner = HiddenForm();
            using var components = new Container();
            using var scheduler = CreateScheduler(owner, components, () => 300, () => { });
            owner.Show();
            using var outer = HiddenForm();
            outer.Shown += (_, _) => outer.BeginInvoke(new Action(() =>
            {
                using var inner = HiddenForm();
                inner.Shown += (_, _) => inner.BeginInvoke(new Action(inner.Close));
                inner.ShowDialog(outer);
                Assert.True(IsActive(scheduler));
                outer.Close();
            }));
            outer.ShowDialog(owner);
            Assert.False(IsActive(scheduler));
            owner.Close();
        });
    }

    [Fact]
    public void StopAndIgnoreFurtherTransitionsWhenDisposed()
    {
        OnStaThread(() =>
        {
            using var owner = HiddenForm();
            using var components = new Container();
            int calls = 0;
            var scheduler = CreateScheduler(owner, components, () => 300, () => calls++);
            owner.Show();
            owner.Enabled = false;
            Assert.True(IsActive(scheduler));
            scheduler.Dispose();
            Assert.False(IsActive(scheduler));
            owner.Enabled = true;
            owner.Enabled = false; // Must not touch a disposed timer through a leaked handler.
            Application.DoEvents();
            Assert.Equal(0, calls);
        });
    }

    private static Component CreateScheduler(Form owner, IContainer container, Func<int> rate, Action refresh)
    {
        Type type = typeof(TimerForm).Assembly.GetType("LiveSplit.View.ModalRefreshScheduler", true);
        var scheduler = (Component)Activator.CreateInstance(type, container, rate, refresh);
        ((ModalOwnerForm)owner).ModalStateChanged = value =>
            type.GetMethod("SetActive").Invoke(scheduler, new object[] { value });
        return scheduler;
    }

    private static bool IsActive(Component scheduler) =>
        (bool)scheduler.GetType().GetProperty("Active").GetValue(scheduler);

    private static Form HiddenForm() => new ModalOwnerForm
    {
        ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual,
        Location = new Point(-32000, -32000)
    };

    private sealed class ModalOwnerForm : Form
    {
        public Action<bool> ModalStateChanged;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x000A) // WM_ENABLE, as used by TimerForm
                ModalStateChanged?.Invoke(message.WParam == IntPtr.Zero);
            base.WndProc(ref message);
        }
    }

    private static void OnStaThread(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true); action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Modal refresh test timed out.");
        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
