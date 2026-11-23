using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Linq;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Windows.Data;

namespace GoldWidget
{
    // Alerts are one-shot rules; disabled rules remain editable after firing.
    public sealed class PetPreferences
    {
        public double Scale = 1;
        public bool BelowEnabled, AboveEnabled;
        public double Below = 750, Above = 800;
        public bool Quiet;
        public bool SnapToEdges = true;
        public List<AlertRecord> Alerts = new List<AlertRecord>();
    }

    public sealed class AlertRecord
    {
        public string TriggeredAt { get; set; }
        public string QuoteAt { get; set; }
        public string Rule { get; set; }
        public double Target { get; set; }
        public double Actual { get; set; }
    }
    // Transparent, always-on-top pet surface. Data and details remain in MainWindow.
    public sealed class PetWindow : Window
    {
        private readonly MainWindow data;
        private readonly TextBlock value = new TextBlock();
        private readonly TextBlock freshness = new TextBlock();
        private readonly Image character = new Image();
        private readonly List<BitmapSource> frames = new List<BitmapSource>();
        private readonly Dictionary<string, List<BitmapSource>> actionFrames = new Dictionary<string, List<BitmapSource>>();
        private string baseAction = "idle", activeAction = "idle";
        private DateTime actionUntil = DateTime.MinValue;
        private readonly TextBlock stateBadge = new TextBlock();
        private readonly ToolTip preview = new ToolTip();
        private readonly TextBlock previewPrice = new TextBlock();
        private readonly TextBlock previewChange = new TextBlock();
        private readonly TextBlock previewExtremes = new TextBlock();
        private readonly TextBlock previewStatus = new TextBlock();
        private readonly Canvas previewChart = new Canvas { Width = 228, Height = 78 };
        private readonly TextBlock previewCaption = new TextBlock();
        private string marketStatus = "正在连接，暂未获取本次在线报价";
        private bool speechIsAlert;
        private readonly DispatcherTimer animation = new DispatcherTimer();
        private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        private int frame;
        private Point mouseDown;
        private bool dragging;
        private bool suppressClick;
        private readonly DispatcherTimer clickDelay = new DispatcherTimer();
        private readonly DispatcherTimer speechTimer = new DispatcherTimer();
        private readonly DispatcherTimer hoverDelay = new DispatcherTimer();
        private readonly Popup speech = new Popup();
        private readonly TextBlock speechText = new TextBlock();
        private readonly Random random = new Random();
        private readonly TranslateTransform motion = new TranslateTransform();
        private PetPreferences preferences = new PetPreferences();
        private FrameworkElement petRoot;
        private Window settingsWindow;
        private Window historyWindow;
        private DataGrid historyGrid;
        private TextBlock historyCount;
        private MenuItem quietMenu;
        private Forms.ToolStripMenuItem trayQuietMenu;
        private const string StartupValueName = "GoldWidgetRMB";
        private double? lastPrice;
        private DateTime lastMood = DateTime.MinValue;
        private readonly string preferencesFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoldWidgetRMB", "pet-preferences.json");
        private readonly string positionFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoldWidgetRMB", "pet-position.txt");

