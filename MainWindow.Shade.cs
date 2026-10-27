using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland
{
    public partial class MainWindow
    {
        bool shade, vpnOn, vpnBusy, vpnFirst = true, xPending, xDrag, shVolDrag;
        double xStartMouse, xStartLeft;
        int curTab = 0, slowN;

        static readonly (string Id, string Title, string[] Names, string Proc)[] Vpns =
        {
            ("v2raytun", "v2rayTun", new[] { "v2RayTun", "v2raytun" }, "v2raytun"),
            ("incy", "Incy", new[] { "Incy" }, "incy"),
            ("happ", "Happ", new[] { "Happ", "Happ - Proxy Utility", "Happ Proxy Utility" }, "happ"),
        };

        (string Id, string Title, string[] Names, string Proc) CurVpn()
        {
            foreach (var v in Vpns) if (v.Id == cfg.VpnProvider) return v;
            return Vpns[2];
        }

        static TranslateTransform TT(UIElement e)
        {
            if (e.RenderTransform is TranslateTransform t) return t;
            var n = new TranslateTransform(); e.RenderTransform = n; return n;
        }

        // ---------- плавное переключение вкладок (пружина / bounce) ----------
        void SwitchTab(int n)
        {
            int from = curTab;
            bool animate = expanded && Full.Opacity > 0.4 && n != from;
            for (int i = 0; i < panels.Length; i++)
            {
                var p = panels[i];
                p.BeginAnimation(OpacityProperty, null);
                var t = TT(p); t.BeginAnimation(TranslateTransform.XProperty, null); t.X = 0;
                p.Opacity = 1;
                if (!animate || i != from) p.Visibility = i == n ? Visibility.Visible : Visibility.Collapsed;
            }
            curTab = n;
            MovePill(n, animate);
            if (animate)
            {
                var outp = panels[from]; var inp = panels[n];
                int dir = n > from ? 1 : -1;
                var outT = TT(outp); var inT = TT(inp);
                inp.Opacity = 0;
                inT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 80, 0, TimeSpan.FromMilliseconds(620))
                    { EasingFunction = new BackEase { Amplitude = 1.0, EasingMode = EasingMode.EaseOut } });
                inp.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
                outT.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -dir * 60, TimeSpan.FromMilliseconds(240))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fade.Completed += (s, e) =>
                {
                    if (curTab == from) return;
                    outp.Visibility = Visibility.Collapsed;
                    outp.BeginAnimation(OpacityProperty, null); outp.Opacity = 1;
                    outT.BeginAnimation(TranslateTransform.XProperty, null); outT.X = 0;
                };
                outp.BeginAnimation(OpacityProperty, fade);
            }
            if (n == 2) { Activate(); Input.Focus(); }
            OnTabSelected(n);
        }

        void MovePill(int n, bool anim)
        {
            double x1 = 37.0 * n;
            var tt = TT(TabPill);
            if (!anim)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null); tt.X = x1;
                TabPill.BeginAnimation(WidthProperty, null); TabPill.Width = 34;
                return;
            }
            double dx = Math.Abs(x1 - tt.X);
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x1, TimeSpan.FromMilliseconds(560))
                { EasingFunction = new BackEase { Amplitude = 0.9, EasingMode = EasingMode.EaseOut } });
            var w = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(560) };
            w.KeyFrames.Add(new EasingDoubleKeyFrame(34 + Math.Min(dx * 0.5, 70), KeyTime.FromPercent(0.28), new CubicEase { EasingMode = EasingMode.EaseOut }));
            w.KeyFrames.Add(new EasingDoubleKeyFrame(34, KeyTime.FromPercent(1.0), new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }));
            TabPill.BeginAnimation(WidthProperty, w);
        }

        // ---------- шторка ----------
        void Clock_Click(object s, MouseButtonEventArgs e) { ToggleShade(); e.Handled = true; }
        void Grabber_Click(object s, MouseButtonEventArgs e) { CloseShade(); }

        void ToggleShade() { if (!expanded) return; if (shade) CloseShade(); else OpenShade(); }

        void OpenShade()
        {
            shade = true;
            Width = 460; Height = 460; CenterTop();
            ShadeSync(true);
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(438, TimeSpan.FromMilliseconds(560))
                { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } });
            TT(Shade).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-70, 0, TimeSpan.FromMilliseconds(560))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(80) });
            Shade.IsHitTestVisible = true;
        }

        void CloseShade()
        {
            if (!shade) return;
            shade = false; Shade.IsHitTestVisible = false;
            Shade.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
            var h = new DoubleAnimation(230, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            h.Completed += (s, e) => { if (expanded && !shade) { Height = 240; CenterTop(); } };
            Island.BeginAnimation(HeightProperty, h);
        }

        void CloseShadeInstant()
        {
            if (!shade) return;
            shade = false;
            Shade.BeginAnimation(OpacityProperty, null); Shade.Opacity = 0; Shade.IsHitTestVisible = false;
        }

        void SetTile(Button b, bool on)
        {
            if (on) { b.SetResourceReference(BackgroundProperty, "Accent"); b.Foreground = Brushes.White; }
            else { b.SetResourceReference(BackgroundProperty, "Card"); b.SetResourceReference(ForegroundProperty, "Fg"); }
        }

        void ShadeSync(bool full)
        {
            ThemeSub.Text = cfg.Active;
            SetTile(FocusTile, !cfg.Peeks); PeekChk.IsChecked = cfg.Peeks; FocusSub.Text = cfg.Peeks ? "Выкл" : "Вкл";
            bool muted = VolMuted();
            SetTile(MuteTile, muted); MuteSub.Text = muted ? "Выключен" : "Включён";
            if (!shVolDrag) ShVolFill.Width = VolGet() * ShVolTrack.ActualWidth;
            ShTitle.Text = TitleText.Text; ShArtist.Text = ArtistText.Text;
            ShArt.Background = Art.Background; ShPlay.Content = PlayIcon.Content;
            ShTimer.Text = (tRun || tAcc > TimeSpan.Zero) ? TimerBig.Text : "Старт";
            if (full) { RefreshVpn(); _ = FetchWeather(); }
        }

        void ThemeTile_Click(object s, RoutedEventArgs e)
        {
            Apply(cfg.Active == "Тёмная" ? Config.Builtin[1] : Config.Builtin[0]);
            ShadeSync(false);
        }

        void FocusTile_Click(object s, RoutedEventArgs e) { cfg.Peeks = !cfg.Peeks; cfg.Save(); ShadeSync(false); }
        void MuteTile_Click(object s, RoutedEventArgs e) { Mute_Click(s, e); ShadeSync(false); }
        void ShPlay_Click(object s, RoutedEventArgs e) { Play_Click(s, e); }
        void TimerTile_Click(object s, RoutedEventArgs e) { TStart_Click(s, e); ShadeSync(false); }

        void ShSetVol(double x)
        {
            if (ShVolTrack.ActualWidth <= 0) return;
            VolSet((float)Math.Clamp(x / ShVolTrack.ActualWidth, 0, 1));
            ShVolFill.Width = VolGet() * ShVolTrack.ActualWidth;
        }

        void Sh_Vol_Down(object s, MouseButtonEventArgs e) { shVolDrag = true; ShVolTrack.CaptureMouse(); ShSetVol(e.GetPosition(ShVolTrack).X); }
        void Sh_Vol_Move(object s, MouseEventArgs e) { if (shVolDrag) ShSetVol(e.GetPosition(ShVolTrack).X); }
        void Sh_Vol_Up(object s, MouseButtonEventArgs e) { shVolDrag = false; ShVolTrack.ReleaseMouseCapture(); }

        // ---------- перемещение по оси X ----------
        static bool Interactive(DependencyObject d, DependencyObject stop)
        {
            while (d != null && d != stop)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase || d is System.Windows.Controls.Primitives.TextBoxBase ||
                    d is System.Windows.Controls.Primitives.ScrollBar || d is System.Windows.Controls.Primitives.Thumb) return true;
                if (d is FrameworkElement fe && (fe.Name == "SeekTrack" || fe.Name == "VolV" || fe.Name == "ShVolTrack")) return true;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        void Island_PreviewDown(object s, MouseButtonEventArgs e)
        {
            if (cfg.Static || Interactive(e.OriginalSource as DependencyObject, Island)) return;
            xPending = true; xStartMouse = System.Windows.Forms.Control.MousePosition.X; xStartLeft = Left;
        }

        void Island_PreviewMove(object s, MouseEventArgs e)
        {
            if (!xPending) return;
            if (e.LeftButton != MouseButtonState.Pressed) { if (xDrag) EndXDrag(); xPending = false; return; }
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double dx = (System.Windows.Forms.Control.MousePosition.X - xStartMouse) / dpi;
            if (!xDrag) { if (Math.Abs(dx) < 4) return; xDrag = true; Island.CaptureMouse(); }
            double sw = SystemParameters.PrimaryScreenWidth;
            Left = Math.Clamp(xStartLeft + dx, 0, Math.Max(0, sw - Width));
        }

        void Island_PreviewUp(object s, MouseButtonEventArgs e) { if (xDrag) EndXDrag(); xPending = false; }

        void EndXDrag()
        {
            xDrag = false; Island.ReleaseMouseCapture();
            cfg.OffsetX = Left + Width / 2 - SystemParameters.PrimaryScreenWidth / 2;
            cfg.Save();
        }

        // ---------- VPN (v2rayTun / Incy / Happ) ----------
        static List<System.Diagnostics.Process> FindProcs(string key)
        {
            var l = new List<System.Diagnostics.Process>();
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    bool ok = false;
                    try { ok = p.ProcessName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0; } catch { }
                    if (ok) l.Add(p); else p.Dispose();
                }
            }
            catch { }
            return l;
        }

        static bool TunUp()
        {
            try
            {
                foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var s = (n.Name + " " + n.Description).ToLowerInvariant();
                    if (s.Contains("wintun") || s.Contains("v2ray") || s.Contains("xray") || s.Contains("sing-box") || s.Contains("singbox") ||
                        s.Contains("happ") || s.Contains("incy") || n.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        static bool ProxyOn()
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                return k != null && Convert.ToInt32(k.GetValue("ProxyEnable", 0)) == 1 &&
                       ((k.GetValue("ProxyServer") as string) ?? "").Contains("127.0.0.1");
            }
            catch { return false; }
        }

        void RefreshVpn()
        {
            var v = CurVpn();
            bool prev = vpnOn;
            var procs = FindProcs(v.Proc);
            vpnOn = procs.Count > 0 && (TunUp() || ProxyOn());
            foreach (var p in procs) p.Dispose();
            SetTile(VpnTile, vpnOn);
            if (!vpnBusy) VpnSub.Text = v.Title + (vpnOn ? " · вкл" : " · выкл");
            if (!vpnFirst && prev != vpnOn) ShowPeek("\uE774", vpnOn ? "VPN подключён" : "VPN отключён", v.Title, "", -1);
            vpnFirst = false;
        }

        static bool TryUia(System.Diagnostics.Process p, bool want)
        {
            try
            {
                p.Refresh();
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) return false;
                var root = System.Windows.Automation.AutomationElement.FromHandle(h);
                var cond = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.IsInvokePatternAvailableProperty, true);
                string[] onW = { "connect", "подключ", "включ", "turn on", "start", "старт", "запуст" };
                string[] offW = { "disconnect", "отключ", "выключ", "turn off", "stop", "стоп", "останов" };
                foreach (System.Windows.Automation.AutomationElement el in root.FindAll(System.Windows.Automation.TreeScope.Descendants, cond))
                {
                    var name = (el.Current.Name ?? "").ToLowerInvariant();
                    if (name.Length == 0) continue;
                    bool isOff = offW.Any(w => name.Contains(w));
                    bool isOn = !isOff && onW.Any(w => name.Contains(w));
                    if ((want && isOn) || (!want && isOff))
                    {
                        ((System.Windows.Automation.InvokePattern)el.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        static void RunShell(string cmd)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + cmd) { CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        }

        async Task VpnToggle()
        {
            if (vpnBusy) return;
            vpnBusy = true;
            try
            {
                var v = CurVpn(); bool want = !vpnOn;
                VpnSub.Text = want ? "Подключаю…" : "Отключаю…";
                string cmd = want ? cfg.VpnConnectCmd : cfg.VpnDisconnectCmd;
                if (!string.IsNullOrWhiteSpace(cmd)) RunShell(cmd);
                else
                {
                    var procs = FindProcs(v.Proc);
                    if (procs.Count == 0)
                    {
                        var found = await LaunchApp(v.Names.ToList());
                        if (found == null) { VpnSub.Text = v.Title + " не найден"; return; }
                        await Task.Delay(4000);
                        procs = FindProcs(v.Proc);
                    }
                    bool done = await Task.Run(() => procs.Any(p => TryUia(p, want)));
                    if (!done)
                    {
                        await LaunchApp(v.Names.ToList());
                        await Task.Delay(1500);
                        procs = FindProcs(v.Proc);
                        done = await Task.Run(() => procs.Any(p => TryUia(p, want)));
                    }
                    if (!done) VpnSub.Text = "Нажмите в " + v.Title;
                }
                await Task.Delay(2000);
            }
            finally { vpnBusy = false; }
            RefreshVpn();
        }

        async void VpnTile_Click(object s, RoutedEventArgs e) { await VpnToggle(); }

        void VpnTile_Right(object s, MouseButtonEventArgs e)
        {
            int i = Array.FindIndex(Vpns, v => v.Id == cfg.VpnProvider);
            cfg.VpnProvider = Vpns[(i + 1) % Vpns.Length].Id; cfg.Save();
            BuildVpnChips(); RefreshVpn();
        }

        void BuildVpnChips()
        {
            VpnChips.Children.Clear();
            foreach (var v in Vpns)
            {
                var id = v.Id;
                var b = new Button { Content = v.Title, Style = (Style)FindResource("Flat"), Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 0, 6, 0), FontSize = 12 };
                if (cfg.VpnProvider == id) { b.SetResourceReference(BackgroundProperty, "Accent"); b.Foreground = Brushes.White; }
                else b.SetResourceReference(BackgroundProperty, "Card");
                b.Click += (s, e) => { cfg.VpnProvider = id; cfg.Save(); BuildVpnChips(); RefreshVpn(); };
                VpnChips.Children.Add(b);
            }
        }
    }
}
