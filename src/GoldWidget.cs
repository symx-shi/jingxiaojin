using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Threading;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: System.Reflection.AssemblyTitle("鲸小金")]
[assembly: System.Reflection.AssemblyProduct("鲸小金")]
[assembly: System.Reflection.AssemblyDescription("小金陪你看国际金价 · 人民币每克")]
[assembly: System.Reflection.AssemblyVersion("1.2.2.0")]

namespace GoldWidget
{
    public sealed class Quote
    {
        public double Gold, Previous, High, Low, Rate;
        public DateTime QuoteTime, FxTime;
    }

    // Source timestamps are Beijing wall-clock times, independent of the PC timezone.
    public sealed class SavedState
    {
        public double Left, Top;
        public bool Pinned;
    }

    public sealed class MinutePayload
    {
        public List<List<string>> minLine_1d;
    }

    public sealed class MainWindow : Window
    {
        private readonly TextBlock price = new TextBlock();
        private readonly TextBlock change = new TextBlock();
        private readonly TextBlock status = new TextBlock();
        private readonly Grid details = new Grid();
        private readonly Canvas chart = new Canvas();
        private readonly TextBlock chartEmpty = new TextBlock();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        private readonly List<Tuple<DateTime, double>> history = new List<Tuple<DateTime, double>>();
        private Quote quote;
        private bool expanded;
        private bool loading;
        private bool closing;
        private bool hourRange;
        private DateTime lastHistoryFetch = DateTime.MinValue;
        private readonly List<TextBlock> statistics = new List<TextBlock>();
        private readonly TextBlock quoteTime = new TextBlock();
        private readonly TextBlock fxTime = new TextBlock();
        private readonly TextBlock chartCaption = new TextBlock();
        private readonly Button refreshButton = new Button();
        private List<Tuple<DateTime, double>> chartPoints = new List<Tuple<DateTime, double>>();
        private readonly Line chartGuide = new Line { Stroke = Brushes.LightSteelBlue, StrokeThickness = 1, IsHitTestVisible = false };
        private readonly Border chartReadout = new Border { Background = Brushes.WhiteSmoke, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), IsHitTestVisible = false };
        private readonly TextBlock chartReadoutText = new TextBlock { Foreground = Brushes.MidnightBlue, FontSize = 11 };
        private readonly Button hourButton = new Button();
        private readonly Button dayButton = new Button();
        private readonly string cacheFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoldWidgetRMB", "quote.txt");
        public event Action QuoteUpdated;
        public event Action AlertHistoryRequested;
        public event Action<string> StatusUpdated;
        public Quote CurrentQuote { get { return quote; } }
        public bool GoldQuoteStale { get { return quote == null || (DateTime.UtcNow.AddHours(8) - quote.QuoteTime).TotalMinutes > 5; } }
        public bool FxQuoteStale { get { return quote == null || (DateTime.UtcNow.AddHours(8) - quote.FxTime).TotalMinutes > 5; } }
        public List<Tuple<DateTime, double>> HistorySnapshot { get { return new List<Tuple<DateTime, double>>(history); } }
        public bool ExitRequested;
        public Task RequestRefresh() { return RefreshQuote(); }

        // Details are independent from the pet window and can be closed without stopping quotes.
        public void OpenDetails()
        {
            if (!expanded) { ToggleDetails(); }
            Show(); WindowState = WindowState.Normal; ClampPosition(); Activate();
        }