        public PetWindow(MainWindow dataWindow)
        {
            data = dataWindow;
            Title = "鲸小金";
            Icon = App.WindowIcon;
            Width = 76; Height = 104;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            // The pet is a desktop-only surface; only the expanded details window owns a taskbar button.
            ShowInTaskbar = false; UseLayoutRounding = true;
            Left = SystemParameters.WorkArea.Right - Width - 35;
            Top = SystemParameters.WorkArea.Bottom - Height - 20;
            LoadPosition();
            LoadPreferences();
            BuildPet();
            ApplySize();
            LoadFrames();
            clickDelay.Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 60);
            clickDelay.Tick += (s, e) => { clickDelay.Stop(); PlayAction("touch", 6); React("♡ " + new[] { "小金今天也在认真盯盘！", "摸摸收到啦～", "有我陪你看金价。", "别急，小金在盯着呢！" }[random.Next(4)], 4); };
            speechTimer.Tick += (s, e) => { speechTimer.Stop(); speech.IsOpen = false; };
            data.QuoteUpdated += UpdatePrice;
            data.StatusUpdated += UpdateStatus;
            data.AlertHistoryRequested += OpenHistory;
            var menu = new ContextMenu();
            AddMenu(menu, "打开行情详情", () => data.OpenDetails());
            var refresh = new MenuItem { Header = "刷新行情" };
            refresh.Click += async (s, e) => await data.RequestRefresh(); menu.Items.Add(refresh);
            AddMenu(menu, "隐藏小金", () => Hide());
            AddMenu(menu, "鲸小金设置…", () => OpenSettings());
            AddMenu(menu, "提醒记录…", () => OpenHistory());
            quietMenu = new MenuItem { Header = "安静模式", IsCheckable = true, IsChecked = preferences.Quiet };
            quietMenu.Click += (s, e) => SetQuiet(quietMenu.IsChecked);
            menu.Items.Add(quietMenu);
            AddMenu(menu, "素材来源与许可", () => MessageBox.Show("鲸鱼娘素材来源：PC2005-cloud/dsh-pet\nhttps://github.com/PC2005-cloud/dsh-pet\n角色原型：上善无形；女仆鲸鱼娘设计：ZipZipPipe\nCC BY-NC-SA 4.0：署名、非商业、相同方式共享。\n本版对待机 GIF 抽帧、裁切并移除绿色背景。非 DeepSeek 官方产品。", "素材署名"));
            AddMenu(menu, "退出鲸小金", () => Close());
            ContextMenu = menu;
            tray.Icon = App.CreateTrayIcon(); tray.Text = "鲸小金 · 双击恢复"; tray.Visible = true;
            tray.DoubleClick += (s, e) => Restore();
            var trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("叫出小金", null, (s, e) => Restore());
            trayMenu.Items.Add("行情详情", null, (s, e) => data.OpenDetails());
            trayMenu.Items.Add("鲸小金设置", null, (s, e) => OpenSettings());
            trayMenu.Items.Add("提醒记录", null, (s, e) => OpenHistory());
            trayQuietMenu = new Forms.ToolStripMenuItem("安静模式") { CheckOnClick = true, Checked = preferences.Quiet };
            trayQuietMenu.Click += (s, e) => SetQuiet(trayQuietMenu.Checked); trayMenu.Items.Add(trayQuietMenu);
            trayMenu.Items.Add("退出", null, (s, e) => Close());
            tray.ContextMenuStrip = trayMenu;
            animation.Interval = TimeSpan.FromMilliseconds(100);
            animation.Tick += (s, e) => {
                if (!IsVisible) { return; }
                if (DateTime.UtcNow >= actionUntil && activeAction != baseAction) { activeAction = baseAction; frame = 0; }
                var sequence = actionFrames[activeAction];
                frame = (frame + 1) % sequence.Count; character.Source = sequence[frame];
            };
            Loaded += (s, e) => { Clamp(); UpdatePrice(); animation.Start(); };
            IsVisibleChanged += (s, e) => { if (!IsVisible) { hoverDelay.Stop(); preview.IsOpen = false; speech.IsOpen = false; clickDelay.Stop(); } };
            Closing += (s, e) => {
                SavePosition(); animation.Stop(); clickDelay.Stop(); speechTimer.Stop(); hoverDelay.Stop(); preview.IsOpen = false; speech.IsOpen = false;
                if (settingsWindow != null) { settingsWindow.Close(); }
                if (historyWindow != null) { historyWindow.Close(); }
                tray.Visible = false; tray.Dispose();
                data.ExitRequested = true; data.Close(); Application.Current.Shutdown();
            };
        }

