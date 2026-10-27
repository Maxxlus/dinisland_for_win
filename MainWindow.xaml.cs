using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;

namespace DynamicIsland
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr h);

        const string SysPrompt = """
Ты ассистент, встроенный в Dynamic Island на Windows. Отвечай кратко и по-русски.
Если нужно выполнить действие, ответь ТОЛЬКО одним JSON без пояснений и без markdown. Варианты:
1) Сменить/создать тему оформления:
{"action":"set_theme","name":"короткое название","bg1":"#RRGGBB","bg2":"#RRGGBB","fg":"#RRGGBB","accent":"#RRGGBB","message":"короткий комментарий"}
bg1 и bg2 — цвета градиента фона острова, fg — цвет текста (обязателен хороший контраст с фоном), accent — акцентный цвет.
2) Включить трек, клип или видео на YouTube (приложение само найдёт и откроет первое подходящее видео):
{"action":"play","query":"исполнитель и название","message":"короткий комментарий"}
3) Открыть сайт или ссылку, в том числе поиск на других сервисах (Яндекс Музыка, Spotify, Google, карты и т.д.) — составь корректный URL:
{"action":"open_url","url":"https://...","message":"короткий комментарий"}
4) Запустить приложение на компьютере (из меню Пуск). Перечисли 1-3 вероятных названия так, как они могут называться в меню Пуск (русское и английское):
{"action":"open_app","names":["Калькулятор","Calculator"],"message":"короткий комментарий"}
5) Поставить таймер:
{"action":"timer","minutes":10,"message":"короткий комментарий"}
6) Управление текущей музыкой (command: next, prev, pause, play, toggle):
{"action":"media","command":"next","message":"короткий комментарий"}
7) Громкость текущего плеера от 0 до 100:
{"action":"volume","level":40,"message":"короткий комментарий"}
Для ссылок разрешены только http и https. На обычные вопросы отвечай обычным текстом.
""";

        static readonly HttpClient http = new(new FallbackHandler()) { Timeout = TimeSpan.FromSeconds(60) };
        readonly Config cfg = Config.Load();
        readonly ObservableCollection<string> clips = new();
        readonly List<object> history = new();
        readonly List<ScaleTransform> bars = new();
        readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        readonly DispatcherTimer shrinkTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
        UIElement[] panels;
        Button[] tabs;
        bool expanded, busy, barsOn, seekDrag, volDrag;
        double seekRatio;
        DateTime seekHold = DateTime.MinValue;
        readonly DispatcherTimer seekTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer volHide = new() { Interval = TimeSpan.FromMilliseconds(350) };
        List<StartApp> startApps;
        GlobalSystemMediaTransportControlsSessionManager mgr;
        GlobalSystemMediaTransportControlsSession ses;
        System.Windows.Forms.NotifyIcon tray;

        public MainWindow()
        {
            InitializeComponent();
            panels = new UIElement[] { P0, P1, P2, P3, P4, P5, P6 };
            tabs = FindTabs();
            ClipList.ItemsSource = clips;

            for (int i = 0; i < 3; i++)
            {
                var st = new ScaleTransform(1, 0.3);
                var r = new System.Windows.Shapes.Rectangle { Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5,
                    Margin = new Thickness(1, 0, 1, 0), RenderTransformOrigin = new Point(0.5, 1), RenderTransform = st };
                r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ArtColor");
                Bars.Children.Add(r); bars.Add(st);
            }

            KeyBox.Text = cfg.ApiKey; ModelBox.Text = cfg.Model; CityBox.Text = cfg.City;
            RefreshThemes();
            Apply(cfg.Themes.FirstOrDefault(t => t.Name == cfg.Active) ?? Config.Builtin[0]);
            SelectTab(0);

            var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (s, e) => { var t = DateTime.Now.ToString("HH:mm"); ClockMini.Text = t; ClockBig.Text = t; ClockBig.ToolTip = DateTime.Now.ToString("dddd, d MMMM", Ru); };
            clock.Start(); ClockMini.Text = ClockBig.Text = DateTime.Now.ToString("HH:mm");

            hoverTimer.Tick += (s, e) => { if (!Island.IsMouseOver && !IsKeyboardFocusWithin && !pinned && !dragOver && !dragOut) Collapse(); };
            shrinkTimer.Tick += (s, e) => { shrinkTimer.Stop(); if (!expanded && !peeking) { Width = 220; Height = 48; CenterTop(); } };

            seekTimer.Tick += (s, e) => UpdateSeek();
            volHide.Tick += (s, e) =>
            {
                if (VolIcon.IsMouseOver || VolPop.IsMouseOver || volDrag) return;
                volHide.Stop(); VolPop.Visibility = Visibility.Collapsed;
            };
            SourceInitialized += (s, e) =>
            {
                var src = (HwndSource)PresentationSource.FromVisual(this);
                SetWindowLong(src.Handle, -20, GetWindowLong(src.Handle, -20) | 0x80); // WS_EX_TOOLWINDOW
                src.AddHook(WndProc);
                AddClipboardFormatListener(src.Handle);
                RegisterHotKey(src.Handle, 1, 0x4003, 0x49); // Ctrl+Alt+I
            };
            Loaded += (s, e) => { CenterTop(); InitMedia(); };
            SetupTray();
            SetAutostart(cfg.Autostart);
            InitExtras();
        }

        Button[] FindTabs()
        {
            var list = new List<Button>();
            void Walk(DependencyObject d)
            {
                foreach (var c in LogicalTreeHelper.GetChildren(d))
                {
                    if (c is Button b && b.Tag is string tg && tg.Length == 1 && "0123456".Contains(tg)) list.Add(b);
                    if (c is DependencyObject dd) Walk(dd);
                }
            }
            Walk(Full);
            return list.OrderBy(b => (string)b.Tag).ToArray();
        }

        // ---------- окно / анимация ----------
        void CenterTop()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            Left = Math.Clamp((sw - Width) / 2 + (cfg.Static ? 0 : cfg.OffsetX), 0, Math.Max(0, sw - Width));
            Top = 6;
        }

        void Island_Enter(object s, MouseEventArgs e) => Expand();

        void Expand()
        {
            if (expanded) return;
            expanded = true; shrinkTimer.Stop(); EndPeekForExpand();
            SelectTab(0); Audio.Reset(); ShowVol(); seekTimer.Start(); UpdateSeek();
            Width = 460; Height = 240; CenterTop();
            Animate(440, 230, true);
            hoverTimer.Start();
        }

        void Collapse()
        {
            if (!expanded) return;
            expanded = false; hoverTimer.Stop(); seekTimer.Stop(); volHide.Stop(); VolPop.Visibility = Visibility.Collapsed; CloseShadeInstant();
            Animate(200, 40, false);
            shrinkTimer.Start();
        }

        void Animate(double w, double h, bool open)
        {
            IEasingFunction ease = open ? new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
                                        : new CubicEase { EasingMode = EasingMode.EaseInOut };
            var d = TimeSpan.FromMilliseconds(open ? 420 : 280);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, d) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, d) { EasingFunction = ease });
            Full.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(open ? 250 : 100))
                { BeginTime = TimeSpan.FromMilliseconds(open ? 120 : 0) });
            Mini.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, TimeSpan.FromMilliseconds(150))
                { BeginTime = TimeSpan.FromMilliseconds(open ? 0 : 130) });
            Full.IsHitTestVisible = open;
        }

        // ---------- вкладки ----------
        void Tab_Click(object s, RoutedEventArgs e) => SelectTab(int.Parse((string)((Button)s).Tag));

        void SelectTab(int n) => SwitchTab(n);

        // ---------- темы ----------
        static byte Mix(byte a, byte b) => (byte)(a * 0.84 + b * 0.16);
        static Color C(string s) => (Color)ColorConverter.ConvertFromString(s);

        void Apply(Theme t)
        {
            var c1 = C(t.Bg1); var c2 = C(t.Bg2); var fg = C(t.Fg); var ac = C(t.Accent);
            Island.Background = new LinearGradientBrush(c1, c2, 45);
            Resources["Fg"] = new SolidColorBrush(fg);
            Resources["Sub"] = new SolidColorBrush(Color.FromArgb(0xA0, fg.R, fg.G, fg.B));
            Resources["Card"] = new SolidColorBrush(Color.FromArgb(0x22, fg.R, fg.G, fg.B));
            Resources["Accent"] = new SolidColorBrush(ac);
            Resources["Field"] = new SolidColorBrush(c1);
            if (!hasArt) Resources["ArtColor"] = new SolidColorBrush(ac);
            Resources["Panel"] = new SolidColorBrush(Color.FromRgb(Mix(c1.R, fg.R), Mix(c1.G, fg.G), Mix(c1.B, fg.B)));
            cfg.Active = t.Name; cfg.Save();
        }

        void RefreshThemes()
        {
            ThemesPanel.Children.Clear();
            foreach (var t in cfg.Themes)
            {
                try
                {
                    var b = new Button { Content = t.Name, Style = (Style)FindResource("Flat"), Padding = new Thickness(12, 5, 12, 5),
                        Margin = new Thickness(0, 0, 6, 6), FontSize = 12, Foreground = new SolidColorBrush(C(t.Fg)),
                        Background = new LinearGradientBrush(C(t.Bg1), C(t.Bg2), 45) };
                    var th = t;
                    b.Click += (s, e) => Apply(th);
                    b.MouseRightButtonUp += (s, e) =>
                    {
                        if (Config.Builtin.Any(x => x.Name == th.Name)) return;
                        cfg.Themes.Remove(th); cfg.Save(); RefreshThemes();
                    };
                    ThemesPanel.Children.Add(b);
                }
                catch { }
            }
        }

        void Save_Click(object s, RoutedEventArgs e)
        {
            cfg.City = CityBox.Text.Trim(); wxStamp = DateTime.MinValue; cfg.ApiKey = KeyBox.Text.Trim(); cfg.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "openai/gpt-4o-mini" : ModelBox.Text.Trim();
            cfg.Save();
        }

        // ---------- медиа ----------
        async void InitMedia()
        {
            try
            {
                mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                mgr.CurrentSessionChanged += (s, e) => Dispatcher.Invoke(Hook);
                Hook();
            }
            catch { }
        }

        void Hook()
        {
            if (ses != null) { ses.MediaPropertiesChanged -= OnProps; ses.PlaybackInfoChanged -= OnPb; }
            ses = mgr.GetCurrentSession();
            if (ses != null) { ses.MediaPropertiesChanged += OnProps; ses.PlaybackInfoChanged += OnPb; }
            _ = UpdateMedia(); UpdatePb();
        }

        void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) => Dispatcher.InvokeAsync(() => UpdateMedia());
        void OnPb(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) => Dispatcher.InvokeAsync(UpdatePb);

        async Task UpdateMedia()
        {
            var s = ses;
            if (s == null) { TitleText.Text = "Ничего не играет"; ArtistText.Text = ""; Art.Background = null; RefreshLike(); OnTrackArt(null); return; }
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                TitleText.Text = string.IsNullOrEmpty(p.Title) ? "Без названия" : p.Title;
                ArtistText.Text = p.Artist ?? "";
                RefreshLike();
                BitmapImage img = null;
                if (p.Thumbnail != null)
                {
                    using var rs = await p.Thumbnail.OpenReadAsync();
                    using var st = rs.AsStreamForRead();
                    var ms = new MemoryStream(); await st.CopyToAsync(ms); ms.Position = 0;
                    img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
                }
                Art.Background = img == null ? null : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
                OnTrackArt(img);
            }
            catch { }
        }

        void UpdatePb()
        {
            bool has = ses != null, playing = false;
            if (has) { try { playing = ses.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { } }
            PlayIcon.Content = playing ? "\uE769" : "\uE768";
            Bars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
            PauseIcon.Visibility = has && !playing ? Visibility.Visible : Visibility.Collapsed;
            if (playing == barsOn) return;
            barsOn = playing; SetBarsRunning(playing);
        }

        async void Prev_Click(object s, RoutedEventArgs e) { if (ses != null) await ses.TrySkipPreviousAsync(); }
        async void Play_Click(object s, RoutedEventArgs e) { if (ses != null) await ses.TryTogglePlayPauseAsync(); }
        async void Next_Click(object s, RoutedEventArgs e) { if (ses != null) await ses.TrySkipNextAsync(); }

        // ---------- буфер обмена ----------
        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x031D) OnClip(); else if (msg == 0x0312) Hotkey();
            return IntPtr.Zero;
        }

        void OnClip()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                var t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                clips.Remove(t); clips.Insert(0, t); OnClipPeek(t);
                while (clips.Count > 25) clips.RemoveAt(clips.Count - 1);
            }
            catch { }
        }

        void Clip_Click(object s, RoutedEventArgs e)
        {
            try { Clipboard.SetText((string)((Button)s).Tag); } catch { }
        }

        // ---------- ИИ ----------
        TextBlock Add(string text, bool user)
        {
            var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            if (user) tb.Foreground = Brushes.White; else tb.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
            var b = new Border { Child = tb, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(user ? 40 : 0, 0, user ? 0 : 40, 5),
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            b.SetResourceReference(Border.BackgroundProperty, user ? "Accent" : "Card");
            ChatList.Children.Add(b); ChatScroll.ScrollToEnd();
            return tb;
        }

        async Task<string> Ask()
        {
            var msgs = new List<object> { new { role = "system", content = SysPrompt } };
            msgs.AddRange(history);
            var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
            { Content = new StringContent(JsonSerializer.Serialize(new { model = cfg.Model, messages = msgs }), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            req.Headers.Add("X-Title", "Dynamic Island");
            var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return ApiError((int)res.StatusCode, body);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }

        async void Input_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift) return;
            e.Handled = true;
            var q = Input.Text.Trim();
            if (q == "" || busy) return;
            if (HandleLocalCommand(q)) { Input.Clear(); return; }
            if (string.IsNullOrWhiteSpace(cfg.ApiKey)) { Add("Укажите API-ключ OpenRouter на вкладке настроек (шестерёнка).", false); return; }
            Input.Clear(); Add(q, true);
            history.Add(new { role = "user", content = q });
            var tb = Add("…", false); busy = true;
            string reply;
            try { reply = await Ask(); } catch (Exception ex) { reply = FriendlyNetError(ex); }
            busy = false;
            string shown = reply;
            int a = reply.IndexOf('{'), b = reply.LastIndexOf('}');
            if (a >= 0 && b > a)
            {
                try
                {
                    using var d = JsonDocument.Parse(reply.Substring(a, b - a + 1));
                    var r = d.RootElement;
                    var act = r.GetProperty("action").GetString();
                    string msg = r.TryGetProperty("message", out var m) ? m.GetString() : "";
                    if (act == "set_theme")
                    {
                        var t = new Theme { Name = r.GetProperty("name").GetString(), Bg1 = r.GetProperty("bg1").GetString(),
                            Bg2 = r.GetProperty("bg2").GetString(), Fg = r.GetProperty("fg").GetString(), Accent = r.GetProperty("accent").GetString() };
                        Apply(t);
                        cfg.Themes.RemoveAll(x => x.Name == t.Name && !Config.Builtin.Any(bb => bb.Name == x.Name));
                        if (!Config.Builtin.Any(bb => bb.Name == t.Name)) cfg.Themes.Add(t);
                        cfg.Save(); RefreshThemes();
                        shown = msg + "\nТема «" + t.Name + "» применена и добавлена в список.";
                    }
                    else if (act == "play")
                    {
                        var q2 = r.GetProperty("query").GetString();
                        tb.Text = "Ищу «" + q2 + "»…";
                        var url = await FindYoutube(q2);
                        shown = OpenUrl(url) ? msg + "\n" + url : "Не удалось открыть: " + url;
                    }
                    else if (act == "open_app")
                    {
                        var names = new List<string>();
                        if (r.TryGetProperty("names", out var ns) && ns.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var nm in ns.EnumerateArray()) names.Add(nm.GetString());
                        }
                        else if (r.TryGetProperty("name", out var n1)) { names.Add(n1.GetString()); }
                        tb.Text = "Запускаю…";
                        var found = await LaunchApp(names);
                        shown = found != null ? (msg + "\nЗапущено: " + found).Trim() : "Не нашёл в меню Пуск: " + string.Join(", ", names);
                    }
                    else if (act == "timer")
                    {
                        var mins = Math.Clamp(r.GetProperty("minutes").GetDouble(), 0.1, 600);
                        StartTimer(TimeSpan.FromMinutes(mins));
                        shown = (msg + "\nТаймер на " + Math.Round(mins, 1) + " мин. запущен.").Trim();
                    }
                    else if (act == "media")
                    {
                        var cmd = r.GetProperty("command").GetString();
                        if (ses == null) shown = "Сейчас ничего не играет.";
                        else
                        {
                            if (cmd == "next") await ses.TrySkipNextAsync();
                            else if (cmd == "prev") await ses.TrySkipPreviousAsync();
                            else if (cmd == "pause") await ses.TryPauseAsync();
                            else if (cmd == "play") await ses.TryPlayAsync();
                            else await ses.TryTogglePlayPauseAsync();
                            shown = (msg + "\nГотово.").Trim();
                        }
                    }
                    else if (act == "volume")
                    {
                        var lv = Math.Clamp(r.GetProperty("level").GetDouble(), 0, 100);
                        VolSet((float)(lv / 100.0));
                        shown = (msg + "\nГромкость плеера: " + (int)lv + "%").Trim();
                    }
                    else if (act == "open_url")
                    {
                        var url = r.GetProperty("url").GetString();
                        shown = OpenUrl(url) ? msg + "\n" + url : "Не удалось открыть ссылку: " + url;
                    }
                }
                catch { }
            }
            tb.Text = shown;
            history.Add(new { role = "assistant", content = reply });
            while (history.Count > 20) history.RemoveAt(0);
            ChatScroll.ScrollToEnd();
        }

        // ---------- громкость / лайки ----------
        string Aumid() { try { return ses?.SourceAppUserModelId; } catch { return null; } }
        bool PerApp() => AppVolume.Ensure(Aumid());
        float VolGet() => PerApp() ? AppVolume.Get() : Audio.Get();
        void VolSet(float v) { if (PerApp()) AppVolume.Set(v); else Audio.Set(v); }
        bool VolMuted() => PerApp() ? AppVolume.IsMuted() : Audio.IsMuted();

        void ShowVol()
        {
            float v = VolGet();
            VolFill.Height = v * VolV.ActualHeight;
            VolIcon.Content = (VolMuted() || v < 0.01f) ? "\uE74F" : "\uE767";
        }

        void VolBtn_Enter(object s, MouseEventArgs e)
        {
            AppVolume.Resolve(Aumid());
            var pt = VolIcon.TransformToVisual(Full).Transform(new Point(VolIcon.ActualWidth / 2, 0));
            VolPop.Margin = new Thickness(pt.X - VolPop.Width / 2, Math.Max(0, pt.Y - VolPop.Height - 2), 0, 0);
            VolPop.Visibility = Visibility.Visible; VolPop.UpdateLayout(); ShowVol();
            VolPop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            volHide.Start();
        }

        void SetVolY(double y)
        {
            if (VolV.ActualHeight <= 0) return;
            VolSet((float)Math.Clamp(1 - y / VolV.ActualHeight, 0, 1)); ShowVol();
        }

        void Vol_Down(object s, MouseButtonEventArgs e) { volDrag = true; VolV.CaptureMouse(); SetVolY(e.GetPosition(VolV).Y); }
        void Vol_Move(object s, MouseEventArgs e) { if (volDrag) SetVolY(e.GetPosition(VolV).Y); }
        void Vol_Up(object s, MouseButtonEventArgs e) { volDrag = false; VolV.ReleaseMouseCapture(); }
        void Vol_Wheel(object s, MouseWheelEventArgs e) { VolSet(VolGet() + (e.Delta > 0 ? 0.05f : -0.05f)); ShowVol(); }
        void Mute_Click(object s, RoutedEventArgs e) { if (PerApp()) AppVolume.ToggleMute(); else Audio.ToggleMute(); ShowVol(); }

        // ---------- перемотка ----------
        static string Fmt(double sec) => TimeSpan.FromSeconds(sec).ToString(sec >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

        void UpdateSeek()
        {
            if (seekDrag || DateTime.Now < seekHold || P0.Visibility != Visibility.Visible) return;
            if (ses == null) { SeekFill.Width = 0; TimeL.Text = TimeR.Text = ""; return; }
            try
            {
                var tl = ses.GetTimelineProperties();
                double dur = (tl.EndTime - tl.StartTime).TotalSeconds;
                if (dur <= 1) { SeekFill.Width = 0; TimeL.Text = TimeR.Text = ""; return; }
                double pos = (tl.Position - tl.StartTime).TotalSeconds;
                var info = ses.GetPlaybackInfo();
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    pos += (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds * (info.PlaybackRate ?? 1.0);
                pos = Math.Clamp(pos, 0, dur);
                SeekFill.Width = SeekTrack.ActualWidth * pos / dur;
                TimeL.Text = Fmt(pos); TimeR.Text = Fmt(dur);
            }
            catch { }
        }

        void SeekPreview(MouseEventArgs e)
        {
            if (SeekTrack.ActualWidth <= 0) return;
            seekRatio = Math.Clamp(e.GetPosition(SeekTrack).X / SeekTrack.ActualWidth, 0, 1);
            SeekFill.Width = seekRatio * SeekTrack.ActualWidth;
            try { var tl = ses.GetTimelineProperties(); TimeL.Text = Fmt(seekRatio * (tl.EndTime - tl.StartTime).TotalSeconds); } catch { }
        }

        void Seek_Down(object s, MouseButtonEventArgs e) { if (ses == null) return; seekDrag = true; SeekTrack.CaptureMouse(); SeekPreview(e); }
        void Seek_Move(object s, MouseEventArgs e) { if (seekDrag) SeekPreview(e); }

        async void Seek_Up(object s, MouseButtonEventArgs e)
        {
            if (!seekDrag) return;
            seekDrag = false; SeekTrack.ReleaseMouseCapture();
            seekHold = DateTime.Now.AddSeconds(1);
            try
            {
                var tl = ses.GetTimelineProperties();
                await ses.TryChangePlaybackPositionAsync(tl.StartTime.Ticks + (long)(seekRatio * (tl.EndTime - tl.StartTime).Ticks));
            }
            catch { }
        }

        string TrackKey() => ArtistText.Text + " — " + TitleText.Text;

        void RefreshLike()
        {
            bool liked = ses != null && cfg.Likes.Contains(TrackKey());
            LikeBtn.Content = liked ? "\u2665" : "\u2661";
            if (liked) LikeBtn.SetResourceReference(ForegroundProperty, "Accent");
            else LikeBtn.SetResourceReference(ForegroundProperty, "Fg");
        }

        void Like_Click(object s, RoutedEventArgs e)
        {
            if (ses == null) return;
            var k = TrackKey();
            bool added = !cfg.Likes.Remove(k);
            if (added) { cfg.Likes.Add(k); HeartPop(); }
            cfg.Save(); RefreshLike();
        }

        async void Dislike_Click(object s, RoutedEventArgs e)
        {
            if (ses == null) return;
            if (cfg.Likes.Remove(TrackKey())) cfg.Save();
            await ses.TrySkipNextAsync();
        }

        // ---------- ссылки ----------
        static bool OpenUrl(string u)
        {
            try
            {
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return false;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        static async Task<string> FindYoutube(string q)
        {
            var search = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q);
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, search);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                req.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
                req.Headers.Add("Cookie", "SOCS=CAI; CONSENT=YES+1");
                var html = await (await http.SendAsync(req)).Content.ReadAsStringAsync();
                var m = System.Text.RegularExpressions.Regex.Match(html, "\"videoRenderer\":\\{\"videoId\":\"([\\w-]{11})\"");
                if (m.Success) return "https://www.youtube.com/watch?v=" + m.Groups[1].Value;
            }
            catch { }
            return search;
        }

        // ---------- запуск приложений ----------
        static Task<List<StartApp>> LoadStartApps() => Task.Run(() =>
        {
            var list = new List<StartApp>();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                using var p = System.Diagnostics.Process.Start(psi);
                var outp = p.StandardOutput.ReadToEnd().TrimStart('\uFEFF');
                p.WaitForExit(15000);
                using var d = JsonDocument.Parse(outp);
                var root = d.RootElement;
                if (root.ValueKind == JsonValueKind.Object) { list.Add(new StartApp { Name = root.GetProperty("Name").GetString(), Id = root.GetProperty("AppID").GetString() }); }
                else foreach (var e in root.EnumerateArray())
                    list.Add(new StartApp { Name = e.GetProperty("Name").GetString(), Id = e.GetProperty("AppID").GetString() });
            }
            catch { }
            return list;
        });

        async Task<string> LaunchApp(List<string> names)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (startApps == null || startApps.Count == 0 || attempt == 1) startApps = await LoadStartApps();
                StartApp hit = null; int best = 0;
                foreach (var a in startApps)
                {
                    if (string.IsNullOrEmpty(a.Name)) continue;
                    var y = a.Name.ToLowerInvariant();
                    foreach (var n in names)
                    {
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        var x = n.Trim().ToLowerInvariant();
                        int sc = y == x ? 100 : y.StartsWith(x) ? 80 : y.Contains(x) ? 60 : (x.Contains(y) && y.Length >= 4) ? 40 : 0;
                        if (sc > best) { best = sc; hit = a; }
                    }
                }
                if (hit != null)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"shell:AppsFolder\\" + hit.Id + "\"") { UseShellExecute = true });
                        return hit.Name;
                    }
                    catch { return null; }
                }
            }
            return null;
        }

        // ---------- трей / автозапуск ----------
        void SetupTray()
        {
            tray = new System.Windows.Forms.NotifyIcon { Icon = AppIcon(), Visible = true, Text = "Dynamic Island" };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var auto = new System.Windows.Forms.ToolStripMenuItem("Автозапуск") { Checked = cfg.Autostart, CheckOnClick = true };
            auto.CheckedChanged += (s, e) => { cfg.Autostart = auto.Checked; cfg.Save(); SetAutostart(cfg.Autostart); };
            menu.Items.Add(auto);
            var pk = new System.Windows.Forms.ToolStripMenuItem("Всплывающие уведомления") { Checked = cfg.Peeks, CheckOnClick = true };
            pk.CheckedChanged += (s, e) => { cfg.Peeks = pk.Checked; cfg.Save(); };
            menu.Items.Add(pk);
            menu.Items.Add("Сбросить позицию", null, (s, e) => { cfg.OffsetX = 0; cfg.Save(); CenterTop(); });
            menu.Items.Add("Выход", null, (s, e) => { tray.Visible = false; tray.Dispose(); Application.Current.Shutdown(); });
            tray.ContextMenuStrip = menu;
        }

        static void SetAutostart(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (on) k.SetValue("DynamicIsland", "\"" + Environment.ProcessPath + "\"");
                else k.DeleteValue("DynamicIsland", false);
            }
            catch { }
        }
    }
}