        public MainWindow()
        {
            Title = "鲸小金 · 行情详情"; Width = 326; Height = 184; WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Icon = App.WindowIcon;
            ResizeMode = ResizeMode.NoResize;
            Foreground = new SolidColorBrush(Color.FromRgb(233, 238, 245));
            Background = Brushes.Transparent; Topmost = false; ShowInTaskbar = true; FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            BuildUi();
            tray.Icon = App.CreateTrayIcon(); tray.Visible = false; tray.Text = "鲸小金";
            tray.DoubleClick += (s, e) => { Show(); Activate(); };
            var menu = new Forms.ContextMenuStrip(); menu.Items.Add("显示 / 隐藏", null, (s, e) => { if (IsVisible) Hide(); else { Show(); Activate(); } }); menu.Items.Add("退出", null, (s, e) => Close()); tray.ContextMenuStrip = menu;
            timer.Interval = TimeSpan.FromSeconds(15); timer.Tick += async (s, e) => await RefreshQuote();
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Top + 60;
            LoadSettings();
            Topmost = false;
            Loaded += async (s, e) => { ClampPosition(); LoadCache(); timer.Start(); await RefreshQuote(); };
            Closing += (s, e) => {
                if (!ExitRequested) { e.Cancel = true; Hide(); return; }
                closing = true; SaveSettings(); tray.Visible = false; tray.Dispose(); timer.Stop();
            };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { Hide(); } };
        }

        private Button Button(string text)
        {
            return new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(3, 0, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(190, 201, 218)), Background = new SolidColorBrush(Color.FromRgb(32, 43, 60)), BorderThickness = new Thickness(0) };
        }

        // Custom template avoids Windows' pale hover background obscuring white labels.
        private void ConfigureButton(Button button, string text, bool windowControl = false)
        {
            button.Content = text;
            button.Padding = new Thickness(8, 4, 8, 4);
            button.Margin = new Thickness(3, 0, 0, 0);
            button.Foreground = new SolidColorBrush(Color.FromRgb(220, 230, 245));
            button.Background = new SolidColorBrush(Color.FromRgb(32, 43, 60));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 80, 103));
            button.FontSize = 11;
            button.Cursor = Cursors.Hand;
            string template = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='#415067' BorderThickness='1' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#35435A'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#F4C66A'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>";
            if (windowControl)
            {
                // Mouse focus survives hide/minimize; use keyboard-only focus visuals for title buttons.
                template = template.Replace("<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#F4C66A'/></Trigger>", "");
                if (text == "×") { template = template.Replace("Value='#35435A'", "Value='#E87980'"); }
                button.FocusVisualStyle = (Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Control'><Setter Property='Template'><Setter.Value><ControlTemplate><Rectangle Margin='3' Stroke='#AED9FF' StrokeThickness='1' StrokeDashArray='2 2'/></ControlTemplate></Setter.Value></Setter></Style>");
            }
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(template);
        }

        private void BuildUi()
        {
            var outer = new Border { Background = new SolidColorBrush(Color.FromArgb(250, 24, 34, 50)), BorderBrush = new SolidColorBrush(Color.FromRgb(69, 81, 103)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(17, 12, 17, 12) };
            var root = new Grid();
            foreach (double size in new[] { 28.0, 60, 28 }) { root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size) }); }
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            var header = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            header.Children.Add(new TextBlock { Text = "●  国际黄金", Foreground = new SolidColorBrush(Color.FromRgb(244, 198, 106)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var hide = new Button(); ConfigureButton(hide, "−", true); hide.Background = Brushes.Transparent; hide.ToolTip = "最小化详情";
            header.Children.Add(hide); Grid.SetColumn(hide, 1);
            var closeDetails = new Button(); ConfigureButton(closeDetails, "×", true);
            closeDetails.Background = Brushes.Transparent;
            closeDetails.ToolTip = "关闭详情，小金继续运行";
            closeDetails.Click += (s, e) => { SaveSettings(); Hide(); };
            header.Children.Add(closeDetails); Grid.SetColumn(closeDetails, 2);
            root.Children.Add(header);
            header.MouseLeftButtonDown += (s, e) => {
                // Ignore any nested text inside header buttons when starting a drag.
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null && source != header)
                {
                    if (source is ButtonBase) { return; }
                    source = VisualTreeHelper.GetParent(source);
                }
                if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); ClampPosition(); SaveSettings(); }
            };
            hide.Click += (s, e) => { SaveSettings(); WindowState = WindowState.Minimized; };
            var priceArea = new Grid { Background = Brushes.Transparent };
            var priceRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            priceRow.Children.Add(new TextBlock { Text = "¥", Foreground = new SolidColorBrush(Color.FromRgb(244, 198, 106)), FontSize = 23, Margin = new Thickness(0, 8, 5, 0) });
            price.FontSize = 36; price.FontWeight = FontWeights.SemiBold; price.Text = "— —";
            priceRow.Children.Add(price);
            priceRow.Children.Add(new TextBlock { Text = "/ 克", Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), FontSize = 13, Margin = new Thickness(7, 21, 0, 0) });
            priceArea.Children.Add(priceRow);
            Grid.SetRow(priceArea, 1); root.Children.Add(priceArea);
            var changeRow = new Grid();
            changeRow.ColumnDefinitions.Add(new ColumnDefinition());
            change.FontSize = 11; change.VerticalAlignment = VerticalAlignment.Center; change.Text = "正在获取行情…"; change.TextTrimming = TextTrimming.CharacterEllipsis;
            changeRow.Children.Add(change);
            Grid.SetRow(changeRow, 2); root.Children.Add(changeRow);
            details.Visibility = Visibility.Collapsed; details.Margin = new Thickness(0, 14, 0, 8);
            BuildDetails(); Grid.SetRow(details, 3); root.Children.Add(details);
            status.Text = "连接数据源…"; status.FontSize = 10; status.Foreground = Brushes.LightSteelBlue; status.VerticalAlignment = VerticalAlignment.Bottom; status.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(status, 4); root.Children.Add(status);
            outer.Child = root; Content = outer;
        }

        private void BuildDetails()
        {
            foreach (double size in new[] { 34.0, 190, 28, 110 }) { details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size) }); }
            details.RowDefinitions.Add(new RowDefinition());
            var header = new Grid();
            header.Children.Add(new TextBlock { Text = "人民币折算走势", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var ranges = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            ConfigureButton(hourButton, "近1小时"); ConfigureButton(dayButton, "日内");
            hourButton.Click += (s, e) => { hourRange = true; DrawChart(); };
            dayButton.Click += (s, e) => { hourRange = false; DrawChart(); };
            ranges.Children.Add(hourButton); ranges.Children.Add(dayButton); header.Children.Add(ranges); details.Children.Add(header);
            chart.Background = new SolidColorBrush(Color.FromRgb(18, 28, 44));
            chartEmpty.Text = "正在读取分钟走势…"; chartEmpty.Foreground = Brushes.LightSteelBlue;
            chartEmpty.HorizontalAlignment = HorizontalAlignment.Center; chartEmpty.VerticalAlignment = VerticalAlignment.Center; chartEmpty.IsHitTestVisible = false;
            var chartGrid = new Grid { ClipToBounds = true }; chartGrid.Children.Add(chart); chartGrid.Children.Add(chartEmpty);
            Grid.SetRow(chartGrid, 1); details.Children.Add(chartGrid);
            chartCaption.Text = "分钟报价 × 当前汇率 · 人民币/克"; chartCaption.FontSize = 10; chartCaption.Foreground = Brushes.LightSteelBlue; chartCaption.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(chartCaption, 2); details.Children.Add(chartCaption);
            var stats = new UniformGrid { Columns = 3, Rows = 2 };
            foreach (string label in new[] { "日内最高 / 元/克", "日内最低 / 元/克", "日内振幅", "昨收折算 / 元/克", "国际金价 / 美元/盎司", "美元兑人民币" })
            {
                var panel = new StackPanel { Margin = new Thickness(0, 5, 4, 0) };
                panel.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Brushes.LightSteelBlue });
                var value = new TextBlock { Text = "—", FontSize = 16, Margin = new Thickness(0, 5, 0, 0) };
                panel.Children.Add(value); statistics.Add(value); stats.Children.Add(panel);
            }
            Grid.SetRow(stats, 3); details.Children.Add(stats);
            var footer = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
            var timeRow = new Grid();
            quoteTime.Text = "黄金报价 —"; quoteTime.FontSize = 10; quoteTime.VerticalAlignment = VerticalAlignment.Center; quoteTime.Foreground = Brushes.LightSteelBlue;
            timeRow.Children.Add(quoteTime);
            ConfigureButton(refreshButton, "刷新"); refreshButton.HorizontalAlignment = HorizontalAlignment.Right;
            refreshButton.Click += async (s, e) => await RefreshQuote(); timeRow.Children.Add(refreshButton);
            footer.Children.Add(timeRow);
            fxTime.Text = "汇率报价 —"; fxTime.FontSize = 10; fxTime.Foreground = Brushes.LightSteelBlue; fxTime.Margin = new Thickness(0, 3, 0, 0); footer.Children.Add(fxTime);
            var sourceRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            sourceRow.Children.Add(new TextBlock { Text = "来源：新浪财经 · 15秒刷新 · 红涨绿跌", FontSize = 10, Foreground = Brushes.LightSteelBlue, VerticalAlignment = VerticalAlignment.Center });
            var records = new Button(); ConfigureButton(records, "提醒记录"); records.HorizontalAlignment = HorizontalAlignment.Right;
            records.Click += (s, e) => { if (AlertHistoryRequested != null) { AlertHistoryRequested(); } };
            sourceRow.Children.Add(records); footer.Children.Add(sourceRow);
            footer.Children.Add(new TextBlock { Text = "人民币/克 = 美元/金衡盎司 × USD/CNY ÷ 31.1034768。走势、涨跌和高低价均用当前汇率折算；非金店售价。", FontSize = 10, Foreground = Brushes.LightSlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            Grid.SetRow(footer, 4); details.Children.Add(footer);
            chart.SizeChanged += (s, e) => DrawChart();
            chart.Cursor = Cursors.Cross;
            chartReadout.Child = chartReadoutText;
            chart.MouseMove += (s, e) => ShowChartPoint(e.GetPosition(chart));
            chart.MouseLeave += (s, e) => { chart.Children.Remove(chartGuide); chart.Children.Remove(chartReadout); };
        }
        private void ToggleDetails()
        {
            expanded = !expanded;
            details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            Width = expanded ? 506 : 326; Height = expanded ? 686 : 184;
            ClampPosition(); DrawChart();
        }

        // Keep the full widget on its current monitor after expansion or monitor changes.
        private void ClampPosition()
        {
            var source = PresentationSource.FromVisual(this);
            double scale = source == null || source.CompositionTarget == null ? 1.0 : source.CompositionTarget.TransformToDevice.M11;
            var area = Forms.Screen.FromPoint(new Drawing.Point((int)(Left * scale), (int)(Top * scale))).WorkingArea;
            Left = Math.Max(area.Left / scale, Math.Min(Left, area.Right / scale - Width));
            Top = Math.Max(area.Top / scale, Math.Min(Top, area.Bottom / scale - Height));
        }

        // Publish the current quote before requesting history so a slow history API cannot hide a good quote.
        private async Task RefreshQuote()
        {
            if (loading || closing) { return; }
            loading = true; refreshButton.IsEnabled = false;
            try
            {
                var next = await Task.Run(() => FetchQuote());
                if (closing) { return; }
                quote = next;
                history.RemoveAll(p => p.Item1.Date != quote.QuoteTime.Date || p.Item1 == quote.QuoteTime);
                history.Add(Tuple.Create(quote.QuoteTime, quote.Gold));
                ShowQuote(); SaveCache();
                // Gold and onshore FX have different sessions; stale FX does not mean gold is closed.
                var utc = DateTime.UtcNow;
                bool weekend = utc.DayOfWeek == DayOfWeek.Saturday || (utc.DayOfWeek == DayOfWeek.Sunday && utc.Hour < 21);
                if (GoldQuoteStale)
                {
                    status.Text = weekend ? "黄金报价未更新 · 周末可能休市" : "黄金报价延迟 · 查看源站时间";
                }
                else if (FxQuoteStale)
                {
                    status.Text = "黄金已更新 · 汇率沿用旧报价";
                }
                else { status.Text = "已更新 " + utc.AddHours(8).ToString("HH:mm:ss") + " · 15秒刷新"; }
                status.ToolTip = "黄金：" + quote.QuoteTime.ToString("yyyy-MM-dd HH:mm:ss") + "；汇率：" + quote.FxTime.ToString("yyyy-MM-dd HH:mm:ss") + "（北京时间）" + (FxQuoteStale ? "。人民币折算使用最后汇率，价格提醒暂缓。" : "");
                if (StatusUpdated != null) { StatusUpdated(status.Text); }
                if ((DateTime.UtcNow - lastHistoryFetch).TotalSeconds >= 60)
                {
                    lastHistoryFetch = DateTime.UtcNow;
                    try
                    {
                        var minutes = await Task.Run(() => FetchMinutes(next.QuoteTime.Date));
                        if (closing) { return; }
                        var merged = minutes.Concat(history).Where(p => p.Item1.Date == quote.QuoteTime.Date && p.Item1 <= quote.QuoteTime).GroupBy(p => p.Item1).Select(g => g.Last()).OrderBy(p => p.Item1).ToList();
                        history.Clear(); history.AddRange(merged); DrawChart();
                        if (minutes.Count == 0) { chartCaption.Text = "分钟历史暂不可用 · 正在积累实际报价"; }
                    }
                    catch (Exception ex) { Log("分钟历史", ex); chartCaption.Text = "分钟历史暂不可用 · 正在积累实际报价"; }
                }
            }
            catch (Exception ex)
            {
                Log("行情刷新", ex);
                status.Text = quote == null ? "连接失败 · 15秒后重试" : "连接失败 · 显示旧报价 · 15秒后重试";
                status.ToolTip = ex.Message;
                if (StatusUpdated != null) { StatusUpdated(status.Text); }
            }
            finally { loading = false; if (!closing) { refreshButton.IsEnabled = true; } }
        }

        // Restricted HTTP headers must use their dedicated .NET properties.
        private static string Download(string uri)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Referer = "https://finance.sina.com.cn/";
            request.UserAgent = "Mozilla/5.0";
            request.Timeout = 12000; request.ReadWriteTimeout = 12000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), System.Text.Encoding.GetEncoding(936)))
            {
                return reader.ReadToEnd();
            }
        }

        private static bool IsPositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static void Validate(Quote q)
        {
            if (q == null || !new[] { q.Gold, q.Previous, q.High, q.Low, q.Rate }.All(IsPositive) || q.High < q.Low || q.Rate < 1 || q.Rate > 30 || q.QuoteTime.Year < 2000 || q.FxTime.Year < 2000)
            {
                throw new FormatException("报价不完整或超出合理范围");
            }
        }

        private Quote FetchQuote()
        {
            string raw = Download("https://hq.sinajs.cn/?_=" + DateTime.UtcNow.Ticks + "&list=hf_XAU,fx_susdcny");
            var gm = Regex.Match(raw, "hq_str_hf_XAU=\"([^\"]+)\"");
            var fm = Regex.Match(raw, "hq_str_fx_susdcny=\"([^\"]+)\"");
            if (!gm.Success || !fm.Success) { throw new FormatException("数据源未返回完整行情"); }
            var g = gm.Groups[1].Value.Split(','); var f = fm.Groups[1].Value.Split(',');
            if (g.Length < 13 || f.Length < 18) { throw new FormatException("数据源格式发生变化"); }
            var ci = CultureInfo.InvariantCulture;
            var q = new Quote {
                Gold = double.Parse(g[0], ci), Previous = double.Parse(g[7], ci), High = double.Parse(g[4], ci), Low = double.Parse(g[5], ci), Rate = double.Parse(f[1], ci),
                QuoteTime = DateTime.ParseExact(g[12] + " " + g[6], "yyyy-MM-dd HH:mm:ss", ci),
                FxTime = DateTime.ParseExact(f[17] + " " + f[0], "yyyy-MM-dd HH:mm:ss", ci)
            };
            Validate(q);
            return q;
        }

        private List<Tuple<DateTime, double>> FetchMinutes(DateTime tradingDate)
        {
            string raw = Download("https://stock2.finance.sina.com.cn/futures/api/jsonp.php/var%20_data=/GlobalFuturesService.getGlobalFuturesMinLine?symbol=XAU");
            int start = raw.IndexOf("({", StringComparison.Ordinal), end = raw.LastIndexOf("})", StringComparison.Ordinal);
            if (start < 0 || end <= start) { throw new FormatException("分钟数据格式异常"); }
            var payload = new JavaScriptSerializer().Deserialize<MinutePayload>(raw.Substring(start + 1, end - start));
            if (payload == null || payload.minLine_1d == null) { throw new FormatException("分钟数据为空"); }
            var result = new List<Tuple<DateTime, double>>();
            foreach (var row in payload.minLine_1d)
            {
                // First minute includes extra trading-day metadata.
                if (row == null || row.Count < 6) { continue; }
                string timeText = row.Count >= 10 ? row[9] : row[5];
                string priceText = row.Count >= 10 ? row[5] : row[1];
                DateTime time; double value;
                if (DateTime.TryParseExact(timeText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out time) && time.Date == tradingDate && double.TryParse(priceText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && IsPositive(value))
                {
                    result.Add(Tuple.Create(time, value));
                }
            }
            return result;
        }

        // Reference prices and history all use the current FX rate; this is labelled in the UI.
        private void ShowQuote()
        {
            if (quote == null) { return; }
            double factor = quote.Rate / 31.1034768;
            double p = quote.Gold * factor, delta = (quote.Gold - quote.Previous) * factor, percent = (quote.Gold / quote.Previous - 1) * 100;
            price.Text = p.ToString("N2", CultureInfo.GetCultureInfo("zh-CN"));
            change.Text = delta.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " 元 (" + percent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%) 较昨收";
            change.Foreground = delta >= 0 ? Brushes.Salmon : Brushes.LightGreen;
            double[] values = { quote.High * factor, quote.Low * factor, (quote.High - quote.Low) / quote.Previous * 100, quote.Previous * factor, quote.Gold, quote.Rate };
            for (int i = 0; i < values.Length; i++) { statistics[i].Text = values[i].ToString(i == 5 ? "F4" : "N2", CultureInfo.GetCultureInfo("zh-CN")) + (i == 2 ? "%" : ""); }
            quoteTime.Text = "黄金报价 " + quote.QuoteTime.ToString("yyyy-MM-dd HH:mm:ss") + "（北京时间）";
            fxTime.Text = "汇率报价 " + quote.FxTime.ToString("yyyy-MM-dd HH:mm:ss") + "（北京时间）";
            tray.Text = "国际金价 ¥" + p.ToString("N2") + "/克";
            DrawChart();
            if (QuoteUpdated != null) { QuoteUpdated(); }
        }

        private void DrawChart()
        {
            chart.Children.Clear();
            if (!expanded || quote == null || chart.ActualWidth < 120 || chart.ActualHeight < 50) { return; }
            try
            {
                var points = history.Where(p => !hourRange || p.Item1 >= quote.QuoteTime.AddHours(-1)).OrderBy(p => p.Item1).ToList();
                chartPoints = points;
                dayButton.Background = new SolidColorBrush(hourRange ? Color.FromRgb(32, 43, 60) : Color.FromRgb(73, 81, 106));
                hourButton.Background = new SolidColorBrush(hourRange ? Color.FromRgb(73, 81, 106) : Color.FromRgb(32, 43, 60));
                chartEmpty.Visibility = points.Count >= 2 ? Visibility.Collapsed : Visibility.Visible;
                if (points.Count < 2) { chartEmpty.Text = "等待至少两条实际报价…"; return; }
                double factor = quote.Rate / 31.1034768;
                double min = points.Min(x => x.Item2) * factor, max = points.Max(x => x.Item2) * factor;
                double padding = Math.Max(0.05, (max - min) * 0.12); min -= padding; max += padding;
                double width = chart.ActualWidth - 68, height = chart.ActualHeight - 36;
                for (int i = 0; i < 3; i++)
                {
                    double y = 10 + height * i / 2;
                    chart.Children.Add(new Line { X1 = 8, X2 = width + 8, Y1 = y, Y2 = y, Stroke = Brushes.DarkSlateGray, StrokeThickness = 1 });
                    ChartLabel((max - (max - min) * i / 2).ToString("F2"), width + 14, y - 6);
                }
                // Calculate elapsed seconds as Double rather than passing Int64 ticks through Int32 overloads.
                double duration = Math.Max(1.0, (points.Last().Item1 - points.First().Item1).TotalSeconds);
                var line = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(244, 198, 106)), StrokeThickness = 2 };
                foreach (var point in points)
                {
                    line.Points.Add(new Point(8 + (point.Item1 - points[0].Item1).TotalSeconds / duration * width, 10 + (max - point.Item2 * factor) / (max - min) * height));
                }
                chart.Children.Add(line);
                ChartLabel(points[0].Item1.ToString("HH:mm"), 8, height + 18);
                ChartLabel(points.Last().Item1.ToString("HH:mm"), width - 24, height + 18);
                chartCaption.Text = "分钟报价 × 当前汇率 · 人民币/克 · 截至 " + points.Last().Item1.ToString("HH:mm:ss");
                if (chart.IsMouseOver) { ShowChartPoint(Mouse.GetPosition(chart)); }
            }
            catch (Exception ex) { Log("图表绘制", ex); chartEmpty.Text = "走势图暂不可用，请刷新"; chartEmpty.Visibility = Visibility.Visible; }
        }

        // Select the nearest actual sample by time; do not interpolate prices across missing minutes.
        private void ShowChartPoint(Point mouse)
        {
            if (quote == null || chartPoints.Count < 2 || chart.ActualWidth < 120) { return; }
            double width = chart.ActualWidth - 68;
            double ratio = Math.Max(0, Math.Min(1, (mouse.X - 8) / width));
            double span = Math.Max(1, (chartPoints.Last().Item1 - chartPoints[0].Item1).TotalSeconds);
            DateTime target = chartPoints[0].Item1.AddSeconds(ratio * span);
            var point = chartPoints.OrderBy(p => Math.Abs((p.Item1 - target).TotalSeconds)).First();
            double x = 8 + (point.Item1 - chartPoints[0].Item1).TotalSeconds / span * width;
            chartGuide.X1 = chartGuide.X2 = x; chartGuide.Y1 = 8; chartGuide.Y2 = chart.ActualHeight - 25;
            chartReadoutText.Text = point.Item1.ToString("MM-dd HH:mm:ss") + "\n" + (point.Item2 * quote.Rate / 31.1034768).ToString("F2") + " 元/克";
            chartReadout.ToolTip = "按当前汇率折算";
            chart.Children.Remove(chartGuide); chart.Children.Remove(chartReadout);
            chart.Children.Add(chartGuide); chart.Children.Add(chartReadout);
            Canvas.SetLeft(chartReadout, Math.Max(0, Math.Min(x + 10, chart.ActualWidth - 150)));
            Canvas.SetTop(chartReadout, Math.Max(6, Math.Min(mouse.Y - 58, chart.ActualHeight - 60)));
        }

        private void ChartLabel(string text, double left, double top)
        {
            var label = new TextBlock { Text = text, FontSize = 10, Foreground = Brushes.LightSteelBlue };
            Canvas.SetLeft(label, left); Canvas.SetTop(label, top); chart.Children.Add(label);
        }

        private string SettingsFile { get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cacheFile), "native-settings.json"); } }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, new JavaScriptSerializer().Serialize(new SavedState { Left = Left, Top = Top, Pinned = Topmost }));
            }
            catch (Exception ex) { Log("保存位置", ex); }
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsFile)) { return; }
            try
            {
                var state = new JavaScriptSerializer().Deserialize<SavedState>(File.ReadAllText(SettingsFile));
                if (state == null) { return; }
                if (!double.IsNaN(state.Left) && !double.IsInfinity(state.Left) && !double.IsNaN(state.Top) && !double.IsInfinity(state.Top)) { Left = state.Left; Top = state.Top; }
                Topmost = false;
            }
            catch (Exception ex) { Log("读取位置", ex); }
        }

        private void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cacheFile));
                // InvariantCulture keeps cache readable when Windows display language changes.
                File.WriteAllText(cacheFile, string.Join("|", new[] { quote.Gold.ToString("R", CultureInfo.InvariantCulture), quote.Previous.ToString("R", CultureInfo.InvariantCulture), quote.High.ToString("R", CultureInfo.InvariantCulture), quote.Low.ToString("R", CultureInfo.InvariantCulture), quote.Rate.ToString("R", CultureInfo.InvariantCulture), quote.QuoteTime.Ticks.ToString(CultureInfo.InvariantCulture), quote.FxTime.Ticks.ToString(CultureInfo.InvariantCulture) }));
            }
            catch (Exception ex) { Log("保存缓存", ex); }
        }

        private void LoadCache()
        {
            if (!File.Exists(cacheFile)) { return; }
            try
            {
                var a = File.ReadAllText(cacheFile).Split('|');
                if (a.Length != 7) { throw new FormatException("缓存字段不完整"); }
                var q = new Quote { Gold = double.Parse(a[0], CultureInfo.InvariantCulture), Previous = double.Parse(a[1], CultureInfo.InvariantCulture), High = double.Parse(a[2], CultureInfo.InvariantCulture), Low = double.Parse(a[3], CultureInfo.InvariantCulture), Rate = double.Parse(a[4], CultureInfo.InvariantCulture), QuoteTime = new DateTime(long.Parse(a[5], CultureInfo.InvariantCulture)), FxTime = new DateTime(long.Parse(a[6], CultureInfo.InvariantCulture)) };
                Validate(q); quote = q; ShowQuote(); status.Text = "本地缓存 · 正在连接更新";
            }
            catch (Exception ex) { Log("读取缓存", ex); }
        }

        private void Log(string stage, Exception exception)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cacheFile), "native-error.log");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > 512000) { File.WriteAllText(path, ""); }
                File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " [" + stage + "] " + exception + Environment.NewLine);
            }
            catch (IOException) { /* Logging must not interrupt live quote display. */ }
            catch (UnauthorizedAccessException) { /* Managed PCs may have a read-only state folder. */ }
        }
    }

    public sealed class App : Application
    {
        // Embedded branding keeps the EXE portable, including its taskbar and notification-area icons.
        public static System.Windows.Media.Imaging.BitmapSource WindowIcon
        {
            get
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                {
                    var image = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    image.Freeze(); return image;
                }
            }
        }

        public static Drawing.Icon CreateTrayIcon()
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            using (var icon = new Drawing.Icon(stream)) { return (Drawing.Icon)icon.Clone(); }
        }

        // A single instance avoids concurrent refresh loops and cache writers.
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--shutdown-for-maintenance") { return ShutdownForMaintenance(); }
            using (var mutex = new Mutex(false, "Local\\GoldWidget_RMB_Native"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { MessageBox.Show("鲸小金已在运行，请从任务栏或系统托盘恢复。", "鲸小金"); return 0; }
                try
                {
                    var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var data = new MainWindow();
                    var pet = new PetWindow(data);
                    // Path-specific signal: uninstalling one copy must not close a different portable copy.
                    using (var shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName))
                    {
                        var listener = ThreadPool.RegisterWaitForSingleObject(shutdown, (state, timedOut) => {
                            app.Dispatcher.BeginInvoke(new Action(() => pet.Close()));
                        }, null, Timeout.Infinite, true);
                        try { data.Show(); data.Hide(); app.Run(pet); }
                        finally { listener.Unregister(null); }
                    }
                }
                catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "鲸小金"); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }

        private static string ShutdownEventName
        {
            get
            {
                string path = System.Reflection.Assembly.GetExecutingAssembly().Location.ToUpperInvariant();
                using (var hash = System.Security.Cryptography.SHA256.Create())
                {
                    return "Local\\WhaleGoldShutdown_" + BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path))).Replace("-", "");
                }
            }
        }

        // Run before MSI file validation. Saving state and disposing the tray follows the ordinary close path.
        private static int ShutdownForMaintenance()
        {
            try
            {
                using (var signal = EventWaitHandle.OpenExisting(ShutdownEventName)) { signal.Set(); }
                using (var mutex = Mutex.OpenExisting("Local\\GoldWidget_RMB_Native"))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(10000); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { return 1; }
                    mutex.ReleaseMutex();
                }
            }
            catch (WaitHandleCannotBeOpenedException) { return 0; }
            return 0;
        }
    }
}
