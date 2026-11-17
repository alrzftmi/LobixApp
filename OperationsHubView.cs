
using LobixApp.Models;
using LobixApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LobixApp.Views;

public sealed class OperationsHubView : UserControl
{
    // =========================================================
    // THEME
    // =========================================================

    private static readonly Color Bg = Color.FromRgb(13, 14, 19);
    private static readonly Color Surface = Color.FromRgb(21, 23, 32);
    private static readonly Color Surface2 = Color.FromRgb(27, 30, 41);
    private static readonly Color Surface3 = Color.FromRgb(34, 37, 50);
    private static readonly Color LineColor = Color.FromRgb(43, 46, 61);
    private static readonly Color Text = Color.FromRgb(244, 245, 250);
    private static readonly Color Muted = Color.FromRgb(151, 157, 175);
    private static readonly Color Accent = Color.FromRgb(219, 60, 69);
    private static readonly Color Green = Color.FromRgb(67, 194, 139);
    private static readonly Color Amber = Color.FromRgb(232, 177, 83);
    private static readonly Color Blue = Color.FromRgb(91, 155, 235);

    private static readonly FontFamily Peyda =
        new("pack://application:,,,/Fonts/#Peyda");

    // =========================================================
    // SERVICES AND DATA
    // =========================================================

    private readonly GamenetInfo _gamenet;
    private readonly LobixExtendedApiService _api;
    private readonly LobixApiService _bookingApi;

    private readonly ObservableCollection<CustomerRecord> _customers = new();
    private List<CustomerRecord> _customerRecords = new();

    private readonly Dictionary<string, string> _customerNotes =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _busy;
    private bool _disposed;

    // =========================================================
    // CUSTOMER CONTROLS
    // =========================================================

