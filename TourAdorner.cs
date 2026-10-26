using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RpVoiceSwitcher;

/// <summary>
/// Rundgang: dunkelt das Fenster ab, lässt den gerade erklärten Bereich frei (mit orangem Rahmen)
/// und zeigt daneben eine Karte mit Text und Weiter/Zurück. Liegt als Adorner über dem Fensterinhalt,
/// passt sich also beim Vergrößern automatisch an. Der freie Bereich bleibt anklickbar.
/// </summary>
public sealed class TourAdorner : Adorner
{
    private readonly Canvas _canvas = new();
    private readonly Path _dim;
    private readonly Border _frame;
    private readonly Border _card;
    private readonly TextBlock _title, _text, _counter;
    private readonly Button _back, _next, _close;

    /// <summary>Liefert den hervorgehobenen Bereich (relativ zum Fensterinhalt) oder null = Karte in der Mitte.</summary>
    public Func<Rect?>? HoleProvider { get; set; }

    public event Action? NextClicked, BackClicked, CloseClicked;

    public TourAdorner(UIElement adorned) : base(adorned)
    {
        var accent = (Brush)Application.Current.FindResource("Accent");
        var card = (Brush)Application.Current.FindResource("Card");
        var dim = (Brush)Application.Current.FindResource("FgDim");

        _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0xB8, 0x08, 0x08, 0x0C)) };
        _frame = new Border { BorderBrush = accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), IsHitTestVisible = false };

        _counter = new TextBlock { Foreground = dim, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(_counter, "TourCounter");
        _close = new Button { Content = "✕", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Rundgang beenden (Esc)" };
        AutomationProperties.SetAutomationId(_close, "TourClose");
        AutomationProperties.SetName(_close, "Rundgang beenden");
        _close.Click += (_, _) => CloseClicked?.Invoke();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_close, Dock.Right);
        head.Children.Add(_close);
        head.Children.Add(_counter);

        _title = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        AutomationProperties.SetAutomationId(_title, "TourTitle");
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetAutomationId(_text, "TourText");

        _back = new Button { Content = "← Zurück", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(_back, "TourBack");
        _back.Click += (_, _) => BackClicked?.Invoke();
        _next = new Button { Content = "Weiter →", Padding = new Thickness(14, 5, 14, 5), Background = accent, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetAutomationId(_next, "TourNext");
        _next.Click += (_, _) => NextClicked?.Invoke();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(_title);
        stack.Children.Add(_text);
        stack.Children.Add(buttons);
        _card = new Border
        {
            Background = card, BorderBrush = accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 12, 18, 14), Width = 400, Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.6 },
        };
        AutomationProperties.SetAutomationId(_card, "TourCard");

        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_card);
        AddVisualChild(_canvas);
    }

    public void SetStep(string title, string text, int index, int count)
    {
        _title.Text = title;
        _text.Text = text;
        _counter.Text = $"Rundgang · {index + 1} von {count}";
        _back.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = index == count - 1 ? "Fertig" : index == 0 ? "Los geht's →" : "Weiter →";
        InvalidateMeasure();
        InvalidateArrange();
        Dispatcher.InvokeAsync(() => _next.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _canvas;

    protected override Size MeasureOverride(Size constraint)
    {
        var size = AdornedElement.RenderSize;
        _canvas.Measure(size);
        _card.Measure(new Size(400, double.PositiveInfinity));
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var full = new Rect(finalSize);
        Rect? hole = null;
        try { hole = HoleProvider?.Invoke(); } catch { /* Element gerade nicht sichtbar */ }
        if (hole is Rect h0 && (h0.IsEmpty || h0.Width < 1 || h0.Height < 1)) hole = null;

        Geometry dimGeo = new RectangleGeometry(full);
        if (hole is Rect h)
        {
            h.Inflate(6, 6);
            h.Intersect(full);
            hole = h;
            dimGeo = new CombinedGeometry(GeometryCombineMode.Exclude, dimGeo, new RectangleGeometry(h, 8, 8));
            _frame.Width = h.Width; _frame.Height = h.Height;
            Canvas.SetLeft(_frame, h.Left); Canvas.SetTop(_frame, h.Top);
            _frame.Visibility = Visibility.Visible;
        }
        else _frame.Visibility = Visibility.Collapsed;
        _dim.Data = dimGeo;

        var cs = _card.DesiredSize;
        double x, y;
        if (hole is Rect r)
        {
            const double gap = 14;
            if (r.Bottom + gap + cs.Height <= finalSize.Height) y = r.Bottom + gap;          // darunter
            else if (r.Top - gap - cs.Height >= 0) y = r.Top - gap - cs.Height;              // darüber
            else y = Math.Max(8, (finalSize.Height - cs.Height) / 2);                        // daneben
            x = r.Left;
            if (y + cs.Height > r.Top && y < r.Bottom) // überlappt → seitlich
                x = r.Right + gap + cs.Width <= finalSize.Width ? r.Right + gap : Math.Max(8, r.Left - gap - cs.Width);
        }
        else
        {
            x = (finalSize.Width - cs.Width) / 2;
            y = (finalSize.Height - cs.Height) / 2;
        }
        x = Math.Clamp(x, 8, Math.Max(8, finalSize.Width - cs.Width - 8));
        y = Math.Clamp(y, 8, Math.Max(8, finalSize.Height - cs.Height - 8));
        Canvas.SetLeft(_card, x);
        Canvas.SetTop(_card, y);
        _canvas.Arrange(full);
        return finalSize;
    }
}