        private void BuildPet()
        {
            var root = new Grid { Width = 76, Height = 104, Cursor = Cursors.Hand };
            petRoot = root;
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            root.RowDefinitions.Add(new RowDefinition());
            var bubble = new Border {
                Background = new SolidColorBrush(Color.FromArgb(242, 24, 34, 50)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(117, 142, 186)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 74
            };
            var text = new StackPanel();
            value.Text = "— —"; value.FontFamily = new FontFamily("Microsoft YaHei UI");
            value.FontSize = 15; value.FontWeight = FontWeights.SemiBold;
            value.Foreground = new SolidColorBrush(Color.FromRgb(244, 206, 125)); value.HorizontalAlignment = HorizontalAlignment.Center;
            freshness.Text = "--:--:--"; freshness.FontSize = 9; freshness.Foreground = Brushes.LightSteelBlue;
            freshness.HorizontalAlignment = HorizontalAlignment.Center;
            text.Children.Add(value); bubble.Child = text;
            BuildPreview(); preview.PlacementTarget = bubble;
            hoverDelay.Interval = TimeSpan.FromMilliseconds(350);
            hoverDelay.Tick += (s, e) => { hoverDelay.Stop(); if (bubble.IsMouseOver && IsVisible) { RefreshPreview(); preview.IsOpen = true; } };
            bubble.MouseEnter += (s, e) => { hoverDelay.Stop(); hoverDelay.Start(); };
            bubble.MouseLeave += (s, e) => { hoverDelay.Stop(); preview.IsOpen = false; };
            root.Children.Add(bubble);
            character.Stretch = Stretch.Uniform; character.Margin = new Thickness(3, 0, 3, 0);
            character.Cursor = Cursors.Hand;
            character.RenderTransform = motion;
            character.ToolTip = "单击摸摸小金 · 双击行情详情 · 拖动移动 · 右键设置";
            Grid.SetRow(character, 1); root.Children.Add(character); Content = root;
            stateBadge.FontSize = 13; stateBadge.FontWeight = FontWeights.Bold;
            stateBadge.Foreground = Brushes.LightSteelBlue; stateBadge.HorizontalAlignment = HorizontalAlignment.Right;
            stateBadge.VerticalAlignment = VerticalAlignment.Top; stateBadge.Margin = new Thickness(0, 7, 3, 0);
            stateBadge.IsHitTestVisible = false; Grid.SetRow(stateBadge, 1); root.Children.Add(stateBadge);
            speechText.Foreground = Brushes.White; speechText.FontSize = 12; speechText.TextWrapping = TextWrapping.Wrap;
            speechText.MaxWidth = 190;
            speech.Child = new Border { Background = new SolidColorBrush(Color.FromRgb(32, 43, 60)), BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(10), Child = speechText };
            speech.PlacementTarget = this; speech.Placement = PlacementMode.Top;
            speech.AllowsTransparency = true; speech.StaysOpen = true; speech.IsHitTestVisible = false;
            // Leave the surrounding root transparent; dragging starts only on the character surface.
            character.MouseLeftButtonDown += (s, e) => {
                if (e.ClickCount == 2) { clickDelay.Stop(); suppressClick = true; dragging = false; character.ReleaseMouseCapture(); data.OpenDetails(); e.Handled = true; return; }
                mouseDown = e.GetPosition(this); dragging = false; suppressClick = false;
            };
            character.MouseLeftButtonUp += (s, e) => { if (!suppressClick && !dragging) { clickDelay.Stop(); clickDelay.Start(); } };
            character.MouseMove += (s, e) => {
                if (e.LeftButton != MouseButtonState.Pressed || dragging) { return; }
                Point now = e.GetPosition(this);
                if (Math.Abs(now.X - mouseDown.X) + Math.Abs(now.Y - mouseDown.Y) > 6)
                {
                    clickDelay.Stop(); suppressClick = true; speech.IsOpen = false; preview.IsOpen = false;
                    dragging = true; DragMove(); SnapPosition(); SavePosition(); dragging = false;
                }
            };
        }

        private void LoadFrames()
        {
            var assembly = Assembly.GetExecutingAssembly();
            for (int i = 0; i < 60; i++)
            {
                using (var stream = assembly.GetManifestResourceStream("pet.idle-" + i.ToString("D3") + ".png"))
                {
                    if (stream == null) { throw new FileNotFoundException("内置桌宠动画缺失"); }
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); frames.Add(bitmap);
                }
            }
            character.Source = frames[0];
            actionFrames["idle"] = frames;
            foreach (string action in new[] { "up", "down", "touch", "sleep", "offline" })
            {
                var sequence = new List<BitmapSource>();
                for (int i = 0; i < 60; i++)
                {
                    using (var stream = assembly.GetManifestResourceStream("pet." + action + "-" + i.ToString("D3") + ".png"))
                    {
                        if (stream == null) { throw new FileNotFoundException("内置动作缺失：" + action); }
                        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); sequence.Add(bitmap);
                    }
                }
                actionFrames[action] = sequence;
            }
        }

        private void UpdatePrice()
        {
            Quote q = data.CurrentQuote;
            if (q == null) { return; }
            string amount = (q.Gold * q.Rate / 31.1034768).ToString("F2", CultureInfo.InvariantCulture);
            value.Text = amount;
            freshness.Text = q.QuoteTime.ToString("HH:mm:ss");
            tray.Text = "鲸小金 ¥" + amount + "/克";
            if (preview.IsOpen) { RefreshPreview(); }
        }

        private void UpdateStatus(string message)
        {
            // Keep the bubble numeric; convey stale/failed status through color and hover text.
            freshness.Foreground = message.StartsWith("连接失败") || message.StartsWith("报价未更新") ? Brushes.Orange : Brushes.LightSteelBlue;
            freshness.ToolTip = message;
            marketStatus = message;
            UpdatePetState(message);
            value.Foreground = message.StartsWith("连接失败") || data.GoldQuoteStale || data.FxQuoteStale ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(244, 206, 125));
            // Cached, failed and stale quotes cannot fire a reminder or animate a market move.
            if (message.StartsWith("已更新") && !data.GoldQuoteStale && !data.FxQuoteStale && data.CurrentQuote != null) { ProcessLivePrice(data.CurrentQuote); }
            if (preview.IsOpen) { RefreshPreview(); }
        }

        private void ProcessLivePrice(Quote q)
        {
            double price = q.Gold * q.Rate / 31.1034768;
            var triggered = new List<string>();
            bool belowWasEnabled = preferences.BelowEnabled, aboveWasEnabled = preferences.AboveEnabled;
            var previousHistory = new List<AlertRecord>(preferences.Alerts);
            if (preferences.BelowEnabled && price <= preferences.Below)
            {
                preferences.BelowEnabled = false; triggered.Add("跌至 " + preferences.Below.ToString("F2") + " 以下");
                AddAlertRecord("低于或等于", preferences.Below, price, q);
            }
            if (preferences.AboveEnabled && price >= preferences.Above)
            {
                preferences.AboveEnabled = false; triggered.Add("达到 " + preferences.Above.ToString("F2") + " 以上");
                AddAlertRecord("高于或等于", preferences.Above, price, q);
            }
            if (triggered.Count > 0)
            {
                // Persist one-shot flags and their records in the same atomic file before notifying.
                if (!SavePreferences())
                {
                    preferences.BelowEnabled = belowWasEnabled; preferences.AboveEnabled = aboveWasEnabled; preferences.Alerts = previousHistory;
                    return;
                }
                RefreshHistory();
                string message = "金价 " + price.ToString("F2") + " 元/克，" + string.Join("，", triggered) + "。本次提醒已完成。";
                if (!IsVisible) { Restore(); }
                React("🔔 " + message, 10, true);
                tray.ShowBalloonTip(8000, "金价提醒", message, Forms.ToolTipIcon.Info);
            }
            else if (!preferences.Quiet && lastPrice.HasValue && Math.Abs(price - lastPrice.Value) >= 0.01 && (DateTime.UtcNow - lastMood).TotalSeconds >= 60 && !speech.IsOpen)
            {
                lastMood = DateTime.UtcNow;
                PlayAction(price > lastPrice.Value ? "up" : "down", 6);
                React(price > lastPrice.Value ? "٩(ˊᗜˋ*)و 涨啦～" : "(｡•́︿•̀｡) 回落了，再观察。", 3);
            }
            lastPrice = price;
        }

        // Transient speech never replaces the numeric quote. Market reactions have a one-minute cooldown.
        private void React(string message, double seconds, bool isAlert = false)
        {
            speechIsAlert = isAlert;
            speechText.Text = message; speech.IsOpen = true;
            speechTimer.Stop(); speechTimer.Interval = TimeSpan.FromSeconds(seconds); speechTimer.Start();
        }

        // Transient interactions override resting state briefly, then resume it without resetting every refresh.
        private void PlayAction(string action, double seconds)
        {
            if (!actionFrames.ContainsKey(action)) { return; }
            activeAction = action; frame = 0; actionUntil = DateTime.UtcNow.AddSeconds(seconds);
            character.Source = actionFrames[action][0];
        }

        private void UpdatePetState(string message)
        {
            string next = "idle";
            string badge = "";
            string explanation = "单击摸摸小金 · 双击行情详情 · 拖动移动 · 右键设置";
            if (message.StartsWith("连接失败"))
            {
                next = "offline"; badge = "?"; explanation = "连接失败，正在重试；显示最后成功报价，并非休市确认。";
            }
            else if (data.GoldQuoteStale)
            {
                var utc = DateTime.UtcNow;
                bool weekendWindow = utc.DayOfWeek == DayOfWeek.Saturday || (utc.DayOfWeek == DayOfWeek.Sunday && utc.Hour < 21);
                bool goldStale = data.CurrentQuote != null && (utc.AddHours(8) - data.CurrentQuote.QuoteTime).TotalMinutes > 5;
                if (weekendWindow && goldStale)
                {
                    next = "sleep"; badge = "zZ";
                    explanation = "周末且黄金报价停更，可能休市。仍会定时检查报价，恢复更新后醒来。";
                }
                else
                {
                    next = "offline"; badge = "…";
                    explanation = "黄金源站报价延迟，不能确认休市；详情可查看报价时间。";
                }
            }
            else if (data.FxQuoteStale)
            {
                // Fresh gold keeps the pet awake even when the onshore FX session has not opened.
                explanation = "黄金正在更新，人民币折算沿用最后汇率；价格提醒暂缓，等待汇率恢复。";
            }
            bool stateChanged = baseAction != next;
            baseAction = next; stateBadge.Text = badge; character.ToolTip = explanation;
            if (stateChanged && next != "idle")
            {
                // Failure state takes precedence over a market celebration that is still playing.
                activeAction = next; frame = 0; actionUntil = DateTime.MinValue;
            }
        }

        private void BuildPreview()
        {
            var panel = new StackPanel { Width = 228 };
            previewPrice.FontSize = 17; previewPrice.FontWeight = FontWeights.SemiBold;
            previewPrice.Foreground = Brushes.MidnightBlue;
            previewChange.FontSize = 12; previewChange.Margin = new Thickness(0, 4, 0, 8);
            previewExtremes.FontSize = 11; previewExtremes.Foreground = Brushes.DarkSlateGray; previewExtremes.Margin = new Thickness(0, 0, 0, 8);
            previewCaption.FontSize = 10; previewCaption.Foreground = Brushes.SlateGray;
            previewCaption.TextWrapping = TextWrapping.Wrap;
            previewStatus.FontSize = 10; previewStatus.Foreground = Brushes.SlateGray;
            previewStatus.TextWrapping = TextWrapping.Wrap; previewStatus.Margin = new Thickness(0, 7, 0, 0);
            panel.Children.Add(previewPrice); panel.Children.Add(previewChange); panel.Children.Add(previewExtremes); panel.Children.Add(previewChart);
            panel.Children.Add(previewCaption); panel.Children.Add(previewStatus);
            preview.Content = new Border { Background = new SolidColorBrush(Color.FromRgb(239, 245, 252)), BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = panel };
            preview.Background = Brushes.Transparent; preview.BorderThickness = new Thickness(0); preview.Padding = new Thickness(0);
            preview.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><ContentPresenter/></ControlTemplate>");
            preview.Cursor = Cursors.Hand;
            preview.Placement = PlacementMode.Left;
        }

        // A snapshot prevents the hover chart from mutating the history used by the full details panel.
        private void RefreshPreview()
        {
            previewChart.Children.Clear();
            var q = data.CurrentQuote;
            if (q == null)
            {
                previewPrice.Text = "等待在线报价"; previewChange.Text = ""; previewExtremes.Text = "日内最高 —    最低 —";
                previewCaption.Text = "暂无分钟走势"; previewStatus.Text = marketStatus; return;
            }
            double factor = q.Rate / 31.1034768;
            previewPrice.Text = (q.Gold * factor).ToString("F2") + " 元/克";
            double change = (q.Gold - q.Previous) * factor;
            previewChange.Text = "较昨收 " + change.ToString("+0.00;-0.00;0.00") + " 元（" + ((q.Gold / q.Previous - 1) * 100).ToString("+0.00;-0.00;0.00") + "%）";
            previewChange.Foreground = change >= 0 ? Brushes.Firebrick : Brushes.SeaGreen;
            previewExtremes.Text = "日内最高 " + (q.High * factor).ToString("F2") + "    最低 " + (q.Low * factor).ToString("F2");
            var points = data.HistorySnapshot.Where(p => p.Item1.Date == q.QuoteTime.Date && p.Item1 <= q.QuoteTime).OrderBy(p => p.Item1).ToList();
            if (points.Count >= 2)
            {
                double min = points.Min(p => p.Item2) * factor, max = points.Max(p => p.Item2) * factor;
                double padding = Math.Max((max - min) * 0.1, 0.05); min -= padding; max += padding;
                double duration = Math.Max(1.0, (points.Last().Item1 - points[0].Item1).TotalSeconds);
                var line = new Polyline { Stroke = Brushes.Goldenrod, StrokeThickness = 1.6 };
                foreach (var p in points)
                {
                    line.Points.Add(new Point(3 + (p.Item1 - points[0].Item1).TotalSeconds / duration * 220, 3 + (max - p.Item2 * factor) / (max - min) * 57));
                }
                previewChart.Children.Add(line);
                var left = new TextBlock { Text = points[0].Item1.ToString("HH:mm"), FontSize = 9, Foreground = Brushes.SlateGray };
                var right = new TextBlock { Text = points.Last().Item1.ToString("HH:mm"), FontSize = 9, Foreground = Brushes.SlateGray };
                Canvas.SetTop(left, 63); Canvas.SetTop(right, 63); Canvas.SetLeft(right, 190);
                previewChart.Children.Add(left); previewChart.Children.Add(right);
                previewCaption.Text = "日内分钟报价 × 当前汇率 · 人民币/克";
            }
            else { previewCaption.Text = "历史暂不可用，正在积累实际报价"; }
            previewStatus.Text = marketStatus + "\n黄金 " + q.QuoteTime.ToString("MM-dd HH:mm:ss") + "\n汇率 " + q.FxTime.ToString("MM-dd HH:mm:ss") + "（北京时间）";
        }

        private void ApplySize()
        {
            Width = 76 * preferences.Scale; Height = 104 * preferences.Scale;
            petRoot.LayoutTransform = new ScaleTransform(preferences.Scale, preferences.Scale);
            if (IsLoaded) { Clamp(); }
        }

        private void LoadPreferences()
        {
            try
            {
                if (File.Exists(preferencesFile)) { preferences = new JavaScriptSerializer().Deserialize<PetPreferences>(File.ReadAllText(preferencesFile)) ?? new PetPreferences(); }
                if (double.IsNaN(preferences.Scale) || double.IsInfinity(preferences.Scale)) { preferences.Scale = 1; }
                preferences.Scale = Math.Max(0.75, Math.Min(3, preferences.Scale));
                preferences.Alerts = (preferences.Alerts ?? new List<AlertRecord>()).Where(a => a != null).Take(500).ToList();
                if (!ValidPrice(preferences.Below) || !ValidPrice(preferences.Above) || preferences.Below >= preferences.Above) { preferences.BelowEnabled = preferences.AboveEnabled = false; preferences.Below = 750; preferences.Above = 800; }
            }
            catch (Exception ex) { preferences = new PetPreferences(); value.ToolTip = "设置读取失败：" + ex.Message; }
        }

        private static bool ValidPrice(double price) { return !double.IsNaN(price) && !double.IsInfinity(price) && price > 0 && price <= 100000; }

        private bool SavePreferences()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(preferencesFile));
                string temporary = preferencesFile + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(preferences));
                if (File.Exists(preferencesFile)) { File.Replace(temporary, preferencesFile, null); } else { File.Move(temporary, preferencesFile); }
                return true;
            }
            catch (Exception ex) { MessageBox.Show("设置保存失败：" + ex.Message, "鲸小金"); return false; }
        }

        private void OpenSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            var panel = new StackPanel { Margin = new Thickness(22) };
            panel.Children.Add(new TextBlock { Text = "小金的大小", FontWeight = FontWeights.SemiBold });
            var scaleLabel = new TextBlock { Margin = new Thickness(0, 5, 0, 0) };
            var scale = new Slider { Minimum = 0.75, Maximum = 3, Value = preferences.Scale, TickFrequency = 0.25, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5, 0, 16) };
            scaleLabel.Text = (scale.Value * 100).ToString("F0") + "%（当前小号为100%）";
            scale.ValueChanged += (s, e) => scaleLabel.Text = (scale.Value * 100).ToString("F0") + "%（保存后生效）";
            panel.Children.Add(scaleLabel); panel.Children.Add(scale);
            string oldStartup;
            try { oldStartup = ReadStartupCommand(); }
            catch (Exception ex) { MessageBox.Show("无法读取自启设置：" + ex.Message, "鲸小金设置"); return; }
            var startup = new CheckBox { Content = "登录 Windows 后自动启动", IsChecked = string.Equals(oldStartup, StartupCommand, StringComparison.OrdinalIgnoreCase), Margin = new Thickness(0, 0, 0, 8) };
            var quiet = new CheckBox { Content = "安静模式（保留价格提醒、主动摸头）", IsChecked = preferences.Quiet, Margin = new Thickness(0, 0, 0, 8) };
            var snap = new CheckBox { Content = "拖动到屏幕边缘时自动吸附", IsChecked = preferences.SnapToEdges, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(startup); panel.Children.Add(quiet); panel.Children.Add(snap);
            var below = new CheckBox { Content = "跌到或低于此价提醒（元/克）", IsChecked = preferences.BelowEnabled };
            var belowPrice = new TextBox { Text = preferences.Below.ToString("F2", CultureInfo.InvariantCulture), Margin = new Thickness(0, 5, 0, 14), Padding = new Thickness(5) };
            var above = new CheckBox { Content = "涨到或高于此价提醒（元/克）", IsChecked = preferences.AboveEnabled };
            var abovePrice = new TextBox { Text = preferences.Above.ToString("F2", CultureInfo.InvariantCulture), Margin = new Thickness(0, 5, 0, 14), Padding = new Thickness(5) };
            panel.Children.Add(below); panel.Children.Add(belowPrice); panel.Children.Add(above); panel.Children.Add(abovePrice);
            panel.Children.Add(new TextBlock { Text = "每条提醒只触发一次；触发后取消勾选。保存后若已满足条件，下次成功刷新即提醒。旧报价和休市状态不触发。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
            var save = new Button { Content = "保存设置", Padding = new Thickness(8), Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(save);
            var historyButton = new Button { Content = "查看提醒记录", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(7) };
            historyButton.Click += (s, e) => OpenHistory(); panel.Children.Add(historyButton);
            settingsWindow = new Window { Title = "鲸小金设置", Width = 390, MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Topmost = true, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Microsoft YaHei UI") };
            settingsWindow.Closed += (s, e) => settingsWindow = null;
            save.Click += (s, e) => {
                double low, high;
                if (!double.TryParse(belowPrice.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out low) || !double.TryParse(abovePrice.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out high) || !ValidPrice(low) || !ValidPrice(high) || low >= high)
                { MessageBox.Show("请输入有效价格，且下限必须低于上限。", "价格设置"); return; }
                var old = preferences;
                string desiredStartup = startup.IsChecked == true ? StartupCommand : null;
                string startupBeforeSave;
                try { startupBeforeSave = ReadStartupCommand(); WriteStartupCommand(desiredStartup); }
                catch (Exception ex) { MessageBox.Show("自启设置未保存：" + ex.Message, "鲸小金设置"); return; }
                preferences = new PetPreferences { Scale = scale.Value, Below = low, Above = high, BelowEnabled = below.IsChecked == true, AboveEnabled = above.IsChecked == true, Quiet = quiet.IsChecked == true, SnapToEdges = snap.IsChecked == true, Alerts = old.Alerts };
                if (!SavePreferences())
                {
                    preferences = old;
                    try { WriteStartupCommand(startupBeforeSave); } catch (Exception ex) { MessageBox.Show("自启设置回退失败：" + ex.Message, "鲸小金设置"); }
                    return;
                }
                ApplyQuiet(); ApplySize(); SavePosition(); settingsWindow.Close();
            };
            settingsWindow.Show();
        }

        private static string StartupCommand
        {
            get { return "\"" + Assembly.GetExecutingAssembly().Location + "\""; }
        }

        // Only the current user's Run entry is managed; no administrator permissions are needed.
        private static string ReadStartupCommand()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                return key == null ? null : key.GetValue(StartupValueName) as string;
            }
        }

        private static void WriteStartupCommand(string command)
        {
            if (string.Equals(ReadStartupCommand(), command, StringComparison.Ordinal)) { return; }
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (key == null) { throw new IOException("无法写入当前用户的自启项"); }
                if (command == null) { key.DeleteValue(StartupValueName, false); }
                else { key.SetValue(StartupValueName, command, RegistryValueKind.String); }
            }
        }

        private void SetQuiet(bool quiet)
        {
            bool old = preferences.Quiet; preferences.Quiet = quiet;
            if (!SavePreferences()) { preferences.Quiet = old; }
            ApplyQuiet();
        }

        private void ApplyQuiet()
        {
            quietMenu.IsChecked = preferences.Quiet; trayQuietMenu.Checked = preferences.Quiet;
            if (!preferences.Quiet) { return; }
            if (!speechIsAlert) { speech.IsOpen = false; speechTimer.Stop(); }
            if (activeAction == "up" || activeAction == "down") { activeAction = baseAction; frame = 0; actionUntil = DateTime.MinValue; }
        }

        private void AddAlertRecord(string rule, double target, double actual, Quote quote)
        {
            preferences.Alerts.Insert(0, new AlertRecord {
                TriggeredAt = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss"),
                QuoteAt = quote.QuoteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Rule = rule, Target = target, Actual = actual
            });
            if (preferences.Alerts.Count > 500) { preferences.Alerts.RemoveRange(500, preferences.Alerts.Count - 500); }
        }

        private void RefreshHistory()
        {
            if (historyWindow == null) { return; }
            historyGrid.ItemsSource = new List<AlertRecord>(preferences.Alerts);
            historyCount.Text = preferences.Alerts.Count == 0 ? "暂无提醒记录。价格规则触发后会出现在这里。" : "已保存 " + preferences.Alerts.Count + " 条提醒（最多保留最近500条）";
        }

        private void OpenHistory()
        {
            if (historyWindow != null) { historyWindow.Activate(); return; }
            var root = new DockPanel { Margin = new Thickness(16) };
            historyCount = new TextBlock { Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(historyCount, Dock.Top); root.Children.Add(historyCount);
            var footer = new TextBlock { Text = "时间均为北京时间；目标价和触发价单位：人民币/克。记录仅保存在本机。", Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            historyGrid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
            historyGrid.Columns.Add(new DataGridTextColumn { Header = "提醒时间", Binding = new Binding("TriggeredAt"), Width = 155 });
            historyGrid.Columns.Add(new DataGridTextColumn { Header = "规则", Binding = new Binding("Rule"), Width = 85 });
            historyGrid.Columns.Add(new DataGridTextColumn { Header = "目标价", Binding = new Binding("Target") { StringFormat = "F2" }, Width = 75 });
            historyGrid.Columns.Add(new DataGridTextColumn { Header = "触发价", Binding = new Binding("Actual") { StringFormat = "F2" }, Width = 75 });
            historyGrid.Columns.Add(new DataGridTextColumn { Header = "黄金报价时间", Binding = new Binding("QuoteAt"), Width = 155 });
            root.Children.Add(historyGrid);
            historyWindow = new Window { Title = "金价提醒记录", Width = 650, Height = 390, MinWidth = 420, MinHeight = 260, Content = root, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Microsoft YaHei UI") };
            historyWindow.Closed += (s, e) => { historyWindow = null; historyGrid = null; historyCount = null; };
            RefreshHistory(); historyWindow.Show();
        }

        // Snap only after a drag ends. WorkingArea excludes the taskbar; coordinates are in DIPs.
        private void SnapPosition()
        {
            Clamp();
            if (!preferences.SnapToEdges) { return; }
            var source = PresentationSource.FromVisual(this);
            double scale = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            var area = Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left + Width / 2) * scale), (int)((Top + Height / 2) * scale))).WorkingArea;
            double left = area.Left / scale, right = area.Right / scale - Width;
            double top = area.Top / scale, bottom = area.Bottom / scale - Height;
            const double threshold = 18;
            if (Math.Abs(Left - left) <= threshold) { Left = left; }
            else if (Math.Abs(Left - right) <= threshold) { Left = right; }
            if (Math.Abs(Top - top) <= threshold) { Top = top; }
            else if (Math.Abs(Top - bottom) <= threshold) { Top = bottom; }
        }

        private void Restore() { Show(); Topmost = true; Clamp(); Activate(); }
        private static void AddMenu(ContextMenu menu, string label, Action action)
        {
            var item = new MenuItem { Header = label }; item.Click += (s, e) => action(); menu.Items.Add(item);
        }

        private void Clamp()
        {
            var source = PresentationSource.FromVisual(this);
            double scale = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            var area = Forms.Screen.FromPoint(new System.Drawing.Point((int)(Left * scale), (int)(Top * scale))).WorkingArea;
            Left = Math.Max(area.Left / scale, Math.Min(Left, area.Right / scale - Width));
            Top = Math.Max(area.Top / scale, Math.Min(Top, area.Bottom / scale - Height));
        }

        private void SavePosition()
        {
            try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(positionFile)); File.WriteAllText(positionFile, Left.ToString(CultureInfo.InvariantCulture) + "|" + Top.ToString(CultureInfo.InvariantCulture)); }
            catch (IOException) { freshness.ToolTip = "位置保存失败"; }
            catch (UnauthorizedAccessException) { freshness.ToolTip = "位置目录不可写"; }
        }

        private void LoadPosition()
        {
            try {
                if (!File.Exists(positionFile)) { return; }
                var a = File.ReadAllText(positionFile).Split('|'); double x, y;
                if (a.Length == 2 && double.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(y) && !double.IsInfinity(y)) { Left = x; Top = y; }
            }
            catch (IOException) { freshness.ToolTip = "位置读取失败"; }
            catch (UnauthorizedAccessException) { freshness.ToolTip = "位置目录不可读"; }
        }
    }
}
