using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

using LiveSplit.Options.SettingsFactories;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using LiveSplit.View;

using Xunit;

using IComponent = LiveSplit.UI.Components.IComponent;

namespace LiveSplit.Tests.UI;

public class LayoutSettingsDialogMust
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReleaseDialogsAndKeepBorrowedControlsReusable(bool withFonts, bool visitComponent)
    {
        OnStaThread(() =>
        {
            var component = withFonts ? new FontSettingsComponent() : new SettingsComponent();
            var layout = CreateLayout(component);
            var controls = component.Settings;
            string[] events = ["EventHandleCreated", "EventControlAdded", "EventControlRemoved", "EventSize"];
            int[] before = events.Select(name => HandlerCount(controls, name)).ToArray();
            var windows = new List<WeakReference>();
            for (int i = 0; i < 20; i++)
            {
                windows.Add(CreateAndDispose(layout, visitComponent));
                Assert.False(controls.IsDisposed);
                Assert.Null(controls.Parent);
                Assert.Equal(before, events.Select(name => HandlerCount(controls, name)).ToArray());
                controls.Width++;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.All(windows, window => Assert.False(window.IsAlive));
            controls.Dispose();
            component.Dispose();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreserveOkAndCancelAcrossRepeatedModalSessions(bool cancel)
    {
        OnStaThread(() =>
        {
            var component = new FontSettingsComponent();
            var layout = CreateLayout(component);
            var lc = (LayoutComponent)layout.LayoutComponents[0];
            for (int i = 0; i < 5; i++)
            {
                component.Value = 10;
                lc.FontOverrides.OverrideTextFont = false;
                Exception failure = null;
                using (var dialog = new TestDialog(layout))
                {
                    int transparencyUpdates = 0;
                    dialog.LiveApplyRequested += (_, e) =>
                    {
                        if (e.Scope == BackgroundVideoLiveApplyScope.WindowTransparencyOnly)
                        {
                            transparencyUpdates++;
                        }
                    };
                    dialog.Shown += (_, _) => dialog.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var tabs = (TabControl)dialog.Controls.Find("tabControl", true)[0];
                            tabs.SelectedIndex = 1;
                            dialog.Width += 80;
                            dialog.Height += 60;
                            tabs.SelectedIndex = 0;
                            var transparency = (CheckBox)dialog.Controls.Find("chkTransparentBackgroundForCapture", true)[0];
                            int previousUpdates = transparencyUpdates;
                            transparency.Checked = !transparency.Checked;
                            Assert.Equal(previousUpdates + 1, transparencyUpdates);
                            tabs.SelectedIndex = 1;
                            component.Value = 99;
                            lc.FontOverrides.OverrideTextFont = true;
                            var button = (Button)dialog.Controls.Find(cancel ? "btnCancel" : "btnOK", true)[0];
                            button.PerformClick();
                        }
                        catch (Exception ex)
                        {
                            failure = ex;
                            dialog.DialogResult = DialogResult.Abort;
                        }
                    }));
                    Assert.Equal(cancel ? DialogResult.Cancel : DialogResult.OK, dialog.ShowDialog());
                    if (failure != null)
                    {
                        throw failure;
                    }
                }

                Assert.Equal(cancel ? 10 : 99, component.Value);
                Assert.Equal(!cancel, lc.FontOverrides.OverrideTextFont);
                Assert.False(component.Settings.IsDisposed);
                Assert.Null(component.Settings.Parent);
            }

            component.Settings.Dispose();
            component.Dispose();
        });
    }

    [Fact]
    public void LoadOnlyTheSelectedTabAndKeepItsControlsOnRevisit()
    {
        OnStaThread(() =>
        {
            using var first = new FontSettingsComponent();
            using var second = new SettingsComponent();
            var layout = CreateLayout(first);
            layout.LayoutComponents.Add(new LayoutComponent("second", second));
            using (var dialog = new TestDialog(layout, first))
            {
                var tabs = (TabControl)dialog.Controls.Find("tabControl", true)[0];
                Assert.Equal(1, tabs.SelectedIndex);
                Assert.Empty(tabs.TabPages[0].Controls);
                Assert.Empty(tabs.TabPages[2].Controls);
                Assert.NotNull(first.Settings.Parent);
                Assert.Null(second.Settings.Parent);
                dialog.Show();
                Application.DoEvents();
                Control originalParent = first.Settings.Parent;
                tabs.SelectedIndex = 2;
                Application.DoEvents();
                Assert.NotNull(second.Settings.Parent);
                tabs.SelectedIndex = 1;
                Application.DoEvents();
                Assert.Same(originalParent, first.Settings.Parent);
                Assert.Single(tabs.TabPages[1].Controls.Cast<Control>());
                var fonts = originalParent.Controls.OfType<FontOverridePanel>().Single();
                Assert.True(first.Settings.Top >= fonts.Bottom);
                tabs.SelectedIndex = 0;
                Application.DoEvents();
                Assert.Single(dialog.Controls.Find("chkTransparentBackgroundForCapture", true));
                dialog.Close();
            }

            Assert.False(first.Settings.IsDisposed);
            Assert.False(second.Settings.IsDisposed);
            Assert.Null(first.Settings.Parent);
            Assert.Null(second.Settings.Parent);
            first.Settings.Dispose();
            second.Settings.Dispose();
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndDispose(Layout layout, bool visitComponent)
    {
        var dialog = new TestDialog(layout, visitComponent ? layout.LayoutComponents[0].Component : null);
        var reference = new WeakReference(dialog);
        dialog.Dispose();
        dialog.Dispose(); // Cleanup must be idempotent.
        return reference;
    }

    private static Layout CreateLayout(SettingsComponent component)
    {
        var layout = new Layout { Settings = new StandardLayoutSettingsFactory().Create() };
        layout.LayoutComponents.Add(new LayoutComponent("test", component));
        return layout;
    }

    private static int HandlerCount(Control control, string keyName)
    {
        var events = (EventHandlerList)typeof(Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(control);
        object key = typeof(Control).GetField(keyName, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return events[key]?.GetInvocationList().Length ?? 0;
    }

    private static void OnStaThread(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Dialog test timed out.");
        if (failure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class TestDialog : LayoutSettingsDialog
    {
        public TestDialog(Layout layout, IComponent selected = null) : base(layout.Settings, layout, selected)
        {
            ShowInTaskbar = false;
            Opacity = 0;
        }

        // Do not persist test window sizes to the user's registry.
        protected override void OnFormClosing(FormClosingEventArgs e) { }
    }

    private class SettingsComponent : SeparatorComponent, IComponent
    {
        public Panel Settings { get; } = new Panel { Size = new Size(300, 200) };
        public int Value { get; set; }
        Control IComponent.GetSettingsControl(LayoutMode mode) => Settings;
        XmlNode IComponent.GetSettings(XmlDocument document)
        {
            var element = document.CreateElement("TestSettings");
            element.InnerText = Value.ToString();
            return element;
        }
        void IComponent.SetSettings(XmlNode settings) => Value = int.Parse(settings.InnerText);
    }

    [GlobalFontConsumer(GlobalFont.TextFont)]
    private sealed class FontSettingsComponent : SettingsComponent { }
}
