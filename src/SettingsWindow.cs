using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static PeriDock.Visuals;

namespace PeriDock;

internal sealed class SettingsWindow : Window
{
    readonly Controller controller;
    readonly CheckBox lowAlert;
    readonly StackPanel lowDevices;
    readonly Dictionary<string, Slider> lowThresholds = [];
    readonly Dictionary<string, TextBlock> lowValues = [];
    string lowKey = "";
    readonly ComboBox outputDevice, inputDevice;
    readonly Slider outputVolume, micVolume;
    readonly CheckBox outputMute, micMute;
    readonly TextBlock outputPercent, micPercent;
    readonly Dictionary<AudioFlow, double> savedVolume = [];
    readonly CheckBox ancEnabled;
    readonly Button ancMode, ambientMode;
    readonly List<Button> ancLevels = [];
    readonly StackPanel ancSection;
    readonly TextBlock ancStatus;
    readonly StackPanel dpiSection, dpiControls;
    readonly TextBlock dpiTitle;
    readonly TextBlock dpiReadback;
    readonly TextBox dpiValue;
    readonly Button dpiApply;
    readonly TextBlock footer;
    bool loading;
    bool ancBusy;
    int lastAncMode = 1;

    internal SettingsWindow(Controller controller)
    {
        this.controller = controller;
        Title = Program.AppName + " · 外设设置";
        Width = 760; Height = 760; MinWidth = 700; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png")));
        var frame = WindowFrame(this);
        var grid = new Grid(); frame.Child = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        var header = new Grid { Margin = new Thickness(20, 0, 10, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Brand());
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        var close = Command("x", "关闭", Hide, true);
        Grid.SetColumn(close, 1); header.Children.Add(close);
        grid.Children.Add(new Border { Child = header, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });

        var content = new StackPanel { Margin = new Thickness(24, 10, 24, 16) };
        var scroll = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);

        content.Children.Add(SectionTitle("低电量提醒"));
        lowAlert = new CheckBox { Content = "启用低电量提醒", IsChecked = controller.Settings.LowBatteryAlert,
            Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        lowAlert.Checked += (_, _) => SaveLowAlert();
        lowAlert.Unchecked += (_, _) => SaveLowAlert();
        content.Children.Add(Inline(lowAlert));
        content.Children.Add(lowDevices = new StackPanel());
        content.Children.Add(Inline(Command("battery-full", "查看电池使用统计", controller.ShowBatteryUsage)));

        content.Children.Add(SectionTitle("耳机与麦克风"));
        outputDevice = DeviceCombo();
        outputVolume = VolumeSlider();
        outputMute = new CheckBox { Content = "静音", Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        outputPercent = PercentLabel();
        content.Children.Add(LabeledRow("输出设备", outputDevice));
        content.Children.Add(Inline(Text("音量", 13, Muted), outputVolume, outputMute, outputPercent));
        outputDevice.SelectionChanged += (_, _) => { if (!loading) { LoadVolume(AudioFlow.Render); SetDefault(AudioFlow.Render); } };
        outputVolume.ValueChanged += (_, _) => { UpdatePercent(AudioFlow.Render); if (!loading) SetVolume(AudioFlow.Render); };
        outputMute.Checked += (_, _) => { if (!loading) SetMute(AudioFlow.Render); };
        outputMute.Unchecked += (_, _) => { if (!loading) SetMute(AudioFlow.Render); };

        inputDevice = DeviceCombo();
        micVolume = VolumeSlider();
        micMute = new CheckBox { Content = "静音", Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        micPercent = PercentLabel();
        content.Children.Add(LabeledRow("麦克风音源", inputDevice));
        content.Children.Add(Inline(Text("音量", 13, Muted), micVolume, micMute, micPercent));
        inputDevice.SelectionChanged += (_, _) => { if (!loading) { LoadVolume(AudioFlow.Capture); SetInputDefault(); } };
        micVolume.ValueChanged += (_, _) => { UpdatePercent(AudioFlow.Capture); if (!loading) SetVolume(AudioFlow.Capture); };
        micMute.Checked += (_, _) => { if (!loading) SetMute(AudioFlow.Capture); };
        micMute.Unchecked += (_, _) => { if (!loading) SetMute(AudioFlow.Capture); };
        content.Children.Add(Text("选择麦克风后，系统默认输入设备会同步切换。", 12, Muted));

        ancSection = new StackPanel();
        ancSection.Children.Add(SectionTitle("旋风黑鲨 V3 Pro · 降噪"));
        ancEnabled = new CheckBox { Content = "主动降噪", Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        ancEnabled.Click += (_, _) => _ = ChangeAnc(ancEnabled.IsChecked == true ? lastAncMode : 0, CurrentAncLevel());
        ancStatus = Text("正在读取耳机状态…", 12, Muted);
        ancSection.Children.Add(Inline(ancEnabled, ancStatus));
        ancMode = Choice("主动降噪", () => _ = ChangeAnc(1, CurrentAncLevel()));
        ambientMode = Choice("环境感知", () => _ = ChangeAnc(80, CurrentAncLevel()));
        ancSection.Children.Add(Inline(ancMode, ambientMode));
        var levels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        for (var level = 1; level <= 4; level++)
        {
            var value = level;
            var button = Choice(value.ToString(), () => _ = ChangeAnc(CurrentAncMode(), value));
            button.Width = 82; button.Margin = new Thickness(0, 0, 8, 0);
            ancLevels.Add(button); levels.Children.Add(button);
        }
        ancSection.Children.Add(levels);
        ancSection.Children.Add(Text("设置直接写入耳机并回读确认；关闭应用后仍保留在耳机上。", 12, Muted));
        content.Children.Add(ancSection);

        dpiSection = new StackPanel();
        dpiTitle = SectionTitle("鼠标 · DPI");
        dpiSection.Children.Add(dpiTitle);
        dpiReadback = Text("当前未知", 13, Muted);
        dpiValue = new TextBox
        {
            Width = 100, Height = 34, Padding = new Thickness(8, 5, 8, 5),
            Background = Brush("#30353A"), Foreground = Ink, BorderBrush = Line,
            FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center
        };
        dpiApply = Command("check", "应用", ApplyDpi, true);
        dpiSection.Children.Add(Inline(dpiReadback));
        dpiControls = new StackPanel();
        dpiControls.Children.Add(Inline(Text("DPI", 13, Muted), dpiValue, dpiApply,
            Preset(400), Preset(800), Preset(1600), Preset(3200)));
        dpiSection.Children.Add(dpiControls);
        content.Children.Add(dpiSection);

        footer = Text("", 12, Muted);
        var bottom = new Border
        {
            Child = new Grid { Children = { footer } }, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 0, 24, 0)
        };
        Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
        Closing += (_, e) => { if (!controller.Exiting) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Hide(); };
        LoadAudio();
        Update();
    }

    internal void Update()
    {
        loading = true;
        lowAlert.IsChecked = controller.Settings.LowBatteryAlert;
        EnsureThresholdRows();
        foreach (var state in controller.States.Where(s => s.Supported))
        {
            var threshold = controller.Settings.ThresholdFor(state.Id, state.Kind);
            lowThresholds[state.Id].Value = threshold;
            lowValues[state.Id].Text = threshold.ToString();
        }
        loading = false;

        var mouse = controller.DpiTarget is { } target ? controller.States.FirstOrDefault(s => s.Id == target.Id) : null;
        if (controller.DpiTarget is null)
        {
            dpiTitle.Text = "鼠标 · DPI";
            dpiReadback.Text = controller.HasAnyMouse ? "该鼠标暂未适配" : "未检测到鼠标";
            dpiControls.Visibility = Visibility.Collapsed;
        }
        else
        {
            dpiTitle.Text = controller.DpiTarget.Name + " · DPI";
            dpiReadback.Text = mouse is null ? "当前未知" : "当前 " + mouse.DpiText;
            dpiControls.Visibility = Visibility.Visible;
            dpiControls.IsEnabled = mouse?.Connected != false;
            if (dpiValue is not null && !dpiValue.IsKeyboardFocusWithin)
                dpiValue.Text = mouse?.DpiX?.ToString() ?? "";
            dpiApply.IsEnabled = mouse?.Connected != false;
        }

        ancSection.Visibility = controller.HasHeadsetAncDevice ? Visibility.Visible : Visibility.Collapsed;
        var anc = controller.HeadsetAnc;
        if (anc is null || !anc.Success)
        {
            ancStatus.Text = anc?.Message ?? "耳机未连接";
            ancEnabled.IsEnabled = false; ancMode.IsEnabled = false; ambientMode.IsEnabled = false;
            foreach (var b in ancLevels) b.IsEnabled = false;
        }
        else
        {
            ancEnabled.IsEnabled = !ancBusy;
            ancMode.IsEnabled = !ancBusy; ambientMode.IsEnabled = !ancBusy;
            foreach (var b in ancLevels) b.IsEnabled = !ancBusy;
            ancEnabled.IsChecked = anc.Status != 0;
            if (anc.Status != 0) lastAncMode = anc.Status;
            ancStatus.Text = anc.Status switch
            {
                1 => $"已开启 · 等级 {anc.Level}",
                80 => $"环境感知 · 等级 {anc.Level}",
                _ => "已关闭"
            };
            UpdateChoices(anc);
        }
        footer.Text = ancBusy ? "正在写入耳机设置…" : "设置会立即生效；低电量提醒只在越过阈值时出现。";
    }

    void UpdateChoices(DeviceReader.AncReading anc)
    {
        StyleChoice(ancMode, anc.Status == 1);
        StyleChoice(ambientMode, anc.Status == 80);
        for (var i = 0; i < ancLevels.Count; i++) StyleChoice(ancLevels[i], anc.Level == i + 1);
        ancEnabled.Foreground = anc.Status != 0 ? Green : Ink;
        ancMode.Foreground = anc.Status == 1 ? Ink : Muted;
        ambientMode.Foreground = anc.Status == 80 ? Ink : Muted;
    }

    async Task ChangeAnc(int status, int level)
    {
        if (ancBusy) return;
        ancBusy = true; Update();
        try
        {
            var ok = await controller.SetHeadsetAnc(status, level);
            footer.Text = ok ? "耳机已确认新的降噪设置。" : "耳机没有确认设置，请检查连接。";
        }
        finally { ancBusy = false; Update(); }
    }

    int CurrentAncMode() => controller.HeadsetAnc is { Success: true } anc && anc.Status != 0 ? anc.Status : lastAncMode;
    int CurrentAncLevel() => controller.HeadsetAnc is { Success: true } anc ? anc.Level : 1;

    void LoadAudio()
    {
        loading = true;
        try
        {
            var outputs = AudioControl.List(AudioFlow.Render);
            var inputs = AudioControl.List(AudioFlow.Capture);
            outputDevice.ItemsSource = outputs;
            inputDevice.ItemsSource = inputs;
            outputDevice.SelectedItem = outputs.FirstOrDefault(d => d.Name.Contains("BlackShark", StringComparison.OrdinalIgnoreCase))
                ?? outputs.FirstOrDefault(d => d.IsDefault) ?? outputs.FirstOrDefault();
            inputDevice.SelectedItem = inputs.FirstOrDefault(d => d.Name.Contains("BlackShark", StringComparison.OrdinalIgnoreCase))
                ?? inputs.FirstOrDefault(d => d.IsDefault) ?? inputs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            footer.Text = "音频设备读取失败：" + ex.Message;
        }
        finally { loading = false; }
        LoadVolume(AudioFlow.Render); LoadVolume(AudioFlow.Capture);
    }

    internal void ReloadAudio() => LoadAudio();

    void LoadVolume(AudioFlow flow)
    {
        var combo = flow == AudioFlow.Render ? outputDevice : inputDevice;
        var slider = flow == AudioFlow.Render ? outputVolume : micVolume;
        var mute = flow == AudioFlow.Render ? outputMute : micMute;
        if (combo.SelectedItem is not AudioEndpoint endpoint) return;
        loading = true;
        try
        {
            var volume = AudioControl.Volume(endpoint.Id, flow);
            slider.Value = (volume ?? 0) * 100;
            mute.IsChecked = AudioControl.Muted(endpoint.Id, flow) ?? false;
            if (volume > 0) savedVolume[flow] = volume.Value * 100;
        }
        finally { loading = false; }
        UpdatePercent(flow);
    }

    void SetVolume(AudioFlow flow)
    {
        var combo = flow == AudioFlow.Render ? outputDevice : inputDevice;
        var slider = flow == AudioFlow.Render ? outputVolume : micVolume;
        if (combo.SelectedItem is not AudioEndpoint endpoint) return;
        var value = slider.Value;
        var ok = AudioControl.SetVolume(endpoint.Id, flow, (float)(value / 100d));
        if (ok)
        {
            if (value > 0) savedVolume[flow] = value;
            else savedVolume.Remove(flow);
            footer.Text = "";
        }
        else footer.Text = "音量写入失败，请检查音频设备权限。";
    }

    void SetMute(AudioFlow flow)
    {
        var combo = flow == AudioFlow.Render ? outputDevice : inputDevice;
        var slider = flow == AudioFlow.Render ? outputVolume : micVolume;
        var mute = flow == AudioFlow.Render ? outputMute : micMute;
        if (combo.SelectedItem is not AudioEndpoint endpoint) return;
        var target = mute.IsChecked == true;
        if (target)
        {
            if (slider.Value > 0) savedVolume[flow] = slider.Value;
            if (AudioControl.SetMuted(endpoint.Id, flow, true)) { footer.Text = ""; return; }
            var previous = slider.Value;
            SetSlider(slider, 0);
            if (AudioControl.SetVolume(endpoint.Id, flow, 0f))
            {
                footer.Text = "该设备不支持系统静音，已自动切到 0 音量。";
            }
            else
            {
                SetSlider(slider, previous);
                SetChecked(mute, false);
                footer.Text = "静音和音量都无法写入，请检查音频设备权限。";
            }
            return;
        }
        var cleared = AudioControl.SetMuted(endpoint.Id, flow, false);
        if (cleared && (slider.Value > 0 || !savedVolume.ContainsKey(flow))) { footer.Text = ""; return; }
        var restore = savedVolume.GetValueOrDefault(flow, 50);
        SetSlider(slider, restore);
        if (AudioControl.SetVolume(endpoint.Id, flow, (float)(restore / 100d)))
        {
            savedVolume[flow] = restore;
            footer.Text = cleared ? "" : "已恢复静音前的音量。";
        }
        else
        {
            SetChecked(mute, true);
            footer.Text = "取消静音失败，请检查音频设备权限。";
        }
    }

    void UpdatePercent(AudioFlow flow)
    {
        var slider = flow == AudioFlow.Render ? outputVolume : micVolume;
        var label = flow == AudioFlow.Render ? outputPercent : micPercent;
        label.Text = Math.Round(slider.Value) + "%";
    }

    void SetSlider(Slider slider, double value)
    {
        loading = true;
        try { slider.Value = value; } finally { loading = false; }
    }

    void SetChecked(CheckBox box, bool value)
    {
        loading = true;
        try { box.IsChecked = value; } finally { loading = false; }
    }

    static TextBlock PercentLabel()
    {
        var label = Text("0%", 13, Muted);
        label.MinWidth = 46; label.TextAlignment = TextAlignment.Right;
        return label;
    }

    void SetInputDefault()
    {
        SetDefault(AudioFlow.Capture);
    }

    void SetDefault(AudioFlow flow)
    {
        var combo = flow == AudioFlow.Render ? outputDevice : inputDevice;
        if (combo.SelectedItem is AudioEndpoint endpoint && !AudioControl.SetDefault(endpoint.Id, flow))
            footer.Text = "默认设备切换失败，请检查权限。";
    }

    void ApplyDpi()
    {
        if (!int.TryParse(dpiValue.Text, out var dpi))
        {
            footer.Text = "请输入 50–35000 之间的 DPI。"; return;
        }
        _ = ApplyDpiAsync(dpi);
    }

    async Task ApplyDpiAsync(int dpi)
    {
        dpiApply.IsEnabled = false;
        var result = await controller.SetMouseDpi(dpi);
        footer.Text = result.Message;
        dpiApply.IsEnabled = true;
        Update();
    }

    void SaveLowAlert()
    {
        if (loading) return;
        controller.UpdateSettings(controller.Settings with
        {
            LowBatteryAlert = lowAlert.IsChecked == true
        });
    }

    void SaveThreshold(string deviceId, int value)
        => controller.UpdateSettings(controller.Settings.WithThreshold(deviceId, value));

    void EnsureThresholdRows()
    {
        var supported = controller.States.Where(s => s.Supported).ToList();
        var key = string.Join("|", supported.Select(s => s.Id));
        if (key == lowKey) return;
        lowKey = key;
        lowDevices.Children.Clear();
        lowThresholds.Clear();
        lowValues.Clear();
        foreach (var state in supported)
        {
            var device = state;
            var slider = new Slider
            {
                Minimum = 5, Maximum = 80, TickFrequency = 5, IsSnapToTickEnabled = true,
                Width = 230, VerticalAlignment = VerticalAlignment.Center, Foreground = Green
            };
            var value = Text("", 13, Ink, FontWeights.SemiBold);
            lowThresholds[device.Id] = slider;
            lowValues[device.Id] = value;
            slider.ValueChanged += (_, _) =>
            {
                if (loading) return;
                value.Text = ((int)slider.Value).ToString();
                SaveThreshold(device.Id, (int)slider.Value);
            };
            lowDevices.Children.Add(Inline(Text(device.ShortName, 13, Muted), Text("低于", 13, Muted),
                slider, value, Text("% 时提醒", 13, Muted)));
        }
        if (controller.States.Count == 0)
            lowDevices.Children.Add(Text("未发现已适配外设", 13, Muted));
    }

    Button Preset(int value) => Choice(value.ToString(), () =>
    {
        dpiValue.Text = value.ToString();
        _ = ApplyDpiAsync(value);
    });

    static TextBlock SectionTitle(string text)
    {
        var title = Text(text, 16, Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 22, 0, 10);
        return title;
    }

    static StackPanel Inline(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var item in items) { if (item is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(item); }
        return row;
    }

    static StackPanel LabeledRow(string label, ComboBox combo)
        => Inline(Text(label, 13, Muted), combo);

    static ComboBox DeviceCombo()
    {
        var combo = new ComboBox
        {
            Width = 330, Height = 34, Padding = new Thickness(6, 3, 6, 3),
            Background = Brush("#30353A"), Foreground = Ink, BorderBrush = Line,
            ItemTemplate = ComboItemTemplate, VerticalContentAlignment = VerticalAlignment.Center,
            Template = ComboTemplate, ItemContainerStyle = ComboItemStyle
        };
        return combo;
    }

    static readonly DataTemplate ComboItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <TextBlock Text="{Binding DisplayName}" Foreground="#F2F3F5" TextTrimming="CharacterEllipsis"/>
        </DataTemplate>
        """);

    static readonly ControlTemplate ComboTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Grid>
            <ToggleButton x:Name="toggle" Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border Background="#30353A" BorderBrush="#3B4046" BorderThickness="1" CornerRadius="4">
                    <Path Data="M 0 0 L 5 5 L 10 0" Stroke="#A5ABB3" StrokeThickness="1.5"
                          HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"/>
                  </Border>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              Margin="10,0,30,0" VerticalAlignment="Center" IsHitTestVisible="False"/>
            <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom"
                   AllowsTransparency="True" Focusable="False">
              <Border Background="#2A2E32" BorderBrush="#50565E" BorderThickness="1" CornerRadius="4"
                      MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                <ScrollViewer MaxHeight="220">
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
        </ControlTemplate>
        """);

    static readonly Style ComboItemStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBoxItem">
          <Setter Property="Background" Value="#2A2E32"/>
          <Setter Property="Foreground" Value="#F2F3F5"/>
          <Setter Property="Padding" Value="10,8"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ComboBoxItem">
                <Border x:Name="row" Background="{TemplateBinding Background}" CornerRadius="3">
                  <ContentPresenter Margin="{TemplateBinding Padding}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsHighlighted" Value="True">
                    <Setter TargetName="row" Property="Background" Value="#3B4046"/>
                  </Trigger>
                  <Trigger Property="IsSelected" Value="True">
                    <Setter TargetName="row" Property="Background" Value="#2E3D34"/>
                    <Setter Property="Foreground" Value="#2DB578"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    static Slider VolumeSlider() => new()
    {
        Minimum = 0, Maximum = 100, Width = 210, VerticalAlignment = VerticalAlignment.Center,
        Foreground = Green, IsMoveToPointEnabled = true
    };

    static Button Choice(string label, Action action)
    {
        var text = Text(label, 13, Ink);
        var button = new Button
        {
            Content = text, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0),
            Background = Brush("#30353A"), BorderBrush = Line, BorderThickness = new Thickness(1),
            Template = ButtonTemplate, Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    static void StyleChoice(Button button, bool selected)
    {
        button.BorderBrush = selected ? Green : Line;
        button.Background = selected ? Brush("#2E3D34") : Brush("#30353A");
    }
}
