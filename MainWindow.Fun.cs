using System;
using System.Globalization;
using System.Runtime.InteropServices;
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
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

        static readonly Random rng = new();
        bool settingsReady, lowShown;
        int lastHour = -1;
        DateTime lastEye = DateTime.Now;

        void InitSettings()
        {
            StaticChk.IsChecked = cfg.Static; PeekChk.IsChecked = cfg.Peeks; AutoChk.IsChecked = cfg.Autostart;
            DirectChk.IsChecked = cfg.DirectNet; CafChk.IsChecked = cfg.Caffeine;
            HourChk.IsChecked = cfg.HourChime; EyeChk.IsChecked = cfg.EyeBreak;
            FallbackHandler.ForceDirect = cfg.DirectNet;
            ApplyCaffeine();
            settingsReady = true;
            Greet();
        }

        // ---------- настройки ----------
        void Settings_Changed(object s, TextChangedEventArgs e) { if (settingsReady) Save_Click(null, null); }
        void PeekChk_Click(object s, RoutedEventArgs e) { cfg.Peeks = PeekChk.IsChecked == true; cfg.Save(); }
        void AutoChk_Click(object s, RoutedEventArgs e) { cfg.Autostart = AutoChk.IsChecked == true; cfg.Save(); SetAutostart(cfg.Autostart); }
        void Direct_Click(object s, RoutedEventArgs e) { cfg.DirectNet = DirectChk.IsChecked == true; FallbackHandler.ForceDirect = cfg.DirectNet; cfg.Save(); }
        void Caf_Click(object s, RoutedEventArgs e) { cfg.Caffeine = CafChk.IsChecked == true; cfg.Save(); ApplyCaffeine(); }
        void Hour_Click(object s, RoutedEventArgs e) { cfg.HourChime = HourChk.IsChecked == true; cfg.Save(); }
        void Eye_Click(object s, RoutedEventArgs e) { cfg.EyeBreak = EyeChk.IsChecked == true; lastEye = DateTime.Now; cfg.Save(); }

        void Static_Click(object s, RoutedEventArgs e)
        {
            cfg.Static = StaticChk.IsChecked == true; cfg.Save();
            SlideToPosition();
        }

        void SlideToPosition()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            double target = Math.Clamp((sw - Width) / 2 + (cfg.Static ? 0 : cfg.OffsetX), 0, Math.Max(0, sw - Width));
            var a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(520))
                { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
            a.Completed += (s, e) => { BeginAnimation(LeftProperty, null); Left = target; };
            BeginAnimation(LeftProperty, a);
        }

        void ApplyCaffeine() => SetThreadExecutionState(cfg.Caffeine ? 0x80000003u : 0x80000000u);

        // ---------- мелочи ----------
        async void Greet()
        {
            await Task.Delay(1500);
            int h = DateTime.Now.Hour;
            string g = h < 5 ? "Доброй ночи" : h < 12 ? "Доброе утро" : h < 18 ? "Добрый день" : "Добрый вечер";
            ShowPeek("\uE706", g + "!", "Dynamic Island запущен · Ctrl+Alt+I", "", -1, null, null, 3600);
        }

        void FunTick()
        {
            var now = DateTime.Now;
            if (cfg.HourChime && now.Minute == 0 && now.Hour != lastHour)
            {
                lastHour = now.Hour;
                ShowPeek("\uE916", "Сейчас " + now.ToString("HH:mm"), "Новый час", "", -1, null, null, 3000);
            }
            if (cfg.EyeBreak && (now - lastEye).TotalMinutes >= 20)
            {
                lastEye = now;
                ShowPeek("\uE7B3", "Перерыв для глаз", "Посмотрите вдаль 20 секунд", "", -1, null, null, 6000, true);
            }
        }

        void HeartPop()
        {
            LikeBtn.RenderTransformOrigin = new Point(0.5, 0.5);
            var st = new ScaleTransform(1, 1);
            LikeBtn.RenderTransform = st;
            var a = new DoubleAnimation(1.8, 1, TimeSpan.FromMilliseconds(700)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4, EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        void Art_Click(object s, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2 || ses == null) return;
            OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(ArtistText.Text + " " + TitleText.Text + " текст песни"));
        }

        void Clip_Remove(object s, MouseButtonEventArgs e) { clips.Remove((string)((Button)s).Tag); }

        bool HandleLocalCommand(string q)
        {
            if (q.StartsWith("="))
            {
                Add(q, true);
                try
                {
                    var v = new System.Data.DataTable().Compute(q.Substring(1).Replace(',', '.'), null);
                    Add("= " + Convert.ToString(v, CultureInfo.InvariantCulture), false);
                }
                catch { Add("Не смог посчитать это выражение.", false); }
                return true;
            }
            if (!q.StartsWith("/")) return false;
            Add(q, true);
            switch (q.ToLowerInvariant())
            {
                case "/clear": ChatList.Children.Clear(); history.Clear(); break;
                case "/dice": Add("🎲 Выпало: " + rng.Next(1, 7), false); break;
                case "/coin": Add(rng.Next(2) == 0 ? "🪙 Орёл" : "🪙 Решка", false); break;
                case "/time": Add(DateTime.Now.ToString("dddd, d MMMM yyyy, HH:mm", Ru), false); break;
                default: Add("Команды: /dice, /coin, /time, /clear.\nКалькулятор: «=2+2*3».", false); break;
            }
            return true;
        }

        static string FriendlyNetError(Exception ex) =>
            "Не удалось связаться с OpenRouter.\n" + ex.GetBaseException().Message +
            "\nПроверьте интернет/VPN или включите «Без прокси» в настройках.";

        static string ApiError(int code, string body) => code switch
        {
            401 => "Неверный API-ключ OpenRouter (401). Проверьте его в настройках.",
            402 => "На счёте OpenRouter не хватает средств (402).",
            404 => "Модель не найдена (404). Проверьте название модели в настройках.",
            429 => "Слишком много запросов (429). Подождите немного.",
            _ => "Ошибка " + code + ": " + (body.Length > 200 ? body.Substring(0, 200) : body)
        };
    }
}
