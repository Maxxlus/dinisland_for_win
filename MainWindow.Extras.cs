using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland
{
    public class ShelfItem { public string FullPath { get; set; } public string Name { get; set; } }

    public partial class MainWindow
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);

        bool pinned, dragOver, dragOut, peeking, hasArt, micOn, camOn, tRun, tSw, lastMute;
        string lastTrack, dragPath;
        float lastVol = -1, lvl;
        System.Windows.Forms.PowerLineStatus? lastLine;
        double phase;
        int barTick;
        TimeSpan tSet = TimeSpan.FromMinutes(5), tAcc;
        DateTime tT0, wxStamp = DateTime.MinValue;
        DateTime calMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        Point? dragStart;
        readonly DispatcherTimer peekTimer = new();
        readonly DispatcherTimer tmrTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer fastTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
        readonly DispatcherTimer barTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
        readonly ObservableCollection<ShelfItem> shelf = new();
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        void InitExtras()
        {
            foreach (var pn in panels) TT(pn);
            TT(TabPill);
            Island.PreviewMouseLeftButtonDown += Island_PreviewDown;
            Island.PreviewMouseMove += Island_PreviewMove;
            Island.PreviewMouseLeftButtonUp += Island_PreviewUp;
            seekTimer.Tick += (s, e) => { if (shade) ShadeSync(false); };
            BuildVpnChips();
            SwitchTab(0);
            ShelfList.ItemsSource = shelf;
            foreach (var f in cfg.Shelf.ToList())
                if (File.Exists(f) || Directory.Exists(f))
                    shelf.Add(new ShelfItem { FullPath = f, Name = Path.GetFileName(f.TrimEnd('\\', '/')) });
            ShelfHint.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            peekTimer.Tick += (s, e) => EndPeek();
            fastTimer.Tick += (s, e) => CheckVolume();
            barTimer.Tick += (s, e) => TickBars();
            tmrTimer.Tick += (s, e) => TickTimer();
            fastTimer.Start();

            var slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            slow.Tick += (s, e) => { CheckPower(); CheckPrivacy(); if ((++slowN & 1) == 0) RefreshVpn(); FunTick(); };
            slow.Start();
            CheckPower(); CheckPrivacy();

            Deactivated += (s, e) => pinned = false;
            BuildCalendar(); ShowTimer();
            InitSettings();
        }

        System.Drawing.Icon AppIcon()
        {
            try { return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
            catch { return System.Drawing.SystemIcons.Application; }
        }

        void OnTabSelected(int n)
        {
            if (n == 3) ShowTimer();
            if (n == 5) { calMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1); BuildCalendar(); _ = FetchWeather(); }
        }

        void Hotkey()
        {
            if (expanded) { pinned = false; Collapse(); }
            else { pinned = true; Expand(); Activate(); }
        }

        // ---------- «Пик»: короткие всплывающие активности ----------
        void ShowPeek(string glyph, string l1, string l2, string right, double prog,
                      ImageSource art = null, string font = null, int ms = 2800, bool force = false)
        {
            if (expanded) return;
            if (!force && !cfg.Peeks) return;
            PkGlyph.Text = glyph ?? "";
            PkGlyph.FontFamily = font == null ? (FontFamily)FindResource("Icons") : new FontFamily(font);
            PkGlyph.Visibility = art == null ? Visibility.Visible : Visibility.Collapsed;
            if (art == null) PkArt.SetResourceReference(Border.BackgroundProperty, "Card");
            else PkArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            PkL1.Text = l1 ?? ""; PkL2.Text = l2 ?? "";
            PkL2.Visibility = string.IsNullOrEmpty(l2) ? Visibility.Collapsed : Visibility.Visible;
            PkRight.Text = right ?? "";
            if (prog >= 0)
            {
                PkProg.Visibility = Visibility.Visible; PeekBox.UpdateLayout();
                PkFill.Width = Math.Clamp(prog, 0, 1) * PkProg.ActualWidth;
            }
            else PkProg.Visibility = Visibility.Collapsed;

            peekTimer.Stop(); peekTimer.Interval = TimeSpan.FromMilliseconds(ms); peekTimer.Start();
            if (peeking) return;
            peeking = true; shrinkTimer.Stop();
            Width = 360; Height = 66; CenterTop();
            var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(420);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(330, d) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(56, d) { EasingFunction = ease });
            Mini.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            PeekBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        }

        void EndPeek()
        {
            peekTimer.Stop();
            if (!peeking) return;
            peeking = false;
            if (expanded) return;
            var d = TimeSpan.FromMilliseconds(280);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(200, d) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(40, d) { EasingFunction = ease });
            PeekBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            Mini.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { BeginTime = TimeSpan.FromMilliseconds(140) });
            shrinkTimer.Start();
        }

        void EndPeekForExpand()
        {
            if (!peeking) return;
            peeking = false; peekTimer.Stop();
            PeekBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        }

        // ---------- обложка: цвет, свечение, пик при смене трека ----------
        static Color Dominant(BitmapSource src)
        {
            var small = new TransformedBitmap(src, new ScaleTransform(16.0 / Math.Max(1, src.PixelWidth), 16.0 / Math.Max(1, src.PixelHeight)));
            var fc = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = fc.PixelWidth, h = fc.PixelHeight;
            var px = new byte[w * h * 4];
            fc.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0, wt = 0.0001;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                double B = px[i], G = px[i + 1], R = px[i + 2];
                double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                double sat = (mx - mn) / (mx + 1);
                double k = 0.03 + sat * sat * (mx / 255.0);
                r += R * k; g += G * k; b += B * k; wt += k;
            }
            r /= wt; g /= wt; b /= wt;
            double top = Math.Max(r, Math.Max(g, b));
            if (top > 0 && top < 150) { double f = 150 / top; r = Math.Min(255, r * f); g = Math.Min(255, g * f); b = Math.Min(255, b * f); }
            return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }

        void OnTrackArt(BitmapImage img)
        {
            MiniArt.Background = img == null ? null : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
            MiniArt.Visibility = img == null ? Visibility.Collapsed : Visibility.Visible;
            hasArt = img != null;
            if (img != null)
            {
                Color c;
                try { c = Dominant(img); } catch { c = Color.FromRgb(10, 132, 255); }
                Resources["ArtColor"] = new SolidColorBrush(c);
                Glow.Background = new RadialGradientBrush
                {
                    Center = new Point(0.22, 0.4), GradientOrigin = new Point(0.22, 0.4), RadiusX = 0.6, RadiusY = 0.9,
                    GradientStops = { new GradientStop(Color.FromArgb(0x70, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) }
                };
            }
            else
            {
                Resources["ArtColor"] = Resources["Accent"];
                Glow.Background = null;
            }
            if (ses == null) return;
            var key = ArtistText.Text + "|" + TitleText.Text;
            if (key != lastTrack)
            {
                bool first = lastTrack == null;
                lastTrack = key;
                if (!first) ShowPeek("\uE8D6", TitleText.Text, ArtistText.Text, "", -1, img, null, 3200);
            }
            else if (peeking && img != null) PkArt.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }

        void OnClipPeek(string t)
        {
            var one = t.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (one.Length > 40) one = one.Substring(0, 40) + "…";
            ShowPeek("\uE77F", "Скопировано", one, "", -1, null, null, 1800);
        }

        // ---------- эквалайзер, реагирующий на звук ----------
        void SetBarsRunning(bool on)
        {
            if (on) barTimer.Start();
            else { barTimer.Stop(); foreach (var b in bars) b.ScaleY = 0.3; }
        }

        void TickBars()
        {
            if (expanded) return;
            if (++barTick % 20 == 0) AppVolume.Ensure(Aumid());
            float p = AppVolume.Has ? AppVolume.Peak() : -1;
            if (p < 0) p = Audio.Peak();
            if (p < 0) p = 0.5f;
            lvl = lvl * 0.55f + p * 0.45f;
            phase += 0.35;
            for (int i = 0; i < bars.Count; i++)
            {
                double w = 0.55 + 0.45 * Math.Sin(phase * (1 + i * 0.37) + i * 1.7);
                bars[i].ScaleY = 0.22 + 0.78 * Math.Min(1.0, Math.Sqrt(Math.Max(0, lvl)) * 1.6 * w);
            }
        }

        // ---------- громкость, питание, приватность ----------
        void CheckVolume()
        {
            float v = Audio.Get(); bool m = Audio.IsMuted();
            if (lastVol < 0) { lastVol = v; lastMute = m; return; }
            bool ch = Math.Abs(v - lastVol) > 0.004f || m != lastMute;
            lastVol = v; lastMute = m;
            if (ch && !expanded)
                ShowPeek((m || v < 0.01f) ? "\uE74F" : "\uE767", "Громкость", "", Math.Round(v * 100) + "%", m ? 0 : v, null, null, 1400);
        }

        void CheckPower()
        {
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                bool hasBat = !ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
                BatBox.Visibility = hasBat ? Visibility.Visible : Visibility.Collapsed;
                if (!hasBat) return;
                int pct = (int)Math.Round(Math.Min(1f, Math.Max(0f, ps.BatteryLifePercent)) * 100);
                bool ac = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                BatText.Text = pct + "%"; BatFill.Width = 16.0 * pct / 100.0;
                BatBolt.Visibility = ac ? Visibility.Visible : Visibility.Collapsed;
                if (lastLine != null && lastLine != ps.PowerLineStatus)
                    ShowPeek(ac ? "\u26A1" : "\uE7E8", ac ? "Подключено к питанию" : "Работа от батареи", pct + "%", "", pct / 100.0,
                             null, ac ? "Segoe UI Symbol" : null, 3000);
                if (!ac && pct <= 15 && !lowShown)
                {
                    lowShown = true;
                    ShowPeek("\u26A0", "Низкий заряд", pct + "%", "", pct / 100.0, null, "Segoe UI Symbol", 5000, true);
                }
                if (ac || pct > 25) lowShown = false;
                lastLine = ps.PowerLineStatus;
            }
            catch { }
        }

        static bool ScanUse(RegistryKey k)
        {
            foreach (var n in k.GetSubKeyNames())
            {
                using var s = k.OpenSubKey(n);
                if (s == null) continue;
                var start = s.GetValue("LastUsedTimeStart"); var stop = s.GetValue("LastUsedTimeStop");
                if (start is long a && a > 0 && stop is long b && b == 0) return true;
                if (n == "NonPackaged" && ScanUse(s)) return true;
            }
            return false;
        }

        static bool InUse(string cap)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap);
                return k != null && ScanUse(k);
            }
            catch { return false; }
        }

        void CheckPrivacy()
        {
            bool m = InUse("microphone"), c = InUse("webcam");
            if (m != micOn)
            {
                micOn = m; MicDot.Visibility = m ? Visibility.Visible : Visibility.Collapsed;
                if (m) ShowPeek("\uE720", "Микрофон используется", "", "", -1, null, null, 2200);
            }
            if (c != camOn)
            {
                camOn = c; CamDot.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
                if (c) ShowPeek("\uE714", "Камера используется", "", "", -1, null, null, 2200);
            }
        }

        // ---------- таймер / секундомер ----------
        static string Fmt3(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        TimeSpan TElapsed() => tAcc + (tRun ? DateTime.Now - tT0 : TimeSpan.Zero);

        void ShowTimer()
        {
            var e = TElapsed();
            TimeSpan shown = tSw ? TimeSpan.FromSeconds(Math.Floor(e.TotalSeconds))
                                 : TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, (tSet - e).TotalSeconds)));
            TimerBig.Text = Fmt3(shown);
            double frac = tSw ? (e.TotalSeconds % 60) / 60.0 : (tSet.TotalSeconds > 0 ? e.TotalSeconds / tSet.TotalSeconds : 0);
            TFill.Width = Math.Clamp(frac, 0, 1) * TProg.Width;
            TStart.Content = tRun ? "\uE769" : "\uE768";
            if (tRun || tAcc > TimeSpan.Zero) { TimerMini.Text = Fmt3(shown); TimerMini.Visibility = Visibility.Visible; }
            else TimerMini.Visibility = Visibility.Collapsed;
            if (shade) ShTimer.Text = (tRun || tAcc > TimeSpan.Zero) ? TimerBig.Text : "Старт";
        }

        void TickTimer()
        {
            ShowTimer();
            if (!tSw && TElapsed() >= tSet) FinishTimer();
        }

        void FinishTimer()
        {
            tRun = false; tAcc = TimeSpan.Zero; tmrTimer.Stop(); ShowTimer();
            System.Media.SystemSounds.Exclamation.Play();
            ShowPeek("\uE916", "Таймер", "Время вышло", "", -1, null, null, 6000, true);
        }

        void StartTimer(TimeSpan t)
        {
            tSw = false; tSet = t; tAcc = TimeSpan.Zero; tT0 = DateTime.Now; tRun = true; tmrTimer.Start(); ShowTimer();
        }

        void TStart_Click(object s, RoutedEventArgs e)
        {
            if (tRun) { tAcc += DateTime.Now - tT0; tRun = false; tmrTimer.Stop(); }
            else
            {
                if (!tSw && TElapsed() >= tSet) tAcc = TimeSpan.Zero;
                tT0 = DateTime.Now; tRun = true; tmrTimer.Start();
            }
            ShowTimer();
        }

        void TReset_Click(object s, RoutedEventArgs e) { tRun = false; tAcc = TimeSpan.Zero; tmrTimer.Stop(); ShowTimer(); }

        void TMode_Click(object s, RoutedEventArgs e)
        {
            var t = (string)((Button)s).Tag;
            tRun = false; tAcc = TimeSpan.Zero; tmrTimer.Stop();
            if (t == "sw") tSw = true;
            else { tSw = false; tSet = TimeSpan.FromMinutes(int.Parse(t.Substring(1))); }
            ShowTimer();
        }

        // ---------- полка файлов ----------
        void AddShelf(IEnumerable<string> paths)
        {
            foreach (var p in paths)
            {
                if (shelf.Any(x => x.FullPath == p)) continue;
                shelf.Add(new ShelfItem { FullPath = p, Name = Path.GetFileName(p.TrimEnd('\\', '/')) });
            }
            cfg.Shelf = shelf.Select(x => x.FullPath).ToList(); cfg.Save();
            ShelfHint.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Win_DragEnter(object s, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { dragOver = true; Expand(); SelectTab(4); e.Effects = DragDropEffects.Copy; }
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        void Win_DragOver(object s, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
            dragOver = ok; e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
        }

        void Win_DragLeave(object s, DragEventArgs e) { dragOver = false; }

        void Win_Drop(object s, DragEventArgs e)
        {
            dragOver = false;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f) AddShelf(f);
            e.Handled = true;
        }

        void Shelf_Down(object s, MouseButtonEventArgs e) { dragStart = e.GetPosition(null); dragPath = (string)((Button)s).Tag; }

        void Shelf_Move(object s, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragStart == null) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(p.Y - dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null; dragOut = true;
            try { DragDrop.DoDragDrop((DependencyObject)s, new DataObject(DataFormats.FileDrop, new[] { dragPath }), DragDropEffects.Copy | DragDropEffects.Move); }
            catch { }
            dragOut = false;
        }

        void Shelf_Click(object s, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo((string)((Button)s).Tag) { UseShellExecute = true }); }
            catch { }
        }

        void Shelf_Remove(object s, MouseButtonEventArgs e)
        {
            var p = (string)((Button)s).Tag;
            var it = shelf.FirstOrDefault(x => x.FullPath == p);
            if (it != null) shelf.Remove(it);
            cfg.Shelf = shelf.Select(x => x.FullPath).ToList(); cfg.Save();
            ShelfHint.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- календарь и погода ----------
        void CalPrev_Click(object s, RoutedEventArgs e) { calMonth = calMonth.AddMonths(-1); BuildCalendar(); }
        void CalNext_Click(object s, RoutedEventArgs e) { calMonth = calMonth.AddMonths(1); BuildCalendar(); }

        UIElement Cell(string t, bool head, bool today)
        {
            var tb = new TextBlock { Text = t, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, FontWeight = today ? FontWeights.SemiBold : FontWeights.Normal };
            if (today) tb.Foreground = Brushes.White;
            else tb.SetResourceReference(TextBlock.ForegroundProperty, head ? "Sub" : "Fg");
            var b = new Border { Child = tb, Width = 22, Height = 19, CornerRadius = new CornerRadius(9.5),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (today) b.SetResourceReference(Border.BackgroundProperty, "Accent");
            return b;
        }

        void BuildCalendar()
        {
            CalGrid.Children.Clear();
            var name = Ru.DateTimeFormat.GetMonthName(calMonth.Month);
            CalTitle.Text = char.ToUpper(name[0]) + name.Substring(1) + " " + calMonth.Year;
            foreach (var d in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }) CalGrid.Children.Add(Cell(d, true, false));
            int off = ((int)calMonth.DayOfWeek + 6) % 7;
            for (int i = 0; i < off; i++) CalGrid.Children.Add(new Border());
            int n = DateTime.DaysInMonth(calMonth.Year, calMonth.Month);
            var td = DateTime.Today;
            for (int d = 1; d <= n; d++)
                CalGrid.Children.Add(Cell(d.ToString(), false, calMonth.Year == td.Year && calMonth.Month == td.Month && d == td.Day));
        }

        static (string, string) Wx(int c) => c switch
        {
            0 => ("☀", "Ясно"),
            1 or 2 => ("⛅", "Малооблачно"),
            3 => ("☁", "Пасмурно"),
            45 or 48 => ("🌫", "Туман"),
            >= 51 and <= 57 => ("🌦", "Морось"),
            >= 61 and <= 67 => ("🌧", "Дождь"),
            >= 71 and <= 77 => ("❄", "Снег"),
            >= 80 and <= 82 => ("🌧", "Ливень"),
            85 or 86 => ("🌨", "Снегопад"),
            >= 95 => ("⛈", "Гроза"),
            _ => ("🌡", "—")
        };

        async Task FetchWeather()
        {
            if (string.IsNullOrWhiteSpace(cfg.City))
            {
                WxIcon.Text = "🌍"; WxTemp.Text = "—"; WxDesc.Text = "Укажите город в настройках"; WxCity.Text = ""; return;
            }
            if ((DateTime.Now - wxStamp).TotalMinutes < 20) return;
            try
            {
                var g = await http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&name=" + Uri.EscapeDataString(cfg.City));
                using var gd = JsonDocument.Parse(g);
                var res = gd.RootElement.GetProperty("results")[0];
                double lat = res.GetProperty("latitude").GetDouble(), lon = res.GetProperty("longitude").GetDouble();
                string city = res.GetProperty("name").GetString();
                var w = await http.GetStringAsync(FormattableString.Invariant(
                    $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m&wind_speed_unit=ms"));
                using var wd = JsonDocument.Parse(w);
                var cur = wd.RootElement.GetProperty("current");
                double t = cur.GetProperty("temperature_2m").GetDouble(), wind = cur.GetProperty("wind_speed_10m").GetDouble();
                var (ic, ds) = Wx(cur.GetProperty("weather_code").GetInt32());
                WxIcon.Text = ic; WxTemp.Text = Math.Round(t) + "°";
                WxDesc.Text = ds + "\nВетер " + Math.Round(wind) + " м/с"; WxCity.Text = city; wxStamp = DateTime.Now; ShWx.Text = ic + " " + Math.Round(t) + "° " + city;
            }
            catch { WxDesc.Text = "Нет данных о погоде"; }
        }
    }
}