    private readonly ComboBox _customerFilter = new()
    {
        Width = 155,
        Height = 42,
        FontFamily = Peyda,
        FontSize = 12,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private readonly TextBox _customerQuery = new()
    {
        MinWidth = 200,
        Height = 42
    };

    private readonly TextBlock _customerCount = new()
    {
        Text = "۰ مشتری",
        Foreground = Brush(Muted),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly DataGrid _customerGrid = new();

    private readonly TextBlock _detailName = new()
    {
        Text = "یک مشتری را انتخاب کنید",
        FontSize = 20,
        FontWeight = FontWeights.Bold,
        Foreground = Brush(Text)
    };

    private readonly TextBlock _detailPhone = new()
    {
        Text = "اطلاعات پروفایل مشتری در این قسمت نمایش داده می‌شود",
        Foreground = Brush(Muted),
        FontSize = 12,
        Margin = new Thickness(0, 6, 0, 18)
    };

    private readonly TextBlock _detailId = NewValueText();
    private readonly TextBlock _detailBookingId = NewValueText();
    private readonly TextBlock _detailLast = NewValueText();
    private readonly TextBlock _detailAmount = NewValueText();
    private readonly TextBlock _detailPeriod = NewValueText();

    private readonly TextBlock _detailHistory = new()
    {
        Foreground = Brush(Muted),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 23
    };

    private readonly TextBox _customerNote = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 105,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    // =========================================================
    // REPORT CONTROLS
    // =========================================================

    private readonly TextBox _from = new()
    {
        Text = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Width = 132,
        Height = 42
    };

    private readonly TextBox _to = new()
    {
        Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Width = 132,
        Height = 42
    };

    private readonly List<TextBlock> _kpiValues = new();

    private readonly StackPanel _reservationChart = new();
    private readonly StackPanel _amountChart = new();
    private readonly StackPanel _peakHoursChart = new();
    private readonly StackPanel _systemCustomerChart = new();

    private readonly TextBlock _reportNote = new()
    {
        Foreground = Brush(Muted),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap
    };

    // =========================================================
    // MAINTENANCE AND STATUS
    // =========================================================

    private readonly TextBlock _status = new()
    {
        Foreground = Brush(Muted),
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private readonly TextBox _maintenanceText = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 180,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private readonly ListBox _auditList = new()
    {
        MinHeight = 150
    };

    private readonly Grid _loadingLayer = new();

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public OperationsHubView(GamenetInfo gamenet, OperatorSession session)
    {
        _gamenet = gamenet;
        _api = new LobixExtendedApiService(session.Token);

        _bookingApi = new LobixApiService();
        _bookingApi.SetAuthentication(session.Token);

        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush(Bg);
        Foreground = Brush(Text);
        FontFamily = Peyda;

        BuildUi();

        Loaded += async (_, _) =>
        {
            LoadLocalData();
            await SearchCustomersAsync();
            await LoadReportAsync();
        };

        Unloaded += (_, _) =>
        {
            if (_disposed) return;

            _disposed = true;
            _api.Dispose();
            _bookingApi.Dispose();
        };
    }

    // =========================================================
    // MAIN LAYOUT
    // =========================================================

    private void BuildUi()
    {
        var root = new Grid
        {
            Margin = new Thickness(24, 20, 24, 16),
            Background = Brush(Bg)
        };

        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        // HEADER
        var header = new Grid
        {
            Margin = new Thickness(0, 0, 0, 20)
        };

        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        header.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        var titleStack = new StackPanel();

        titleStack.Children.Add(Txt(
            "مرکز عملیات",
            26,
            Text,
            FontWeights.Bold));

        titleStack.Children.Add(Txt(
            "مدیریت مشتریان، گزارش عملکرد و کنترل فعالیت گیمنت",
            12,
            Muted,
            margin: new Thickness(0, 5, 0, 0)));

        header.Children.Add(titleStack);

        var venue = new Border
        {
            Background = Brush(Color.FromRgb(38, 31, 39)),
            BorderBrush = Brush(Color.FromRgb(83, 48, 57)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(15, 10, 15, 10),
            VerticalAlignment = VerticalAlignment.Center
        };

        venue.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Brush(Green),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                },
                Txt(_gamenet.Name, 12, Text, FontWeights.SemiBold)
            }
        };

        Grid.SetColumn(venue, 1);
        header.Children.Add(venue);

        root.Children.Add(header);

        // TABS
        var tabs = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(Text),
            Margin = new Thickness(0, 4, 0, 12),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        tabs.ItemContainerStyle = BuildTabStyle();

        tabs.Items.Add(new TabItem
        {
            Header = "مشتریان و CRM",
            Content = BuildCustomersPage()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "گزارش و تحلیل",
            Content = BuildReportsPage()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "یادداشت‌های شیفت",
            Content = BuildMaintenancePage()
        });

        tabs.Items.Add(new TabItem
        {
            Header = "سلامت سیستم",
            Content = BuildReliabilityPage()
        });

        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        // BOTTOM STATUS
        var statusBar = new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(15, 10, 15, 10),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var statusGrid = new Grid();

        statusGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        statusGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        var statusDot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Brush(Green),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };

        statusGrid.Children.Add(statusDot);

        Grid.SetColumn(_status, 1);
        statusGrid.Children.Add(_status);

        statusBar.Child = statusGrid;

        Grid.SetRow(statusBar, 2);
        root.Children.Add(statusBar);

        // LOADING OVERLAY
        _loadingLayer.Background =
            new SolidColorBrush(Color.FromArgb(220, 9, 10, 15));

        _loadingLayer.Visibility = Visibility.Collapsed;

        var loadingCard = new Border
        {
            Width = 310,
            Padding = new Thickness(28),
            Background = Brush(Surface2),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var loadingStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };

        loadingStack.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Width = 190,
            Height = 5,
            Foreground = Brush(Accent),
            Background = Brush(LineColor),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 18)
        });

        loadingStack.Children.Add(Txt(
            "در حال دریافت اطلاعات...",
            14,
            Text,
            FontWeights.SemiBold));

        loadingCard.Child = loadingStack;
        _loadingLayer.Children.Add(loadingCard);

        Grid.SetRowSpan(_loadingLayer, 3);
        root.Children.Add(_loadingLayer);

        Content = root;
    }

    // =========================================================
    // CUSTOMERS PAGE
    // =========================================================


    private UIElement BuildCustomersPage()
    {
        var page = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0)
        };

        page.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        page.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        // TOOLBAR
        var toolbar = new Grid
        {
            Margin = new Thickness(0, 0, 0, 15)
        };

        toolbar.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        toolbar.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        var searchWrap = new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(13, 0, 13, 0),
            Height = 46
        };

        _customerQuery.Background = Brushes.Transparent;
        _customerQuery.BorderThickness = new Thickness(0);
        _customerQuery.Foreground = Brush(Text);
        _customerQuery.CaretBrush = Brush(Accent);
        _customerQuery.FontFamily = Peyda;
        _customerQuery.FontSize = 13;
        _customerQuery.VerticalContentAlignment = VerticalAlignment.Center;
        _customerQuery.ToolTip = "نام، شماره موبایل یا شناسه مشتری";

        _customerQuery.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                await SearchCustomersAsync();
        };

        searchWrap.Child = _customerQuery;
        toolbar.Children.Add(searchWrap);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        actions.Children.Add(_customerCount);

        _customerFilter.Items.Clear();
        _customerFilter.Items.Add("همه مشتریان");
        _customerFilter.Items.Add("مشتریان پرتکرار");
        _customerFilter.Items.Add("دارای شماره تماس");
        _customerFilter.Items.Add("بدون شماره تماس");

        _customerFilter.SelectedIndex = 0;

        StyleComboBox(_customerFilter);

        _customerFilter.SelectionChanged += (_, _) =>
            ApplyCustomerFilter();

        actions.Children.Add(_customerFilter);

        actions.Children.Add(MakeButton(
            "جست‌وجو",
            async (_, _) => await SearchCustomersAsync(),
            true,
            105));

        actions.Children.Add(MakeButton(
            "پاک‌کردن",
            (_, _) =>
            {
                _customerQuery.Clear();
                _ = SearchCustomersAsync();
            },
            false,
            85));

        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(actions);

        page.Children.Add(toolbar);

        // BODY
        var body = new Grid();

        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1.65, GridUnitType.Star)
        });

        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        // CUSTOMER TABLE CARD
        var tableCard = Card();

        var tableLayout = new Grid();

        tableLayout.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        tableLayout.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        var tableHeader = new StackPanel
        {
            Margin = new Thickness(2, 0, 2, 13)
        };

        tableHeader.Children.Add(Txt(
            "فهرست مشتریان",
            16,
            Text,
            FontWeights.Bold));

        tableHeader.Children.Add(Txt(
            "اطلاعات مشتریان و فعالیت آن‌ها در بازه اخیر",
            11,
            Muted,
            margin: new Thickness(0, 4, 0, 0)));

        tableLayout.Children.Add(tableHeader);

        ConfigureCustomerGrid();

        Grid.SetRow(_customerGrid, 1);
        tableLayout.Children.Add(_customerGrid);

        tableCard.Child = tableLayout;
        body.Children.Add(tableCard);

        // CUSTOMER PROFILE CARD
        var detail = Card();

        detail.Margin = new Thickness(12, 0, 0, 0);

        var detailScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CanContentScroll = true
        };

        // DARK SCROLLBAR STYLE
        var scrollStyle = new Style(typeof(ScrollBar));

        scrollStyle.Setters.Add(new Setter(
            FrameworkElement.WidthProperty, 10d));

        scrollStyle.Setters.Add(new Setter(
            Control.BackgroundProperty, Brushes.Transparent));

        scrollStyle.Setters.Add(new Setter(
            Control.BorderThicknessProperty, new Thickness(0)));

        var scrollTemplate = new ControlTemplate(typeof(ScrollBar));

        var track = new FrameworkElementFactory(typeof(Track));
        track.Name = "PART_Track";

        track.SetValue(
            Track.OrientationProperty,
            Orientation.Vertical);

        track.SetValue(
            Track.IsDirectionReversedProperty,
            true);

        track.SetValue(
            Track.MinimumProperty,
            new TemplateBindingExtension(RangeBase.MinimumProperty));

        track.SetValue(
            Track.MaximumProperty,
            new TemplateBindingExtension(RangeBase.MaximumProperty));

        track.SetValue(
            Track.ValueProperty,
            new TemplateBindingExtension(RangeBase.ValueProperty));

        track.SetValue(
            Track.ViewportSizeProperty,
            new TemplateBindingExtension(ScrollBar.ViewportSizeProperty));

        var thumb = new FrameworkElementFactory(typeof(Thumb));

        thumb.SetValue(
            FrameworkElement.MinHeightProperty,
            35d);

        var thumbTemplate = new ControlTemplate(typeof(Thumb));

        var thumbBorder = new FrameworkElementFactory(typeof(Border));

        thumbBorder.SetValue(
            Border.BackgroundProperty,
            Brush(Color.FromRgb(75, 79, 96)));

        thumbBorder.SetValue(
            Border.CornerRadiusProperty,
            new CornerRadius(5));

        thumbBorder.SetValue(
            Border.MarginProperty,
            new Thickness(2, 2, 2, 2));

        thumbTemplate.VisualTree = thumbBorder;

        thumb.SetValue(
            Control.TemplateProperty,
            thumbTemplate);

        track.SetValue(
            Track.ThumbProperty,
            thumb);

        scrollTemplate.VisualTree = track;

        scrollStyle.Setters.Add(new Setter(
            Control.TemplateProperty,
            scrollTemplate));

        // HOVER EFFECT
        var hover = new Trigger
        {
            Property = ScrollBar.IsMouseOverProperty,
            Value = true
        };

        var hoverThumbTemplate = new ControlTemplate(typeof(Thumb));

        var hoverThumbBorder = new FrameworkElementFactory(typeof(Border));

        hoverThumbBorder.SetValue(
            Border.BackgroundProperty,
            Brush(Accent));

        hoverThumbBorder.SetValue(
            Border.CornerRadiusProperty,
            new CornerRadius(5));

        hoverThumbBorder.SetValue(
            Border.MarginProperty,
            new Thickness(2, 2, 2, 2));

        hoverThumbTemplate.VisualTree = hoverThumbBorder;

        // Keep the scrollbar compact and dark.
        detailScroll.Resources.Add(
            typeof(ScrollBar),
            scrollStyle);

        var detailStack = new StackPanel();

        var profileBadge = new Border
        {
            Background = Brush(Color.FromRgb(48, 31, 41)),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(11, 6, 11, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 15),
            Child = Txt(
                "پرونده مشتری",
                11,
                Accent,
                FontWeights.SemiBold)
        };

        detailStack.Children.Add(profileBadge);

        detailStack.Children.Add(_detailName);
        detailStack.Children.Add(_detailPhone);

        detailStack.Children.Add(ProfileLine("شناسه مشتری", _detailId));
        detailStack.Children.Add(ProfileLine("کد آخرین رزرو", _detailBookingId));
        detailStack.Children.Add(ProfileLine("آخرین رزرو", _detailLast));
        detailStack.Children.Add(ProfileLine("مبلغ آخرین رزرو", _detailAmount));
        detailStack.Children.Add(ProfileLine("عملکرد ۳۰ روز", _detailPeriod));

        detailStack.Children.Add(SectionTitle(
            "سوابق اخیر",
            new Thickness(0, 20, 0, 9)));

        var historyCard = new Border
        {
            Background = Brush(Color.FromRgb(17, 19, 27)),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12),
            Child = _detailHistory
        };

        detailStack.Children.Add(historyCard);

        detailStack.Children.Add(SectionTitle(
            "یادداشت داخلی",
            new Thickness(0, 20, 0, 5)));

        detailStack.Children.Add(Txt(
            "این یادداشت فقط روی همین کامپیوتر ذخیره می‌شود.",
            10,
            Muted,
            margin: new Thickness(0, 0, 0, 10)));

        StyleInput(_customerNote);

        detailStack.Children.Add(_customerNote);

        detailStack.Children.Add(MakeButton(
            "ذخیره یادداشت مشتری",
            (_, _) => SaveSelectedCustomerNote(),
            true));

        detailScroll.Content = detailStack;

        detail.Child = detailScroll;

        Grid.SetColumn(detail, 1);
        body.Children.Add(detail);

        Grid.SetRow(body, 1);
        page.Children.Add(body);

        return page;
    }


    private void ConfigureCustomerGrid()
    {
        _customerGrid.AutoGenerateColumns = false;
        _customerGrid.ItemsSource = _customers;
        _customerGrid.IsReadOnly = true;
        _customerGrid.SelectionMode = DataGridSelectionMode.Single;
        _customerGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _customerGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _customerGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _customerGrid.Background = Brush(Surface);
        _customerGrid.Foreground = Brush(Text);
        _customerGrid.BorderThickness = new Thickness(0);
        _customerGrid.RowBackground = Brush(Surface);
        _customerGrid.AlternatingRowBackground =
            Brush(Color.FromRgb(25, 28, 38));

        _customerGrid.HorizontalGridLinesBrush = Brush(LineColor);
        _customerGrid.VerticalGridLinesBrush = Brushes.Transparent;

        _customerGrid.FontFamily = Peyda;
        _customerGrid.FontSize = 12;
        _customerGrid.RowHeight = 52;
        _customerGrid.ColumnHeaderHeight = 45;
        _customerGrid.CanUserAddRows = false;
        _customerGrid.CanUserDeleteRows = false;
        _customerGrid.CanUserResizeRows = false;
        _customerGrid.SelectionChanged += (_, _) =>
            ShowCustomerDetails(_customerGrid.SelectedItem as CustomerRecord);

        var headerStyle = new Style(typeof(DataGridColumnHeader));

        headerStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(Color.FromRgb(31, 34, 46))));

        headerStyle.Setters.Add(new Setter(
            Control.ForegroundProperty, Brush(Muted)));

        headerStyle.Setters.Add(new Setter(
            Control.FontFamilyProperty, Peyda));

        headerStyle.Setters.Add(new Setter(
            Control.FontWeightProperty, FontWeights.SemiBold));

        headerStyle.Setters.Add(new Setter(
            Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));

        headerStyle.Setters.Add(new Setter(
            Control.BorderBrushProperty, Brush(LineColor)));

        headerStyle.Setters.Add(new Setter(
            Control.PaddingProperty, new Thickness(12, 8, 12, 8)));

        _customerGrid.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));

        cellStyle.Setters.Add(new Setter(
            Control.ForegroundProperty, Brush(Text)));

        cellStyle.Setters.Add(new Setter(
            Control.BackgroundProperty, Brushes.Transparent));

        cellStyle.Setters.Add(new Setter(
            Control.BorderThicknessProperty, new Thickness(0)));

        cellStyle.Setters.Add(new Setter(
            Control.PaddingProperty, new Thickness(10, 7, 10, 7)));

        var selectedCell = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true
        };

        selectedCell.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(Color.FromRgb(66, 35, 45))));

        selectedCell.Setters.Add(new Setter(
            Control.ForegroundProperty, Brushes.White));

        cellStyle.Triggers.Add(selectedCell);
        _customerGrid.CellStyle = cellStyle;

        _customerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "نام مشتری",
            Binding = new System.Windows.Data.Binding(nameof(CustomerRecord.Name)),
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star)
        });

        _customerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "شماره موبایل",
            Binding = new System.Windows.Data.Binding(nameof(CustomerRecord.Phone)),
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });

        _customerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "رزرو ۳۰ روز",
            Binding = new System.Windows.Data.Binding(nameof(CustomerRecord.PeriodBookings)),
            Width = new DataGridLength(.8, DataGridLengthUnitType.Star)
        });

        _customerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "مجموع مبلغ",
            Binding = new System.Windows.Data.Binding(nameof(CustomerRecord.PeriodAmountText)),
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });
    }

    // =========================================================
    // REPORTS PAGE
    // =========================================================

    private UIElement BuildReportsPage()
    {
        var page = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0)
        };

        page.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        page.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        page.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });

        // DATE FILTER
        var filter = new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(13),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var filterGrid = new Grid();

        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        filterGrid.Children.Add(Txt(
            "بازه گزارش",
            12,
            Muted,
            FontWeights.SemiBold));

        StyleInput(_from);
        _from.Width = 132;
        _from.Margin = new Thickness(13, 0, 8, 0);

        Grid.SetColumn(_from, 1);
        filterGrid.Children.Add(_from);

        StyleInput(_to);
        _to.Width = 132;
        _to.Margin = new Thickness(0, 0, 10, 0);

        Grid.SetColumn(_to, 2);
        filterGrid.Children.Add(_to);

        var quick = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        quick.Children.Add(MakeButton("۷ روز", (_, _) => SetRange(7), false, 72));
        quick.Children.Add(MakeButton("۳۰ روز", (_, _) => SetRange(30), false, 78));
        quick.Children.Add(MakeButton("ماه جاری", (_, _) => SetCurrentMonth(), false, 92));

        Grid.SetColumn(quick, 3);
        filterGrid.Children.Add(quick);

        var refresh = MakeButton(
            "اعمال بازه",
            async (_, _) => await LoadReportAsync(),
            true,
            112);

        Grid.SetColumn(refresh, 4);
        filterGrid.Children.Add(refresh);

        filter.Child = filterGrid;
        page.Children.Add(filter);

        // KPI CARDS
        var kpiGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 15)
        };

        for (int i = 0; i < 4; i++)
        {
            kpiGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            if (i < 3)
            {
                kpiGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(10)
                });
            }
        }

        string[] labels =
        {
            "کل رزروها",
            "رزرو دستی",
            "رزرو آنلاین",
            "مجموع مبالغ ثبت‌شده"
        };

        Color[] colors = { Blue, Amber, Accent, Green };

        for (int i = 0; i < labels.Length; i++)
        {
            var value = Txt(
                "—",
                23,
                Text,
                FontWeights.Bold);

            var card = KpiCard(labels[i], value, colors[i]);

            Grid.SetColumn(card, i * 2);
            kpiGrid.Children.Add(card);

            _kpiValues.Add(value);
        }

        Grid.SetRow(kpiGrid, 1);
        page.Children.Add(kpiGrid);

        // CHARTS
        var charts = new Grid();

        charts.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        charts.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        charts.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        charts.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto
        });

        var reservationCard = ChartCard(
            "ترکیب رزروها",
            "مقایسه تعداد رزروهای دستی و آنلاین",
            _reservationChart);

        reservationCard.Margin = new Thickness(0, 0, 7, 7);
        charts.Children.Add(reservationCard);

        var amountCard = ChartCard(
            "ترکیب مبالغ",
            "مبالغ ثبت‌شده بر اساس منبع رزرو",
            _amountChart);

        amountCard.Margin = new Thickness(7, 0, 0, 7);

        if (amountCard.Child is StackPanel amountStack)
        {
            amountStack.Children.Add(new Border
            {
                Height = 1,
                Background = Brush(LineColor),
                Margin = new Thickness(0, 15, 0, 11)
            });

            amountStack.Children.Add(_reportNote);
        }

        Grid.SetColumn(amountCard, 1);
        charts.Children.Add(amountCard);

        var peakCard = ChartCard(
            "ساعت‌های پرتردد",
            "ساعت شروع رزروها در بازه انتخاب‌شده",
            _peakHoursChart);

        peakCard.Margin = new Thickness(0, 7, 7, 0);

        Grid.SetRow(peakCard, 1);
        charts.Children.Add(peakCard);

        var systemsCard = ChartCard(
            "سیستم‌ها و مشتریان پرتکرار",
            "بیشترین تعداد مراجعه و رزرو",
            _systemCustomerChart);

        systemsCard.Margin = new Thickness(7, 7, 0, 0);

        Grid.SetRow(systemsCard, 1);
        Grid.SetColumn(systemsCard, 1);

        charts.Children.Add(systemsCard);

        var chartScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = charts
        };

        Grid.SetRow(chartScroll, 2);
        page.Children.Add(chartScroll);

        return page;
    }

    // =========================================================
    // MAINTENANCE PAGE
    // =========================================================

    private UIElement BuildMaintenancePage()
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        // NOTES
        var notes = Card();
        notes.Margin = new Thickness(0, 0, 7, 0);

        var stack = new StackPanel();

        stack.Children.Add(SectionTitle("یادداشت شیفت و تعمیرات"));

        stack.Children.Add(Txt(
            "نکته‌های مهم، مشکلات دستگاه‌ها و موارد قابل پیگیری را ثبت کن.",
            11,
            Muted,
            margin: new Thickness(0, 5, 0, 15)));

        StyleInput(_maintenanceText);

        _maintenanceText.MinHeight = 190;
        stack.Children.Add(_maintenanceText);

        var localNotice = new Border
        {
            Background = Brush(Color.FromRgb(48, 39, 26)),
            BorderBrush = Brush(Color.FromRgb(91, 70, 38)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 12, 0, 12),
            Child = Txt(
                "این اطلاعات فقط روی همین کامپیوتر ذخیره می‌شود و بین دستگاه‌ها همگام نیست.",
                11,
                Amber)
        };

        stack.Children.Add(localNotice);

        stack.Children.Add(MakeButton(
            "ذخیره یادداشت شیفت",
            (_, _) =>
            {
                SaveLocalData();
                Log("یادداشت شیفت ذخیره شد.");
                SetStatus("یادداشت شیفت ذخیره شد.");
            },
            true));

        notes.Child = stack;
        grid.Children.Add(notes);

        // AUDIT
        var audit = Card();
        audit.Margin = new Thickness(7, 0, 0, 0);

        var auditStack = new StackPanel();

        auditStack.Children.Add(SectionTitle("سوابق فعالیت"));

        auditStack.Children.Add(Txt(
            "آخرین رویدادهای ثبت‌شده در این دستگاه",
            11,
            Muted,
            margin: new Thickness(0, 5, 0, 14)));

        _auditList.Background = Brushes.Transparent;
        _auditList.Foreground = Brush(Text);
        _auditList.BorderThickness = new Thickness(0);
        _auditList.FontFamily = Peyda;
        _auditList.FontSize = 11;
        _auditList.Padding = new Thickness(0);

        auditStack.Children.Add(_auditList);

        audit.Child = auditStack;

        Grid.SetColumn(audit, 1);
        grid.Children.Add(audit);

        return grid;
    }

    // =========================================================
    // RELIABILITY PAGE
    // =========================================================

    private UIElement BuildReliabilityPage()
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0)
        };

        stack.Children.Add(InfoCard(
            "اتصال امن",
            "درخواست‌های این بخش با HTTPS و توکن نشست فعلی ارسال می‌شوند. توکن در این صفحه روی دیسک ذخیره نمی‌شود.",
            Green));

        stack.Children.Add(InfoCard(
            "داده‌های مشتری",
            "فهرست مشتریان از endpoint جست‌وجوی رزروهای دستی دریافت می‌شود. این endpoint تاریخچه کامل همه سفارش‌های آنلاین و دفترکل مشتری را ارائه نمی‌کند.",
            Blue));

        stack.Children.Add(InfoCard(
            "محدودیت گزارش",
            "ساعت‌های پرتردد، سیستم‌های پرتکرار و مشتریان پرتکرار از جزئیات رزروهای Sync محاسبه می‌شوند. سود خالص و گزارش حسابداری به دفترکل مالی نیاز دارند.",
            Amber));

        stack.Children.Add(InfoCard(
            "وضعیت نقشه سالن",
            "نقشه سالن وضعیت رزرو و ظرفیت را از روی رزروها نمایش می‌دهد. روشن بودن دستگاه یا اتصال زنده کلاینت در این نسخه اندازه‌گیری نمی‌شود.",
            Accent));

        stack.Children.Add(MakeButton(
            "بررسی اتصال API",
            async (_, _) =>
            {
                try
                {
                    using var d = await _api.GetReportAsync(
                        _gamenet.Id,
                        DateTime.Today.ToString("yyyy-MM-dd"),
                        DateTime.Today.ToString("yyyy-MM-dd"));

                    SetStatus("ارتباط API برقرار است.");
                    Log("بررسی اتصال موفق بود.");
                }
                catch (Exception ex)
                {
                    SetStatus("اتصال ناموفق: " + ex.Message);
                    Log("بررسی اتصال ناموفق بود.");
                }
            },
            true,
            165));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack
        };
    }

    // =========================================================
    // CUSTOMER SEARCH
    // =========================================================

    private async Task SearchCustomersAsync()
    {
        if (_busy) return;

        string q = _customerQuery.Text.Trim();

        if (q.Length == 1)
        {
            SetStatus("برای جست‌وجو حداقل دو نویسه وارد کنید.");
            return;
        }

        try
        {
            SetBusy(true, "در حال دریافت پرونده مشتریان و سوابق اخیر...");

            var records = new List<CustomerRecord>();

            using (var doc = await _api.SearchCustomersAsync(_gamenet.Id, q))
            {
                JsonElement payload = Unwrap(doc.RootElement);

                if (TryArray(payload, "customers", out var arr))
                {
                    foreach (var c in arr.EnumerateArray())
                    {
                        var record = new CustomerRecord
                        {
                            Name = S(c, "customer"),
                            Phone = S(c, "phone"),
                            CustomerId = S(c, "customer_id"),
                            LastBookingId = S(c, "last_booking_id"),
                            LastDate = S(c, "date_iso"),
                            Amount = N(c, "amount")
                        };

                        if (string.IsNullOrWhiteSpace(record.Name))
                            record.Name = "مشتری بدون نام";

                        records.Add(record);
                    }
                }
            }

            string historyWarning = string.Empty;

            try
            {
                string from = DateTime.Today.AddDays(-29)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                string to = DateTime.Today
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var sync = await _bookingApi.SyncAsync(
                    _gamenet.Id,
                    from,
                    to);

                var bookings = _bookingApi.GetReservationsFromSync(sync);

                foreach (var booking in bookings.Where(
                    b => !string.IsNullOrWhiteSpace(b.Customer)))
                {
                    var record = records.FirstOrDefault(c =>
                        (!string.IsNullOrWhiteSpace(booking.Phone) &&
                         string.Equals(
                             c.Phone,
                             booking.Phone,
                             StringComparison.OrdinalIgnoreCase)) ||
                        (string.IsNullOrWhiteSpace(booking.Phone) &&
                         string.Equals(
                             c.Name,
                             booking.Customer,
                             StringComparison.OrdinalIgnoreCase)));

                    if (record == null)
                    {
                        record = new CustomerRecord
                        {
                            Name = booking.Customer,
                            Phone = booking.Phone
                        };

                        records.Add(record);
                    }

                    record.PeriodBookings++;
                    record.PeriodAmount += (double)booking.Amount;

                    record.RecentHistory.Add(
                        $"{booking.Date}  •  {booking.StartTime}  •  {booking.Amount:N0} تومان");

                    if (string.CompareOrdinal(
                        booking.Date,
                        record.LastDate) > 0)
                    {
                        record.LastDate = booking.Date;
                        record.Amount = (double)booking.Amount;
                        record.LastBookingId = booking.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                historyWarning = "سوابق ۳۰ روز اخیر کامل دریافت نشد: " + ex.Message;
            }

            _customerRecords = records;

            ApplyCustomerFilter();

            _customerGrid.SelectedItem = _customers.FirstOrDefault();

            SetStatus(
                _customers.Count == 0
                    ? "مشتری‌ای با این مشخصات پیدا نشد."
                    : $"{_customers.Count:N0} مشتری • سوابق تجمیعی ۳۰ روز اخیر؛ یادداشت‌ها محلی هستند. {historyWarning}");
        }
        catch (Exception ex)
        {
            SetStatus("دریافت مشتریان ناموفق بود: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyCustomerFilter()
    {
        IEnumerable<CustomerRecord> filtered = _customerRecords;

        string q = _customerQuery.Text.Trim();

        if (q.Length >= 2)
        {
            filtered = filtered.Where(c =>
                (c.Name + " " + c.Phone + " " + c.CustomerId)
                .Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        string filter = _customerFilter.SelectedItem?.ToString()
                        ?? "همه مشتریان";

        if (filter == "مشتریان پرتکرار")
            filtered = filtered.Where(c => c.PeriodBookings >= 2);

        else if (filter == "دارای شماره تماس")
            filtered = filtered.Where(c =>
                !string.IsNullOrWhiteSpace(c.Phone));

        else if (filter == "بدون شماره تماس")
            filtered = filtered.Where(c =>
                string.IsNullOrWhiteSpace(c.Phone));

        _customers.Clear();

        foreach (var item in filtered
                     .OrderByDescending(x => x.PeriodBookings)
                     .ThenBy(x => x.Name))
        {
            _customers.Add(item);
        }

        _customerCount.Text = $"{_customers.Count:N0} مشتری";
    }

    // =========================================================
    // REPORT LOADING
    // =========================================================

    private async Task LoadReportAsync()
    {
        if (_busy) return;

        if (!DateTime.TryParseExact(
                _from.Text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var from) ||
            !DateTime.TryParseExact(
                _to.Text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var to) ||
            to < from)
        {
            SetStatus("تاریخ‌ها باید معتبر و با قالب YYYY-MM-DD باشند.");
            return;
        }

        try
        {
            SetBusy(true, "در حال ساخت گزارش بازه انتخاب‌شده...");

            using var doc = await _api.GetReportAsync(
                _gamenet.Id,
                _from.Text.Trim(),
                _to.Text.Trim());

            JsonElement root = Unwrap(doc.RootElement);

            List<ReservationItem> detailedBookings = new();
            string detailWarning = string.Empty;

            try
            {
                var sync = await _bookingApi.SyncAsync(
                    _gamenet.Id,
                    _from.Text.Trim(),
                    _to.Text.Trim());

                detailedBookings =
                    _bookingApi.GetReservationsFromSync(sync);
            }
            catch (Exception detailEx)
            {
                detailWarning =
                    "تحلیل جزئیات رزرو در دسترس نبود: " + detailEx.Message;
            }

            JsonElement reservations = Get(root, "reservations");
            JsonElement amounts = Get(root, "amounts");

            double manualCount = N(reservations, "manual");
            double onlineCount = N(reservations, "online");
            double totalCount = N(reservations, "total");

            double manualAmount = N(amounts, "manual");
            double onlineAmount = N(amounts, "online");
            double totalAmount = N(amounts, "total");

            SetKpi(0, $"{totalCount:N0}");
            SetKpi(1, $"{manualCount:N0}");
            SetKpi(2, $"{onlineCount:N0}");
            SetKpi(3, $"{totalAmount:N0} تومان");

            BuildBars(
                _reservationChart,
                new[]
                {
                    ("رزرو دستی", manualCount, Amber),
                    ("رزرو آنلاین", onlineCount, Accent)
                },
                Math.Max(1, manualCount + onlineCount),
                "رزرو");

            BuildBars(
                _amountChart,
                new[]
                {
                    ("رزرو دستی", manualAmount, Amber),
                    ("رزرو آنلاین", onlineAmount, Accent)
                },
                Math.Max(1, manualAmount + onlineAmount),
                "تومان");

            BuildDetailedInsights(detailedBookings);

            string note = S(root, "note");

            _reportNote.Text = string.Join(
                "  ",
                new[] { note, detailWarning }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

            SetStatus(
                $"گزارش از {from:yyyy-MM-dd} تا {to:yyyy-MM-dd} به‌روزرسانی شد.");

            Log("گزارش رزرو و مبالغ دریافت شد.");
        }
        catch (Exception ex)
        {
            SetStatus("دریافت گزارش ناموفق بود: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // =========================================================
    // CHARTS
    // =========================================================

    private void BuildBars(
        Panel host,
        (string Label, double Value, Color Color)[] values,
        double max,
        string unit)
    {
        host.Children.Clear();

        foreach (var item in values)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 8, 0, 9)
            };

            row.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            row.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(13)
            });

            var labelRow = new Grid();

            labelRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            labelRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            labelRow.Children.Add(Txt(
                item.Label,
                12,
                Text,
                FontWeights.SemiBold));

            var valueText = Txt(
                $"{item.Value:N0} {unit}",
                11,
                Muted);

            Grid.SetColumn(valueText, 1);
            labelRow.Children.Add(valueText);

            row.Children.Add(labelRow);

            var track = new Border
            {
                Background = Brush(Color.FromRgb(39, 42, 54)),
                CornerRadius = new CornerRadius(7),
                Height = 7,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            };

            var progress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(item.Value / max * 100, 0, 100),
                Height = 7,
                Foreground = Brush(item.Color),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };

            track.Child = progress;

            Grid.SetRow(track, 1);
            row.Children.Add(track);

            host.Children.Add(row);
        }
    }

    private void BuildDetailedInsights(List<ReservationItem> bookings)
    {
        var hours = bookings
            .Select(b => int.TryParse(
                (b.StartTime ?? string.Empty)
                .Split(':')
                .FirstOrDefault(),
                out var h) ? h : -1)
            .Where(h => h >= 0 && h <= 23)
            .GroupBy(h => h)
            .Select(g => ($"{g.Key:00}:00", (double)g.Count(), Accent))
            .OrderByDescending(x => x.Item2)
            .Take(6)
            .ToArray();

        if (hours.Length == 0)
        {
            BuildBars(
                _peakHoursChart,
                new[]
                {
                    ("اطلاعات ساعت شروع در این بازه موجود نیست", 0d, Muted)
                },
                1,
                "رزرو");
        }
        else
        {
            BuildBars(
                _peakHoursChart,
                hours,
                Math.Max(1, hours.Max(x => x.Item2)),
                "رزرو");
        }

        var systems = bookings
            .SelectMany(b => b.Systems ?? new List<ReservationSystem>())
            .Where(x => !string.IsNullOrWhiteSpace(x.SystemNumber))
            .GroupBy(
                x => x.SystemNumber.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                $"سیستم {g.Key}",
                (double)g.Sum(x => Math.Max(1, x.Quantity)),
                Green))
            .OrderByDescending(x => x.Item2)
            .Take(5)
            .ToArray();

        var customers = bookings
            .Where(b => !string.IsNullOrWhiteSpace(b.Customer))
            .GroupBy(
                b => !string.IsNullOrWhiteSpace(b.Phone)
                    ? b.Phone.Trim()
                    : b.Customer.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                $"{g.First().Customer}  •  {g.Count()} رزرو",
                (double)g.Count(),
                Amber))
            .OrderByDescending(x => x.Item2)
            .Take(5)
            .ToArray();

        _systemCustomerChart.Children.Clear();

        _systemCustomerChart.Children.Add(SectionTitle(
            "سیستم‌های پرتکرار"));

        var systemBars = new StackPanel();
        _systemCustomerChart.Children.Add(systemBars);

        if (systems.Length == 0)
        {
            systemBars.Children.Add(Txt(
                "شماره سیستم در داده رزروهای این بازه ثبت نشده است.",
                11,
                Muted,
                margin: new Thickness(0, 6, 0, 10)));
        }
        else
        {
            BuildBars(
                systemBars,
                systems,
                Math.Max(1, systems.Max(x => x.Item2)),
                "بار");
        }

        _systemCustomerChart.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(LineColor),
            Margin = new Thickness(0, 13, 0, 12)
        });

        _systemCustomerChart.Children.Add(SectionTitle(
            "مشتریان پرتکرار"));

        var customerBars = new StackPanel();
        _systemCustomerChart.Children.Add(customerBars);

        if (customers.Length == 0)
        {
            customerBars.Children.Add(Txt(
                "مشتری ثبت‌شده‌ای در این بازه پیدا نشد.",
                11,
                Muted,
                margin: new Thickness(0, 6, 0, 0)));
        }
        else
        {
            BuildBars(
                customerBars,
                customers,
                Math.Max(1, customers.Max(x => x.Item2)),
                "رزرو");
        }
    }

    private void SetKpi(int index, string value)
    {
        if (index >= 0 && index < _kpiValues.Count)
            _kpiValues[index].Text = value;
    }

    // =========================================================
    // CUSTOMER PROFILE
    // =========================================================

    private void ShowCustomerDetails(CustomerRecord? c)
    {
        if (c == null)
        {
            _detailName.Text = "یک مشتری را انتخاب کنید";
            _detailPhone.Text = "";

            _detailId.Text =
                _detailBookingId.Text =
                _detailLast.Text =
                _detailAmount.Text =
                _detailPeriod.Text = "—";

            _detailHistory.Text = "";
            _customerNote.Text = "";

            return;
        }

        _detailName.Text = c.Name;

        _detailPhone.Text =
            string.IsNullOrWhiteSpace(c.Phone)
                ? "شماره موبایل ثبت نشده"
                : c.Phone;

        _detailId.Text =
            string.IsNullOrWhiteSpace(c.CustomerId)
                ? "ثبت نشده"
                : c.CustomerId;

        _detailBookingId.Text =
            string.IsNullOrWhiteSpace(c.LastBookingId)
                ? "ثبت نشده"
                : c.LastBookingId;

        _detailLast.Text =
            string.IsNullOrWhiteSpace(c.LastDate)
                ? "نامشخص"
                : c.LastDate;

        _detailAmount.Text = $"{c.Amount:N0} تومان";

        _detailPeriod.Text =
            $"{c.PeriodBookings:N0} رزرو • {c.PeriodAmount:N0} تومان";

        _detailHistory.Text =
            c.RecentHistory.Count == 0
                ? "جزئیات رزرو در این بازه ثبت نشده است."
                : string.Join(
                    "\n",
                    c.RecentHistory.TakeLast(5).Reverse());

        _customerNote.Text =
            _customerNotes.TryGetValue(c.Key, out var note)
                ? note
                : "";
    }

    private void SaveSelectedCustomerNote()
    {
        if (_customerGrid.SelectedItem is not CustomerRecord c)
        {
            SetStatus("ابتدا یک مشتری را انتخاب کنید.");
            return;
        }

        _customerNotes[c.Key] = _customerNote.Text;

        SaveLocalData();

        SetStatus("یادداشت مشتری روی همین دستگاه ذخیره شد.");
    }

    // =========================================================
    // DATE FILTERS
    // =========================================================

    private void SetRange(int days)
    {
        _from.Text = DateTime.Today
            .AddDays(-(days - 1))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        _to.Text = DateTime.Today
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        _ = LoadReportAsync();
    }

    private void SetCurrentMonth()
    {
        var today = DateTime.Today;

        _from.Text = new DateTime(today.Year, today.Month, 1)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        _to.Text = today
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        _ = LoadReportAsync();
    }

    // =========================================================
    // LOCAL STORAGE AND AUDIT
    // =========================================================

    private string LocalDataPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lobix",
            "ops-" + _gamenet.Id + ".json");

    private void LoadLocalData()
    {
        try
        {
            if (!File.Exists(LocalDataPath))
                return;

            using var doc = JsonDocument.Parse(
                File.ReadAllText(LocalDataPath));

            var root = doc.RootElement;

            if (root.TryGetProperty("notes", out var notes) &&
                notes.ValueKind == JsonValueKind.String)
            {
                _maintenanceText.Text = notes.GetString() ?? "";
            }

            if (root.TryGetProperty("audit", out var audit) &&
                audit.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in audit.EnumerateArray())
                    _auditList.Items.Add(item.GetString() ?? "");
            }

            if (root.TryGetProperty("customer_notes", out var customerNotes) &&
                customerNotes.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in customerNotes.EnumerateObject())
                    _customerNotes[p.Name] = p.Value.GetString() ?? "";
            }
        }
        catch
        {
            // A damaged local file must not prevent the page from opening.
        }
    }

    private void SaveLocalData()
    {
        try
        {
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(LocalDataPath)!);

            var data = new
            {
                notes = _maintenanceText.Text,
                audit = _auditList.Items
                    .Cast<object>()
                    .Select(x => x?.ToString() ?? "")
                    .Take(100)
                    .ToArray(),
                customer_notes = _customerNotes
            };

            File.WriteAllText(
                LocalDataPath,
                JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            SetStatus("ذخیره محلی انجام نشد: " + ex.Message);
        }
    }

    private void Log(string line)
    {
        _auditList.Items.Insert(
            0,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  •  {line}");

        while (_auditList.Items.Count > 100)
            _auditList.Items.RemoveAt(_auditList.Items.Count - 1);

        SaveLocalData();
    }

    // =========================================================
    // BUSY AND STATUS
    // =========================================================

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;

        _loadingLayer.Visibility =
            busy ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(message))
            SetStatus(message);
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.ToolTip = text;
    }

    // =========================================================
    // VISUAL COMPONENTS
    // =========================================================

    private static Style BuildTabStyle()
    {
        var style = new Style(typeof(TabItem));

        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(Muted)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(Surface)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 12, 20, 12)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 48d));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 125d));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 7, 0)));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, Peyda));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(LineColor)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        var template = new ControlTemplate(typeof(TabItem));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,
            new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,
            new TemplateBindingExtension(Control.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        presenter.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 2, 0));

        border.AppendChild(presenter);
        template.VisualTree = border;

        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        var selected = new Trigger
        {
            Property = TabItem.IsSelectedProperty,
            Value = true
        };

        selected.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(Color.FromRgb(55, 28, 37))));

        selected.Setters.Add(new Setter(
            Control.ForegroundProperty,
            Brushes.White));

        selected.Setters.Add(new Setter(
            Control.BorderBrushProperty,
            Brush(Accent)));

        style.Triggers.Add(selected);

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };

        hover.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(Color.FromRgb(39, 33, 43))));

        style.Triggers.Add(hover);

        return style;
    }
    private static Border KpiCard(
        string label,
        TextBlock value,
        Color indicator)
    {
        var stack = new StackPanel();

        var top = new Grid();

        top.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        top.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        top.Children.Add(Txt(
            label,
            11,
            Muted,
            FontWeights.SemiBold));

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(5),
            Background = Brush(indicator),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(dot, 1);
        top.Children.Add(dot);

        stack.Children.Add(top);

        value.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(value);

        return new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(17, 15, 17, 15),
            Child = stack
        };
    }

    private static Border ChartCard(
        string title,
        string subtitle,
        UIElement content)
    {
        var stack = new StackPanel();

        stack.Children.Add(Txt(
            title,
            15,
            Text,
            FontWeights.Bold));

        stack.Children.Add(Txt(
            subtitle,
            11,
            Muted,
            margin: new Thickness(0, 4, 0, 15)));

        stack.Children.Add(content);

        return new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(17),
            Child = stack
        };
    }

    private static Border InfoCard(
        string title,
        string detail,
        Color indicator)
    {
        var layout = new Grid();

        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(4)
        });

        layout.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        var accentLine = new Border
        {
            Background = Brush(indicator),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 2, 0, 2)
        };

        layout.Children.Add(accentLine);

        var stack = new StackPanel
        {
            Margin = new Thickness(15, 0, 0, 0)
        };

        stack.Children.Add(Txt(
            title,
            15,
            Text,
            FontWeights.SemiBold));

        stack.Children.Add(Txt(
            detail,
            11,
            Muted,
            margin: new Thickness(0, 7, 0, 0)));

        Grid.SetColumn(stack, 1);
        layout.Children.Add(stack);

        return new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 11),
            Child = layout
        };
    }

    private static Border ProfileLine(
        string label,
        TextBlock value)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 8)
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(112)
        });

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        grid.Children.Add(Txt(label, 11, Muted));

        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        return new Border
        {
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 7),
            Child = grid
        };
    }

    private static Border Card()
    {
        return new Border
        {
            Background = Brush(Surface),
            BorderBrush = Brush(LineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(17),
            ClipToBounds = true
        };
    }

    private static TextBlock SectionTitle(
        string title,
        Thickness? margin = null)
    {
        return Txt(
            title,
            15,
            Text,
            FontWeights.Bold,
            margin);
    }

    private static TextBlock NewValueText()
    {
        return new TextBlock
        {
            Foreground = Brush(Text),
            FontFamily = Peyda,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static TextBlock Txt(
        string text,
        double size,
        Color color,
        FontWeight? weight = null,
        Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = Peyda,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = weight ?? FontWeights.Normal,
            Margin = margin ?? new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static SolidColorBrush Brush(Color color)
    {
        return new SolidColorBrush(color);
    }

    // =========================================================
    // BUTTON STYLE
    // =========================================================

    private static Button MakeButton(
        string label,
        RoutedEventHandler click,
        bool primary = false,
        double minWidth = 96)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = minWidth,
            Height = 42,
            Margin = new Thickness(4, 0, 4, 0),
            Padding = new Thickness(15, 8, 15, 8),
            FontFamily = Peyda,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(Text),
            Background = Brush(primary ? Accent : Surface2),
            BorderBrush = Brush(primary ? Accent : LineColor),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null
        };

        var template = new ControlTemplate(typeof(Button));

        var border = new FrameworkElementFactory(typeof(Border));

        border.SetValue(
            Border.CornerRadiusProperty,
            new CornerRadius(11));

        border.SetValue(
            Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));

        border.SetValue(
            Border.BorderBrushProperty,
            new TemplateBindingExtension(Control.BorderBrushProperty));

        border.SetValue(
            Border.BorderThicknessProperty,
            new TemplateBindingExtension(Control.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));

        presenter.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);

        presenter.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);

        presenter.SetValue(
            ContentPresenter.RecognizesAccessKeyProperty,
            true);

        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };

        hover.Setters.Add(new Setter(
            Control.BackgroundProperty,
            Brush(primary
                ? Color.FromRgb(235, 72, 81)
                : Surface3)));

        hover.Setters.Add(new Setter(
            Control.BorderBrushProperty,
            Brush(primary ? Color.FromRgb(235, 72, 81) : Accent)));

        template.Triggers.Add(hover);

        var pressed = new Trigger
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true
        };

        pressed.Setters.Add(new Setter(
            UIElement.OpacityProperty,
            0.82));

        template.Triggers.Add(pressed);

        var disabled = new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };

        disabled.Setters.Add(new Setter(
            UIElement.OpacityProperty,
            0.45));

        template.Triggers.Add(disabled);

        button.Template = template;
        button.Click += click;

        return button;
    }

    // =========================================================
    // INPUT STYLE
    // =========================================================

    private static void StyleInput(Control control)
    {
        control.FontFamily = Peyda;
        control.FontSize = 12;
        control.Foreground = Brush(Text);
        control.Background = Brush(Color.FromRgb(17, 19, 27));
        control.BorderBrush = Brush(LineColor);
        control.BorderThickness = new Thickness(1);
        control.Padding = new Thickness(11, 8, 11, 8);

        if (control is TextBox textBox)
        {
            textBox.CaretBrush = Brush(Accent);
            textBox.SelectionBrush = Brush(Color.FromRgb(105, 43, 55));
        }
    }

    private static void StyleComboBox(ComboBox combo)
    {
        combo.FontFamily = Peyda;
        combo.FontSize = 11;
        combo.Foreground = Brush(Text);
        combo.Background = Brush(Surface2);
        combo.BorderBrush = Brush(LineColor);
        combo.BorderThickness = new Thickness(1);
        combo.Padding = new Thickness(9, 5, 9, 5);
    }

    // =========================================================
    // JSON HELPERS
    // =========================================================

    private static JsonElement Unwrap(JsonElement root)
    {
        return root.TryGetProperty("data", out var data) &&
               data.ValueKind == JsonValueKind.Object
            ? data
            : root;
    }

    private static JsonElement Get(JsonElement element, string key)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(key, out var value)
            ? value
            : default;
    }

    private static bool TryArray(
        JsonElement element,
        string key,
        out JsonElement array)
    {
        array = Get(element, key);
        return array.ValueKind == JsonValueKind.Array;
    }

    private static string S(JsonElement element, string key)
    {
        var value = Get(element, key);

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    private static double N(JsonElement element, string key)
    {
        var value = Get(element, key);

        return value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out var number)
            ? number
            : 0;
    }

    // =========================================================
    // CUSTOMER MODEL
    // =========================================================

    private sealed class CustomerRecord
    {
        public string Name { get; set; } = "";
        public string Phone { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string LastBookingId { get; set; } = "";
        public string LastDate { get; set; } = "";

        public double Amount { get; set; }

        public int PeriodBookings { get; set; }

        public double PeriodAmount { get; set; }

        public List<string> RecentHistory { get; } = new();

        public string PeriodAmountText =>
            $"{PeriodAmount:N0} تومان";

        public string Key =>
            !string.IsNullOrWhiteSpace(CustomerId) ||
            !string.IsNullOrWhiteSpace(Phone)
                ? CustomerId + "|" + Phone
                : Name;
    }
}
