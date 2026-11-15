using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LobixApp.Models;
using LobixApp.Services;

namespace LobixApp.Views;

/// <summary>Embedded saved floor plan used to select physical systems from the manual booking workspace.</summary>
public sealed class FloorPlanBookingMapControl : UserControl
{
    private readonly Dictionary<Button, (int VariationId, string Number)> _systemButtons = new();
    private readonly GamenetInfo _gamenet;
    private readonly LobixExtendedApiService _api;
    private readonly LobixApiService _bookingApi;
    private readonly Action<int, string> _onSelect;
    private readonly Func<int, string, bool> _isBooked;
    private readonly Func<int, string, bool> _isSelected;
    private readonly Canvas _canvas = new()
    {
        Width = 1600,
        Height = 1000,
        MinWidth = 1600,
        MinHeight = 1000,
        Background = new SolidColorBrush(Color.FromRgb(22, 24, 32)),
        ClipToBounds = false
    };
    private ScrollViewer? _mapScrollViewer;
    private double _mapZoom = 1.0;
    private readonly TextBlock _zoomPercent = new() { Text = "۱۰۰٪", MinWidth = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(0, 0, 9, 0) };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), Foreground = Brushes.Gainsboro };
    private readonly Dictionary<string, (string Number, int VariationId, string Category, bool Active)> _systems = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedNumbers = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private Task? _loadTask;

    public FloorPlanBookingMapControl(
     GamenetInfo gamenet,
     string token,
     Action<int, string> onSelect,
     Func<int, string, bool> isSelected,
     Func<int, string, bool> isBooked)
    {
        _gamenet = gamenet;
        _onSelect = onSelect;
        _isSelected = isSelected;
        _isBooked = isBooked;

        _api = new LobixExtendedApiService(token);
        _bookingApi = new LobixApiService();
        _bookingApi.SetAuthentication(token);

        FlowDirection = FlowDirection.RightToLeft;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Color.FromRgb(13, 14, 19));
        Foreground = Brushes.White;

        Content = BuildContent();
        Loaded += async (_, _) => await EnsureLoadedAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(top, Dock.Top);
        var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "نقشه سالن", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var zoomOut = new Button { Content = "−", Width = 30, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 9, 0), Background = new SolidColorBrush(Color.FromRgb(43, 45, 56)), Foreground = Brushes.White, FontSize = 16 };
        _zoomPercent.Text = "۱۰۰٪";
        var zoomIn = new Button { Content = "+", Width = 30, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 9, 0), Background = new SolidColorBrush(Color.FromRgb(43, 45, 56)), Foreground = Brushes.White, FontSize = 16 };
        zoomOut.Click += (_, _) => ChangeZoom(1.0 / 1.15);
        zoomIn.Click += (_, _) => ChangeZoom(1.15);
        actions.Children.Add(zoomOut);
        actions.Children.Add(_zoomPercent);
        actions.Children.Add(zoomIn);

        var refresh = new Button { Content = "بازخوانی", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(3, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(43, 45, 56)), Foreground = Brushes.White, FontSize = 11 };
        refresh.Click += async (_, _) => { _loadTask = LoadMapAsync(); await _loadTask; };
        actions.Children.Add(refresh);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        top.Children.Add(header);
        _status.FontSize = 11;
        _status.TextWrapping = TextWrapping.NoWrap;
        top.Children.Add(_status);
        root.Children.Add(top);
        var viewport = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(62, 64, 76)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Background = new SolidColorBrush(Color.FromRgb(20, 16, 20)), ClipToBounds = true };
        _mapScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CanContentScroll = false,
            PanningMode = PanningMode.Both,
            Background = new SolidColorBrush(Color.FromRgb(20, 16, 20)),
            BorderThickness = new Thickness(0),
            Focusable = false,
            SnapsToDevicePixels = true,
            Content = _canvas
        };

        _mapScrollViewer.PreviewMouseWheel += MapScrollViewer_PreviewMouseWheel;

        viewport.Child = _mapScrollViewer;
        root.Children.Add(viewport);
        DrawGrid();
        return root;
    }

    private void DrawGrid()
    {
        for (int x = 0; x <= 1600; x += 40) _canvas.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = 0, Y2 = 1000, Stroke = new SolidColorBrush(Color.FromRgb(33, 36, 46)), StrokeThickness = 1, IsHitTestVisible = false });
        for (int y = 0; y <= 1000; y += 40) _canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = 1600, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(33, 36, 46)), StrokeThickness = 1, IsHitTestVisible = false });
    }

    public Task EnsureLoadedAsync()
    {
        if (_loadTask == null) _loadTask = LoadMapAsync();
        return _loadTask;
    }
    public void RefreshBookedStates()
    {
        foreach (var entry in _systemButtons.ToList())
        {
            Button button = entry.Key;
            int variationId = entry.Value.VariationId;
            string systemNumber = entry.Value.Number;

            bool booked = _isBooked(variationId, systemNumber);
            bool selected = _isSelected(variationId, systemNumber);

            button.Background = new SolidColorBrush(
                booked
                    ? Color.FromRgb(107, 37, 43)
                    : selected
                        ? Color.FromRgb(76, 35, 44)
                        : Color.FromRgb(39, 31, 36));

            button.BorderBrush = new SolidColorBrush(
                booked
                    ? Color.FromRgb(228, 91, 100)
                    : selected
                        ? Color.FromRgb(255, 137, 146)
                        : Color.FromRgb(116, 66, 75));

            button.ToolTip = booked
                ? "این سیستم در این بازه زمانی رزرو شده است."
                : "برای انتخاب یا لغو انتخاب این سیستم کلیک کنید";
        }
    }
    private async Task LoadMapAsync()
    {
        _status.Text = "در حال دریافت نقشه و فهرست سیستم‌ها…";
        foreach (var child in _canvas.Children.OfType<FrameworkElement>().Where(x => x.Tag as string == "map-object").ToList()) _canvas.Children.Remove(child);
        _systems.Clear();
        _systemButtons.Clear();
        try
        {
            // Fetch the authoritative physical-device registry and index it only by
            // stable SystemId and the unambiguous VariationId + Number pair.
            var byVariationNumber = new Dictionary<(int VariationId, string Number), string>();
            var ambiguousPairs = new HashSet<(int VariationId, string Number)>();
            try
            {
                using var details = await _bookingApi.GetGamenetDetailsAsync(_gamenet.Id);
                var root = Unwrap(details.RootElement);
                var gamenet = TryObject(root, "gamenet", "product", "venue");
                if (gamenet.ValueKind != JsonValueKind.Object) gamenet = root;
                var physical = TryArray(gamenet, "physical_systems", "systems");
                if (physical.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in physical.EnumerateArray())
                    {
                        string id = Read(e, "system_id", "id", "physical_system_id", "systemId").Trim();
                        string number = NormalizeDigits(Read(e, "number", "system_number", "pc_number", "systemNumber"));
                        int variationId = ReadInt(e, "variation_id", "variationId");
                        string category = Read(e, "category", "type");
                        string activeText = Read(e, "active");
                        bool active = !activeText.Equals("false", StringComparison.OrdinalIgnoreCase) && activeText != "0";
                        if (id.Length == 0 || number.Length == 0 || variationId < 0) continue;
                        _systems[id] = (number, variationId, category, active);
                        var key = (variationId, number);
                        if (byVariationNumber.ContainsKey(key)) ambiguousPairs.Add(key);
                        else byVariationNumber[key] = id;
                    }
                }
            }
            catch { /* The map remains viewable offline, but no unverified tile is selectable. */ }
            // Resolve physical systems whose API variation_id is zero
            // by matching their number against the variation system_numbers.
            try
            {
                using var details = await _bookingApi.GetGamenetDetailsAsync(_gamenet.Id);
                var root = Unwrap(details.RootElement);
                var gamenet = TryObject(root, "gamenet", "product", "venue");
                if (gamenet.ValueKind != JsonValueKind.Object)
                    gamenet = root;

                var variations = TryArray(gamenet, "variations");

                if (variations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var variation in variations.EnumerateArray())
                    {
                        int variationId = ReadInt(variation, "variation_id", "id", "variationId");
                        if (variationId <= 0) continue;

                        var systemNumbers = TryArray(variation, "system_numbers");

                        if (systemNumbers.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var systemNumberElement in systemNumbers.EnumerateArray())
                        {
                            string systemNumberText =
                                systemNumberElement.ValueKind == JsonValueKind.String
                                    ? systemNumberElement.GetString() ?? string.Empty
                                    : systemNumberElement.ToString();

                            string normalized = NormalizeDigits(systemNumberText.Trim());

                            string digits = new string(normalized.Reverse()
                                .TakeWhile(char.IsDigit)
                                .Reverse()
                                .ToArray());

                            if (digits.Length == 0) continue;

                            // Only resolve physical systems that have no variation assigned.
                            foreach (var entry in _systems.ToList())
                            {
                                if (entry.Value.VariationId != 0 ||
                                    entry.Value.Number != digits)
                                    continue;

                                var resolved = (
                                    entry.Value.Number,
                                    variationId,
                                    entry.Value.Category,
                                    entry.Value.Active
                                );

                                _systems[entry.Key] = resolved;

                                var key = (variationId, digits);

                                if (byVariationNumber.ContainsKey(key) &&
                                    byVariationNumber[key] != entry.Key)
                                {
                                    ambiguousPairs.Add(key);
                                }
                                else
                                {
                                    byVariationNumber[key] = entry.Key;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Keep the original physical-system data if variation resolution fails.
            }
            JsonDocument? layoutDoc = null;
            string localPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LobixApp", $"floorplan-layout-{_gamenet.Id}.json");
            try { layoutDoc = await _api.GetLayoutAsync(_gamenet.Id); }
            catch
            {
                if (System.IO.File.Exists(localPath)) layoutDoc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(localPath));
                else throw;
            }
            // Prefer the last locally saved layout when the API responds successfully
            // but has no objects (common while a pending layout sync is waiting).
            if (layoutDoc != null && System.IO.File.Exists(localPath))
            {
                var remoteRoot = Unwrap(layoutDoc.RootElement);
                var remotePayload = TryObject(remoteRoot, "layout", "floor_plan", "floorplan", "data", "result", "payload");
                if (remotePayload.ValueKind != JsonValueKind.Object) remotePayload = remoteRoot;
                var remoteObjects = remoteRoot.ValueKind == JsonValueKind.Array ? remoteRoot : TryArray(remotePayload, "objects", "items", "elements");
                if (remoteObjects.ValueKind != JsonValueKind.Array || remoteObjects.GetArrayLength() == 0)
                {
                    layoutDoc.Dispose();
                    layoutDoc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(localPath));
                }
            }
            using (layoutDoc)
            {
                var lr = Unwrap(layoutDoc.RootElement);
                JsonElement objects = lr.ValueKind == JsonValueKind.Array ? lr : default;
                var payload = TryObject(lr, "layout", "floor_plan", "floorplan", "data", "result", "payload");
                if (payload.ValueKind == JsonValueKind.Object)
                {
                    var nested = TryObject(payload, "layout", "floor_plan", "floorplan");
                    if (nested.ValueKind == JsonValueKind.Object) payload = nested;
                    objects = TryArray(payload, "objects", "items", "elements");
                }
                if (objects.ValueKind != JsonValueKind.Array) objects = TryArray(lr, "objects", "items", "elements");
                // Local FloorPlanView snapshots serialize PlanItem[] directly.
                if (objects.ValueKind != JsonValueKind.Array && lr.ValueKind == JsonValueKind.Array) objects = lr;
                if (objects.ValueKind != JsonValueKind.Array)
                {
                    _status.Text = "نقشه ذخیره‌شده پیدا نشد. ابتدا در طراحی سالن نقشه را ذخیره و سپس بازخوانی کنید.";
                    return;
                }
                UpdateCanvasSize(objects);
                int count = 0;
                foreach (var item in objects.EnumerateArray())
                {
                    string type = Read(item, "type", "Type");
                    string label = Read(item, "label", "name", "Name");
                    string systemId = Read(item, "system_id", "SystemId", "systemId").Trim();
                    string numberCandidate = NormalizeDigits(Read(item, "system_number", "SystemNumber", "number"));
                    int layoutVariationId = ReadInt(item, "variation_id", "VariationId", "variationId");
                    string layoutCategory = Read(item, "category", "Category", "type_name");
                    bool declaredSystem = type.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                        ReadBool(item, "is_physical_system", "IsPhysicalSystem") ||
                        type.Equals("کامپیوتر", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("کنسول", StringComparison.OrdinalIgnoreCase) || systemId.Length > 0;
                    bool isSystem = declaredSystem;
                    (string Number, int VariationId, string Category, bool Active) resolved = default;
                    bool hasPhysical = false;
                    if (isSystem && systemId.Length > 0 && _systems.TryGetValue(systemId, out resolved))
                    {
                        hasPhysical = true;
                    }
                    else if (isSystem && layoutVariationId > 0 && numberCandidate.Length > 0 &&
                             !ambiguousPairs.Contains((layoutVariationId, numberCandidate)) &&
                             byVariationNumber.TryGetValue((layoutVariationId, numberCandidate), out string? matchedId) &&
                             _systems.TryGetValue(matchedId, out resolved))
                    {
                        hasPhysical = true;
                        systemId = matchedId;
                    }
                    // Do not infer a device from digits in a room name (e.g. VIP 2).
                    // Unmatched/ambiguous objects are rendered as ordinary, non-clickable map items.
                    if (!hasPhysical) isSystem = false;
                    double x = ReadDouble(item, 0, "x", "X"), y = ReadDouble(item, 0, "y", "Y");
                    double w = Math.Clamp(ReadDouble(item, 100, "width", "w", "W"), 24, 1600);
                    double h = Math.Clamp(ReadDouble(item, 60, "height", "h", "H"), 12, 1000);
                    double rotation = ReadDouble(item, 0, "rotation", "Rotation");
                    Brush fill = type.ToLowerInvariant() switch { "room" => new SolidColorBrush(Color.FromArgb(75, 219, 60, 69)), "wall" => new SolidColorBrush(Color.FromRgb(125, 129, 140)), "desk" => new SolidColorBrush(Color.FromRgb(55, 59, 70)), "entrance" => new SolidColorBrush(Color.FromRgb(31, 82, 68)), "section" or "area" => new SolidColorBrush(Color.FromArgb(90, 76, 91, 113)), _ => new SolidColorBrush(Color.FromRgb(45, 48, 60)) };
                    Brush stroke = isSystem ? new SolidColorBrush(Color.FromRgb(116, 66, 75)) : Brushes.Gray;
                    if (isSystem && hasPhysical)
                    {
                        string resolvedNumber = resolved.Number;
                        int resolvedVariationId = resolved.VariationId;
                        string resolvedCategory = string.IsNullOrWhiteSpace(layoutCategory) ? resolved.Category : layoutCategory;
                        System.Diagnostics.Debug.WriteLine(
    $"[MAP TILE] VariationId={resolvedVariationId} | Number=[{resolvedNumber}] | Label=[{label}]"
);
                        bool booked = _isBooked(resolvedVariationId, resolvedNumber);
                        bool selected = _isSelected(resolvedVariationId, resolvedNumber);
                        string displayTitle = string.IsNullOrWhiteSpace(resolvedCategory)
                            ? (string.IsNullOrWhiteSpace(label) ? "سیستم" : label)
                            : resolvedCategory;
                        string displayNumber = resolvedNumber.Trim().TrimStart('#', '＃');
                        foreach (string separator in new[] { "#", "＃", " -" , "-" })
                        {
                            string suffix = separator.Trim() + displayNumber;
                            if (displayNumber.Length > 0 &&
                                displayTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                displayTitle = displayTitle.Substring(0, displayTitle.Length - suffix.Length).Trim();
                                break;
                            }
                        }
                        double titleFontSize = Math.Clamp(Math.Min(w / 7.0, h * 0.25), 8.0, 13.0);
                        double numberFontSize = Math.Clamp(Math.Min(w / 5.8, h * 0.31), 9.0, 15.0);
                        var tileText = new StackPanel
                        {
                            Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Margin = new Thickness(2)
                        };
                        tileText.Children.Add(new TextBlock
                        {
                            Text = displayTitle, Foreground = Brushes.White, FontSize = titleFontSize,
                            FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Stretch, TextWrapping = TextWrapping.NoWrap,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        tileText.Children.Add(new TextBlock
                        {
                            Text = displayNumber, Foreground = new SolidColorBrush(Color.FromRgb(255, 218, 220)),
                            FontSize = numberFontSize, FontWeight = FontWeights.Bold,
                            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis
                        });
                        if (!resolved.Active)
                        {
                            var inactive = new Border
                            {
                                Width = w, Height = h, Tag = "map-object",
                                Background = new SolidColorBrush(Color.FromRgb(43, 35, 39)),
                                BorderBrush = new SolidColorBrush(Color.FromRgb(91, 43, 53)),
                                BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(7),
                                Padding = new Thickness(2), ClipToBounds = true, Child = tileText
                            };
                            foreach (var textBlock in tileText.Children.OfType<TextBlock>())
                                textBlock.Foreground = Brushes.LightGray;
                            Canvas.SetLeft(inactive, x); Canvas.SetTop(inactive, y); _canvas.Children.Add(inactive); count++; continue;
                        }
                        var button = new Button
                        {
                            Width = w,
                            Height = h,
                            Content = tileText,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            ClipToBounds = true,
                            Tag = "map-object",

                            Background = new SolidColorBrush(
        booked
            ? Color.FromRgb(107, 37, 43)
            : selected
                ? Color.FromRgb(76, 35, 44)
                : Color.FromRgb(39, 31, 36)),

                            Foreground = Brushes.White,

                            BorderBrush = new SolidColorBrush(
        booked
            ? Color.FromRgb(228, 91, 100)
            : selected
                ? Color.FromRgb(255, 137, 146)
                : Color.FromRgb(116, 66, 75)),

                            BorderThickness = new Thickness(1.5),
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 12,
                            Padding = new Thickness(2),

                            ToolTip = $"{displayTitle} {displayNumber} · " + (booked
        ? "این سیستم در این بازه زمانی رزرو شده است."
        : "برای انتخاب یا لغو انتخاب این سیستم کلیک کنید")
                        };

                        int capturedVariation = resolvedVariationId;
                        string capturedNumber = resolvedNumber;
                        _systemButtons[button] = (resolvedVariationId, resolvedNumber);
                        button.Click += (_, _) =>
                        {
                            if (_isBooked(capturedVariation, capturedNumber))
                            {
                                _status.Text = $"{displayLabel} در این بازه زمانی رزرو شده است.";
                                return;
                            }

                            _onSelect(capturedVariation, capturedNumber);

                            bool nowSelected = _isSelected(capturedVariation, capturedNumber);

                            button.Background = new SolidColorBrush(
                                nowSelected
                                    ? Color.FromRgb(76, 35, 44)
                                    : Color.FromRgb(39, 31, 36));

                            button.BorderBrush = new SolidColorBrush(
                                nowSelected
                                    ? Color.FromRgb(255, 137, 146)
                                    : Color.FromRgb(116, 66, 75));

                            _status.Text = nowSelected
                                ? $"{displayLabel} انتخاب شد."
                                : $"{displayLabel} از انتخاب خارج شد.";
                        };

                        button.RenderTransformOrigin = new Point(.5, .5);
                        button.RenderTransform = new RotateTransform(rotation);

                        Canvas.SetLeft(button, x);
                        Canvas.SetTop(button, y);
                        Panel.SetZIndex(button, 100);

                        _canvas.Children.Add(button);
                        count++;

                        continue;
                    }
                    var border = new Border { Width = w, Height = h, Tag = "map-object", Background = fill, BorderBrush = stroke, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(type.Equals("wall", StringComparison.OrdinalIgnoreCase) ? 3 : 9), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(rotation), Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) } };
                    Canvas.SetLeft(border, x); Canvas.SetTop(border, y); Panel.SetZIndex(border, 0); _canvas.Children.Add(border);
                }
                _status.Text = count > 0 ? $"نقشه بارگذاری شد؛ {count} سیستم روی نقشه قابل انتخاب است." : "نقشه نمایش داده شد، اما سیستم متصل به نوع محصول پیدا نشد. شناسه نوع سیستم‌ها را در طراحی سالن بررسی کنید.";
            }
        }
        catch (Exception ex) { _status.Text = "بارگذاری نقشه ناموفق بود: " + ex.Message; }
    }
    private void ChangeZoom(double factor)
    {
        if (_mapScrollViewer == null)
            return;

        _mapZoom = Math.Clamp(_mapZoom * factor, 0.25, 3.0);
        _canvas.LayoutTransform = new ScaleTransform(_mapZoom, _mapZoom);
        _zoomPercent.Text = ToPersianDigits(((int)Math.Round(_mapZoom * 100)).ToString(CultureInfo.InvariantCulture)) + "٪";
        _mapScrollViewer.UpdateLayout();
    }

    private static string ToPersianDigits(string value) =>
        value.Replace("0", "۰").Replace("1", "۱").Replace("2", "۲")
             .Replace("3", "۳").Replace("4", "۴").Replace("5", "۵")
             .Replace("6", "۶").Replace("7", "۷").Replace("8", "۸").Replace("9", "۹");

    private void MapScrollViewer_PreviewMouseWheel(
    object sender,
    MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) &&
            !Keyboard.IsKeyDown(Key.RightCtrl))
        {
            return;
        }

        if (_mapScrollViewer == null)
            return;

        e.Handled = true;

        double oldZoom = _mapZoom;

        double zoomFactor = e.Delta > 0
            ? 1.1
            : 1.0 / 1.1;

        double newZoom = Math.Clamp(
            oldZoom * zoomFactor,
            0.25,
            3.0);

        if (Math.Abs(newZoom - oldZoom) < 0.001)
            return;

        Point mousePosition = e.GetPosition(_mapScrollViewer);

        double contentX =
            (_mapScrollViewer.HorizontalOffset + mousePosition.X) / oldZoom;

        double contentY =
            (_mapScrollViewer.VerticalOffset + mousePosition.Y) / oldZoom;

        _mapZoom = newZoom;

        _canvas.LayoutTransform = new ScaleTransform(
            _mapZoom,
            _mapZoom);
        _zoomPercent.Text = ToPersianDigits(((int)Math.Round(_mapZoom * 100)).ToString(CultureInfo.InvariantCulture)) + "٪";

        _mapScrollViewer.UpdateLayout();

        _mapScrollViewer.ScrollToHorizontalOffset(
            contentX * newZoom - mousePosition.X);

        _mapScrollViewer.ScrollToVerticalOffset(
            contentY * newZoom - mousePosition.Y);
    }
    private void UpdateCanvasSize(JsonElement objects)
    {
        double requiredWidth = 1600;
        double requiredHeight = 1000;

        if (objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in objects.EnumerateArray())
            {
                double x = ReadDouble(item, 0, "x", "X");
                double y = ReadDouble(item, 0, "y", "Y");

                double w = Math.Clamp(
                    ReadDouble(item, 100, "width", "w", "W"),
                    24,
                    1600);

                double h = Math.Clamp(
                    ReadDouble(item, 60, "height", "h", "H"),
                    12,
                    1000);

                double rotation = ReadDouble(item, 0, "rotation", "Rotation");
                double radians = rotation * Math.PI / 180.0;

                double rotatedWidth =
                    Math.Abs(w * Math.Cos(radians)) +
                    Math.Abs(h * Math.Sin(radians));

                double rotatedHeight =
                    Math.Abs(w * Math.Sin(radians)) +
                    Math.Abs(h * Math.Cos(radians));

                requiredWidth = Math.Max(requiredWidth, x + rotatedWidth + 160);
                requiredHeight = Math.Max(requiredHeight, y + rotatedHeight + 160);
            }
        }

        _canvas.Width = Math.Ceiling(requiredWidth / 100) * 100;
        _canvas.Height = Math.Ceiling(requiredHeight / 100) * 100;

        _canvas.Children.Clear();
        DrawGrid();
    }
    private static string NormalizeDigits(string value)
    {
        string normalized = new(value.Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + c - '۰'), >= '٠' and <= '٩' => (char)('0' + c - '٠'), _ => c }).ToArray());
        return normalized.Length > 0 && normalized.All(char.IsDigit) ? normalized.TrimStart('0') is { Length: > 0 } n ? n : "0" : normalized;
    }
    private static bool ReadBool(JsonElement e, params string[] names) => Read(e, names).Equals("true", StringComparison.OrdinalIgnoreCase) || Read(e, names) == "1";

    private static JsonElement Unwrap(JsonElement e)
    {
        for (int i = 0; i < 4 && e.ValueKind == JsonValueKind.Object; i++)
        {
            var next = TryObject(e, "data", "result", "payload");
            if (next.ValueKind != JsonValueKind.Object) break;
            e = next;
        }
        return e;
    }
    private static JsonElement FindProperty(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return default;
        foreach (var p in e.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return default;
    }
    private static JsonElement TryObject(JsonElement e, params string[] names)
    {
        foreach (var name in names) { var v = FindProperty(e, name); if (v.ValueKind == JsonValueKind.Object) return v; }
        return default;
    }
    private static JsonElement TryArray(JsonElement e, params string[] names)
    {
        foreach (var name in names) { var v = FindProperty(e, name); if (v.ValueKind == JsonValueKind.Array) return v; }
        return default;
    }
    private static string Read(JsonElement e, params string[] names)
    {
        foreach (var name in names) { var v = FindProperty(e, name); if (v.ValueKind != JsonValueKind.Undefined && v.ValueKind != JsonValueKind.Null) return v.ToString().Trim(); }
        return string.Empty;
    }
    private static int ReadInt(JsonElement e, params string[] names) => int.TryParse(NormalizeDigits(Read(e, names)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static double ReadDouble(JsonElement e, double fallback, params string[] names) => double.TryParse(Read(e, names), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

}
