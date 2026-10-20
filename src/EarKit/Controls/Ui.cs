using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
namespace EarKit.Controls;

/// 代码构建界面用的小工具（所有主题色均用动态资源，跟随深浅色切换）
public static class Ui
{
    public static TextBlock T(string text, string? style = null, double? size = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style != null) t.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (size != null) t.FontSize = size.Value;
        return t;
    }
    public static TextBlock Plain(string text, double size = 14, FontWeight? w = null, double opacity = 1)
        => new() { Text = text, FontSize = size, FontWeight = w ?? FontWeights.Normal, Opacity = opacity, TextWrapping = TextWrapping.Wrap };
    public static T Fg<T>(T e, string key) where T : FrameworkElement
    {
        if (e is TextBlock tb) tb.SetResourceReference(TextBlock.ForegroundProperty, key);
        else if (e is Control c) c.SetResourceReference(Control.ForegroundProperty, key);
        else if (e is IconElement ic) ic.SetResourceReference(IconElement.ForegroundProperty, key);
        return e;
    }
    public static SymbolIcon Icon(SymbolRegular s, double size = 20, bool filled = false) => new() { Symbol = s, FontSize = size, Filled = filled };

    public static Border Panel(UIElement child, string style = "Panel", Thickness? margin = null)
    {
        var b = new Border { Child = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (margin != null) b.Margin = margin.Value;
        return b;
    }

    public static TextBlock Section(string title) => T(title, "SectionTitle");

    public static StackPanel Header(string title, string subtitle)
    {
        var sp = new StackPanel();
        sp.Children.Add(T(title, "PageTitle"));
        sp.Children.Add(T(subtitle, "PageSubtitle"));
        return sp;
    }

    /// 设置行：图标 | 标题+说明 | 右侧控件
    public static Border Row(SymbolRegular icon, string title, string? desc, UIElement? right, UIElement? below = null)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = Icon(icon, 20);
        ic.Margin = new Thickness(2, 0, 16, 0); ic.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(T(title, "RowTitle"));
        if (!string.IsNullOrEmpty(desc)) sp.Children.Add(T(desc!, "RowDesc"));
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        if (right != null)
        {
            if (right is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(16, 0, 0, 0); }
            Grid.SetColumn(right, 2); g.Children.Add(right);
        }
        UIElement content = g;
        if (below != null)
        {
            var outer = new StackPanel();
            outer.Children.Add(g);
            if (below is FrameworkElement bf) bf.Margin = new Thickness(38, 12, 0, 0);
            outer.Children.Add(below);
            content = outer;
        }
        return Panel(content, "Row");
    }

    public static ToggleSwitch Toggle(Action<bool> changed)
    {
        var t = new ToggleSwitch();
        t.Checked += (_, _) => changed(true);
        t.Unchecked += (_, _) => changed(false);
        return t;
    }

    public static ComboBox Combo(IEnumerable<string> items, double width = 200)
    {
        var c = new ComboBox { MinWidth = width };
        foreach (var i in items) c.Items.Add(i);
        return c;
    }

    public static Wpf.Ui.Controls.Button Btn(string text, SymbolRegular? icon = null, Action? click = null, ControlAppearance ap = ControlAppearance.Secondary)
    {
        var b = new Wpf.Ui.Controls.Button { Content = text, Appearance = ap };
        if (icon != null) b.Icon = new SymbolIcon { Symbol = icon.Value };
        if (click != null) b.Click += (_, _) => click();
        return b;
    }

    public static RadioButton ModeCard(string group, SymbolRegular icon, string title, string? sub = null, double width = 150)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var ic = Icon(icon, 26); ic.HorizontalAlignment = HorizontalAlignment.Center; ic.Margin = new Thickness(0, 0, 0, 6);
        sp.Children.Add(ic);
        sp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        if (sub != null) sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var rb = new RadioButton { GroupName = group, Content = sp, Width = width };
        rb.SetResourceReference(FrameworkElement.StyleProperty, "ModeCard");
        return rb;
    }

    public static RadioButton Pill(string group, string text)
    {
        var rb = new RadioButton { GroupName = group, Content = new TextBlock { Text = text, FontSize = 13 } };
        rb.SetResourceReference(FrameworkElement.StyleProperty, "PillRadio");
        return rb;
    }

    public static SymbolRegular NoiseIcon(string mode) => mode switch
    {
        "anc" or "anc1" or "anc2" => SymbolRegular.HeadphonesSoundWave24,
        "ambient" => SymbolRegular.Speaker224,
        "adaptive" => SymbolRegular.Sparkle24,
        "wind" => SymbolRegular.WeatherMoon24,
        _ => SymbolRegular.SpeakerMute24,
    };
    public static string NoiseDesc(string mode) => mode switch
    {
        "anc" or "anc1" or "anc2" => "隔绝外界噪音",
        "ambient" => "听见周围声音",
        "adaptive" => "随环境自动调节",
        "wind" => "降低风噪",
        _ => "不做降噪处理",
    };

    /// 确认对话框（Fluent 风格）
    public static async Task<bool> Confirm(string title, string text, string ok = "确定", string cancel = "取消", bool danger = false)
    {
        try
        {
            var mb = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                PrimaryButtonText = ok,
                CloseButtonText = cancel,
                PrimaryButtonAppearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary,
            };
            var owner = Application.Current.MainWindow;
            if (owner != null && owner.IsVisible) { mb.Owner = owner; mb.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            var r = await mb.ShowDialogAsync(true, default);
            return r == Wpf.Ui.Controls.MessageBoxResult.Primary;
        }
        catch
        {
            return System.Windows.MessageBox.Show(text, title, System.Windows.MessageBoxButton.OKCancel, MessageBoxImage.Question) == System.Windows.MessageBoxResult.OK;
        }
    }

    public static void Show(UIElement e, bool v) => e.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
}
