using LobixApp.Models;
using LobixApp.Services;
using LobixApp.Services.Sync;
using LobixApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LobixApp.Views
{
    public partial class ReservationsView : UserControl
    {
        private sealed class TimelineSystemRow
        {
            public int VariationId { get; init; }
            public string SystemNumber { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public bool IsLegacy { get; init; }
        }

        // =========================================================
        // SERVICES / STATE
        // =========================================================
        private System.Windows.Threading.DispatcherTimer? _liveMapClock;
        private readonly LobixApiService _apiService;
        private readonly LobixExtendedApiService _extendedApiService;
        private readonly GamenetInfo _gamenet;
        private readonly OperatorSession _session;
        private readonly List<LiveMapObject> _selectedLiveMapObjects = new();
        private List<ReservationItem> _allReservations = new();
        private List<ReservationItem> _reservations = new();
        private List<GamenetVariation> _variations = new();

        private DateTime? _selectedDate;
        private string _selectedStatus = "all";
        private string _searchText = string.Empty;

        private bool _isLoading;

        private DateTime? _lastSuccessfulSync;

        private sealed class LiveMapObject
        {
            public string Id { get; init; } = string.Empty;
            public string Type { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public string SystemId { get; init; } = string.Empty;
            public string SystemNumber { get; init; } = string.Empty;
            public string Category { get; init; } = string.Empty;
            public string Floor { get; init; } = "طبقه اصلی";
            public int VariationId { get; init; }
            public bool Disabled { get; init; }
            public double X { get; init; }
            public double Y { get; init; }
            public double Width { get; init; }
            public double Height { get; init; }
            public double Rotation { get; init; }
        }

        private readonly List<LiveMapObject> _liveMapObjects = new();
        private readonly HashSet<string> _selectedLiveMapSystems = new(StringComparer.OrdinalIgnoreCase);
        private bool _liveMapMultiSelectMode;
        private readonly Dictionary<string, string> _serverAvailabilityStates = new(StringComparer.OrdinalIgnoreCase);
        private bool _availabilityRefreshing;
        private DateTime? _serverAvailabilityDate;
        private DateTime _lastAvailabilityAttempt = DateTime.MinValue;
        private bool _floorMapLoaded;
        private bool _floorMapLoading;
        private string _selectedMapFloor = "طبقه اصلی";
        private bool _suppressMapFloorChange;
        private string _floorMapLoadMessage = "نقشه هنوز بارگذاری نشده است.";

        private string _syncStatusText =
            "در حال آماده‌سازی...";

        private readonly DispatcherTimer _clockTimer;
        public string SyncStatusDisplay => _syncStatusText;

        // =========================================================
        // TIMELINE
        // =========================================================

        private const double LeftColumnWidth = 176;
        private double _hourWidth = 68;
        private double HourWidth => _hourWidth;
        private double _rowHeight = 52;
        private double RowHeight => _rowHeight;

        // =========================================================
        // FULLSCREEN TIMELINE
        // =========================================================

        private ScrollViewer? _activeTimelineScrollViewer;
        private Window? _timelineFullscreenWindow;
        private const double HeaderHeight = 42;

        private const int StartHour = 8;
        private const int EndHour = 24;

        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public ReservationsView(
     GamenetInfo gamenet,
     OperatorSession session)
        {
            InitializeComponent();

            // -----------------------------------------------------
            // Services & Session
            // -----------------------------------------------------

            _apiService = new LobixApiService();
            _extendedApiService = new LobixExtendedApiService(session.Token);

            _gamenet = gamenet;
            _session = session;

            _apiService.SetAuthentication(_session.Token);

            // -----------------------------------------------------
            // Clock Timer
            // -----------------------------------------------------

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _clockTimer.Tick += ClockTimer_Tick;

            // -----------------------------------------------------
            // Page Lifecycle Events
            // -----------------------------------------------------

            Loaded += ReservationsView_Loaded;
            Unloaded += ReservationsView_Unloaded;

            // -----------------------------------------------------
            // UI Event Handlers
            // -----------------------------------------------------

            PreviousDayButton.Click += PreviousDayButton_Click;
            NextDayButton.Click += NextDayButton_Click;
            DatePickerButton.Click += DatePickerButton_Click;
            RefreshReservationsButton.Click += RefreshReservationsButton_Click;
            NewReservationButton.Click += NewReservationButton_Click;

            // -----------------------------------------------------
            // Initial Setup
            // -----------------------------------------------------

            _clockTimer.Start();

            if (SearchPlaceholderTextBlock != null)
            {
                SearchPlaceholderTextBlock.Visibility = Visibility.Visible;
            }

            // -----------------------------------------------------
            // Initial Data Loading
            // -----------------------------------------------------

            _ = LoadReservationsAsync();
        }



        // =========================================================
        // LIFECYCLE
        // =========================================================

        private async void ReservationsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            DrawTimeline();
            if (!_floorMapLoaded)
                await LoadFloorMapAsync();
            else
                RenderLiveFloorMap();
        }

        private void ReservationsView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            _clockTimer.Stop();

            _extendedApiService.Dispose();
        }

        private void ClockTimer_Tick(
            object? sender,
            EventArgs e)
        {
            DrawTimeline();
            _ = RefreshDeviceAvailabilityAsync();
            RenderLiveFloorMap();
        }

        // =========================================================
        // LOAD / SYNC
        // =========================================================

        private async Task LoadReservationsAsync()
        {
            if (_isLoading)
                return;

            _isLoading = true;

            SetSyncStatus("در حال دریافت اطلاعات...");
            if (LoadingText != null)
                LoadingText.Text = "در حال دریافت و همگام‌سازی رزروها با سرور لابیکس...";
            bool showInitialLoading = _allReservations.Count == 0;
            if (LoadingOverlay != null && showInitialLoading)
            {
                LoadingOverlay.Opacity = 0;
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingOverlay.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(90)));
            }
            if (RefreshReservationsButton != null) RefreshReservationsButton.IsEnabled = false;
            if (NewReservationButton != null) NewReservationButton.IsEnabled = false;

            try
            {
                string from;
                string to;

                // -------------------------------------------------
                // اگر تاریخ مشخص شده باشد فقط همان روز را می‌گیریم
                // -------------------------------------------------

                if (_selectedDate.HasValue)
                {
                    from =
                        _selectedDate.Value.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        );

                    to = from;
                }
                else
                {
                    // -------------------------------------------------
                    // حالت همه تاریخ‌ها:
                    // امروز تا ۳۰ روز آینده
                    // -------------------------------------------------

                    from =
                        DateTime.Today.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        );

                    to =
                        DateTime.Today
                            .AddDays(30)
                            .ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture
                            );
                }

                // -------------------------------------------------
                // دریافت Sync فقط یک‌بار و استفاده از همان پاسخ
                // برای رزروها و اطلاعات سیستم‌ها
                // -------------------------------------------------

                GamenetSyncResponse sync =
                    await _apiService.SyncAsync(
                        _gamenet.Id,
                        from,
                        to
                    );

                var serverReservations = _apiService.GetReservationsFromSync(sync);

                var cachedBeforeRefresh = await LocalDatabase.Instance.GetEntityRecordsAsync<ReservationItem>(
                    "ManualBooking", _gamenet.Id, true);

                var protectedLocalIds = cachedBeforeRefresh
                    .Where(x => x.SyncState is "Pending" or "Sending" or "Retry" or "Conflict")
                    .Select(x => x.EntityId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var item in serverReservations.Where(IsManualReservation))
                {
                    if (!protectedLocalIds.Contains(item.Id))
                    {
                        await LocalDatabase.Instance.PutEntityAsync(
                            "ManualBooking",
                            item.Id,
                            _gamenet.Id,
                            item,
                            0,
                            "Synced");
                    }
                }

                var localRecords = await LocalDatabase.Instance.GetEntityRecordsAsync<ReservationItem>(
                    "ManualBooking", _gamenet.Id, true);



                var merged = serverReservations
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Last(),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var local in localRecords.Where(x => !x.IsDeleted || x.SyncState == "Conflict"))
                {
                    local.Payload.LocalSyncState = local.SyncState;
                    merged[local.EntityId] = local.Payload;
                }

                _allReservations = merged.Values.ToList();

                _variations =
                    sync.Data?.Variations
                    ?? new List<GamenetVariation>();

                foreach (var variation in _variations)
                {
                    await LocalDatabase.Instance.PutEntityAsync(
                        "Variation",
                        variation.Id.ToString(CultureInfo.InvariantCulture),
                        _gamenet.Id,
                        variation);
                }

                _lastSuccessfulSync = DateTime.Now;

                SetSyncStatus(
                    "همگام‌سازی موفق • " +
                    _lastSuccessfulSync.Value.ToString(
                        "HH:mm",
                        CultureInfo.InvariantCulture));

                ApplyFilters();
            }
            catch (Exception ex)
            {
                SetSyncStatus("آفلاین • نمایش اطلاعات ذخیره‌شده");

                try
                {
                    var cached = await LocalDatabase.Instance.GetEntityRecordsAsync<ReservationItem>(
                        "ManualBooking", _gamenet.Id, true);

                    foreach (var record in cached)
                        record.Payload.LocalSyncState = record.SyncState;

                    _allReservations = cached
                        .Where(x => !x.IsDeleted || x.SyncState == "Conflict")
                        .Select(x => x.Payload)
                        .ToList();

                    if (_variations.Count == 0)
                    {
                        _variations = (
                            await LocalDatabase.Instance.GetEntitiesAsync<GamenetVariation>(
                                "Variation", _gamenet.Id))
                            .ToList();
                    }

                    ApplyFilters();
                }
                catch (Exception cacheEx)
                {
                    System.Diagnostics.Debug.WriteLine("Local reservation cache: " + cacheEx);
                }

                // اگر تازه‌سازی ناموفق شد، اطلاعات آخرین بار روی صفحه باقی می‌ماند.
                if (_allReservations.Count == 0)
                {
                    _reservations = new List<ReservationItem>();
                    UpdateStatistics();
                    DrawTimeline();
                }

                if (_allReservations.Count == 0)
                {
                    LobixDialog.Show(
                        UserFacingErrors.GetMessage(
                            ex,
                            "دریافت رزروها ناموفق بود. اتصال را بررسی کنید و دوباره تلاش کنید."),
                        "دریافت رزروها ناموفق بود",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                _isLoading = false;
                if (LoadingOverlay != null && showInitialLoading)
                {
                    LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, null);
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoadingOverlay.Opacity = 1;
                }
                if (RefreshReservationsButton != null) RefreshReservationsButton.IsEnabled = true;
                if (NewReservationButton != null) NewReservationButton.IsEnabled = true;
            }
        }

        // =========================================================
        // SYNC STATUS
        // =========================================================

        private void SetSyncStatus(
            string text)
        {
            _syncStatusText = text;

            if (RefreshReservationsButton != null)
                RefreshReservationsButton.ToolTip = _syncStatusText;

            if (SyncStatusTextBlock != null)
            {
                SyncStatusTextBlock.Text = _syncStatusText;
                SyncStatusTextBlock.Foreground = text.Contains("خطا", StringComparison.OrdinalIgnoreCase)
                    ? (Brush)new SolidColorBrush(Color.FromRgb(248, 113, 113))
                    : text.Contains("موفق", StringComparison.OrdinalIgnoreCase)
                        ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                        : new SolidColorBrush(Color.FromRgb(251, 191, 36));
            }
        }

        // =========================================================
        // FILTERS
        // =========================================================

        private void ApplyFilters()
        {
            IEnumerable<ReservationItem> query =
                _allReservations;

            // -----------------------------------------------------
            // Date
            // -----------------------------------------------------

            if (_selectedDate.HasValue)
            {
                string selectedDate =
                    _selectedDate.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    );

                query =
                    query.Where(
                        reservation =>
                            string.Equals(
                                reservation.Date,
                                selectedDate,
                                StringComparison.Ordinal
                            )
                    );
            }


            // -----------------------------------------------------
            // Status
            // -----------------------------------------------------

            if (_selectedStatus == "rejected")
            {
                // فقط رزروهای رد شده
                query = query.Where(
                    reservation =>
                        NormalizeStatus(reservation.Status) == "rejected"
                );
            }
            else
            {
                // رزروهای رد شده در جدول اصلی نمایش داده نشوند
                query = query.Where(
                    reservation =>
                        NormalizeStatus(reservation.Status) != "rejected"
                );

                if (_selectedStatus != "all")
                {
                    query = query.Where(
                        reservation =>
                            NormalizeStatus(reservation.Status) == _selectedStatus
                    );
                }
            }

            // -----------------------------------------------------
            // Search
            // -----------------------------------------------------

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string search =
                    _searchText.Trim();

                query =
                    query.Where(
                        reservation =>
                            ContainsText(
                                reservation.Customer,
                                search
                            )
                            ||
                            ContainsText(
                                reservation.Phone,
                                search
                            )
                            ||
                            ContainsText(
                                reservation.Id,
                                search
                            )
                            ||
                            ContainsText(
                                reservation.OrderId.ToString(
                                    CultureInfo.InvariantCulture
                                ),
                                search
                            )
                            || ContainsText(reservation.Note, search)
                            || ContainsText(reservation.Source, search)
                            || (reservation.Systems?.Any(system =>
                                ContainsText(system.SystemNumber, search) ||
                                ContainsText(GetVariationName(system.VariationId), search)) ?? false)
                    );
            }

            // -----------------------------------------------------
            // Sort
            // -----------------------------------------------------

            _reservations =
                query
                    .OrderBy(
                        reservation =>
                            reservation.Date
                    )
                    .ThenBy(
                        reservation =>
                            ParseStartHour(
                                reservation.StartTime
                            )
                    )
                    .ThenBy(
                        reservation =>
                            reservation.StartTime
                    )
                    .ToList();

            UpdateStatistics();

            DrawTimeline();
            RenderLiveFloorMap();
            if (_floorMapLoaded)
                _ = RefreshDeviceAvailabilityAsync();
        }

        // =========================================================
        // STATISTICS
        // =========================================================

        private void UpdateStatistics()
        {
            if (TotalReservationsText != null)
            {
                TotalReservationsText.Text =
                    _reservations.Count.ToString(
                        CultureInfo.InvariantCulture
                    );
            }

            if (PendingReservationsText != null)
            {
                PendingReservationsText.Text =
                    _reservations.Count(
                        item =>
                            NormalizeStatus(
                                item.Status
                            )
                            ==
                            "pending"
                    )
                    .ToString(
                        CultureInfo.InvariantCulture
                    );
            }

            if (ConfirmedReservationsText != null)
            {
                ConfirmedReservationsText.Text =
                    _reservations.Count(
                        item =>
                            NormalizeStatus(
                                item.Status
                            )
                            ==
                            "confirmed"
                    )
                    .ToString(
                        CultureInfo.InvariantCulture
                    );
            }

            if (SelectedDateText != null)
            {
                SelectedDateText.Text =
                    _selectedDate.HasValue
                        ? ToPersianDate(
                            _selectedDate.Value
                        )
                        : "همه تاریخ‌ها";
            }
        }

        // =========================================================
        // TIMELINE
        // =========================================================

        private void DrawTimeline()
        {
            if (TimelineCanvas == null)
                return;

            TimelineCanvas.Children.Clear();

            DateTime timelineDate =
                _selectedDate
                ?? DateTime.Today;

            List<TimelineSystemRow> systemRows =
                GetSystemRowsForTimeline();

            ScrollViewer activeViewer =
     _activeTimelineScrollViewer ?? TimelineScrollViewer;

            double viewportWidth = activeViewer?.ActualWidth ?? 0;
            double viewportHeight = activeViewer?.ActualHeight ?? 0;
            if (viewportWidth > LeftColumnWidth)
            {
                // Use more of the available width on common laptop displays;
                // keep a sensible minimum so hour labels and booking blocks remain readable.
                _hourWidth = Math.Max(48, Math.Min(86,
                    (viewportWidth - LeftColumnWidth - 12) / (EndHour - StartHour)));
            }

            if (viewportHeight > HeaderHeight + 12 && systemRows.Count > 0)
                _rowHeight = Math.Clamp((viewportHeight - HeaderHeight - 10) / systemRows.Count, 36, 56);
            else
                _rowHeight = 52;

            double totalWidth = LeftColumnWidth + ((EndHour - StartHour) * HourWidth);
            double totalHeight = HeaderHeight + (systemRows.Count * RowHeight);

            TimelineCanvas.Width = Math.Max(totalWidth, Math.Max(900, viewportWidth));
            TimelineCanvas.Height = Math.Max(totalHeight, Math.Max(300, viewportHeight));

            DrawBackground(
                totalWidth,
                totalHeight
            );

            DrawHourHeader(
                totalWidth
            );

            DrawSystemRows(
                systemRows,
                totalWidth
            );

            DrawReservations(
                systemRows,
                timelineDate
            );

            DrawCurrentTimeLine(
                timelineDate,
                totalHeight
            );
        }



        private List<TimelineSystemRow> GetSystemRowsForTimeline()
        {
            var rows = new List<TimelineSystemRow>();

            // Normalize Persian and Arabic digits without relying on external helpers.
            string NormalizeSystemNumber(string? value)
            {
                string result = (value ?? string.Empty).Trim();

                result = result
                    .Replace('۰', '0').Replace('۱', '1')
                    .Replace('۲', '2').Replace('۳', '3')
                    .Replace('۴', '4').Replace('۵', '5')
                    .Replace('۶', '6').Replace('۷', '7')
                    .Replace('۸', '8').Replace('۹', '9')
                    .Replace('٠', '0').Replace('١', '1')
                    .Replace('٢', '2').Replace('٣', '3')
                    .Replace('٤', '4').Replace('٥', '5')
                    .Replace('٦', '6').Replace('٧', '7')
                    .Replace('٨', '8').Replace('٩', '9');

                return result
                    .Replace(" ", "")
                    .Replace("-", "")
                    .ToLowerInvariant();
            }

            bool HasSystemRow(int variationId, string number)
            {
                string normalized = NormalizeSystemNumber(number);

                return rows.Any(r =>
                    r.VariationId == variationId &&
                    !r.IsLegacy &&
                    NormalizeSystemNumber(r.SystemNumber) == normalized);
            }

            bool HasLegacyRow(int variationId)
            {
                return rows.Any(r =>
                    r.VariationId == variationId && r.IsLegacy);
            }

            // Add systems from the current catalogue.
            foreach (GamenetVariation variation in _variations)
            {
                if (variation.Id <= 0)
                    continue;

                string variationName = GetVariationName(variation.Id);
                var numbers = variation.SystemNumbers?.ToList() ?? new List<string>();

                if (numbers.Count == 0)
                {
                    int count = Math.Max(variation.Capacity, 1);

                    for (int i = 1; i <= count; i++)
                        numbers.Add(i.ToString(CultureInfo.InvariantCulture));
                }

                foreach (string raw in numbers)
                {
                    string number = (raw ?? string.Empty).Trim();

                    if (number.Length == 0 ||
                        HasSystemRow(variation.Id, number))
                        continue;

                    rows.Add(new TimelineSystemRow
                    {
                        VariationId = variation.Id,
                        SystemNumber = number,
                        Label = $"{variationName} · سیستم {number}"
                    });
                }

                bool hasLegacy = _allReservations.Any(r =>
                    r.VariationId == variation.Id &&
                    (r.Systems == null ||
                     r.Systems.Count == 0 ||
                     r.Systems.Any(x =>
                         x.VariationId == variation.Id &&
                         string.IsNullOrWhiteSpace(x.SystemNumber))));

                if (hasLegacy && !HasLegacyRow(variation.Id))
                {
                    rows.Add(new TimelineSystemRow
                    {
                        VariationId = variation.Id,
                        SystemNumber = string.Empty,
                        IsLegacy = true,
                        Label = $"{variationName} · دستگاه نامشخص"
                    });
                }
            }

            // Preserve older reservations, including systems no longer in the catalogue.
            foreach (ReservationItem reservation in _allReservations)
            {
                int id = reservation.VariationId;

                if (id > 0 &&
                    !_variations.Any(v => v.Id == id) &&
                    !rows.Any(r => r.VariationId == id))
                {
                    rows.Add(new TimelineSystemRow
                    {
                        VariationId = id,
                        SystemNumber = string.Empty,
                        IsLegacy = true,
                        Label = $"سرویس {id} · دستگاه نامشخص"
                    });
                }

                if (reservation.Systems == null)
                    continue;

                foreach (ReservationSystem system in reservation.Systems)
                {
                    if (system.VariationId <= 0)
                        continue;

                    string number = (system.SystemNumber ?? string.Empty).Trim();

                    // Convert legacy numeric system numbers to their official catalogue names.
                    if (number.Length > 0)
                    {
                        var matchingVariation = _variations.FirstOrDefault(v =>
                            v.Id == system.VariationId);

                        if (matchingVariation?.SystemNumbers != null)
                        {
                            string normalizedLegacy = NormalizeSystemNumber(number);
                            string legacyDigits = new string(normalizedLegacy.Where(char.IsDigit).ToArray());

                            // Match either the exact canonical identifier (Cas-5) or its legacy
                            // numeric alias (5). Never use a loose EndsWith check on a normalized
                            // value because normalization removes the hyphen itself.
                            string? officialName = matchingVariation.SystemNumbers.FirstOrDefault(name =>
                            {
                                string normalizedOfficial = NormalizeSystemNumber(name);
                                string officialDigits = new string(normalizedOfficial.Where(char.IsDigit).ToArray());
                                return normalizedOfficial == normalizedLegacy ||
                                       (legacyDigits.Length > 0 && officialDigits == legacyDigits);
                            });

                            if (!string.IsNullOrWhiteSpace(officialName))
                                number = officialName.Trim();
                        }
                    }

                    if (number.Length == 0)
                    {
                        if (!HasLegacyRow(system.VariationId))
                        {
                            rows.Add(new TimelineSystemRow
                            {
                                VariationId = system.VariationId,
                                SystemNumber = string.Empty,
                                IsLegacy = true,
                                Label = $"{GetVariationName(system.VariationId)} · دستگاه نامشخص"
                            });
                        }

                        continue;
                    }

                    if (HasSystemRow(system.VariationId, number))
                        continue;

                    rows.Add(new TimelineSystemRow
                    {
                        VariationId = system.VariationId,
                        SystemNumber = number,
                        Label = $"{GetVariationName(system.VariationId)} · سیستم {number}"
                    });
                }
            }
            foreach (var row in rows)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"TIMELINE DEBUG | VariationId={row.VariationId} | Number=[{row.SystemNumber}] | Legacy={row.IsLegacy} | Label=[{row.Label}]");
            }
            return rows;
        }



        private void DrawBackground(
            double totalWidth,
            double totalHeight)
        {
            var background = new Rectangle
            {
                Width = totalWidth,
                Height = totalHeight,
                Fill = new SolidColorBrush(
                    Color.FromRgb(17, 17, 22) // #111116
                )
            };

            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);

            TimelineCanvas.Children.Add(background);
        }

        private void DrawHourHeader(
            double totalWidth)
        {
            var header = new Border
            {
                Width = totalWidth,
                Height = HeaderHeight,

                Background = new SolidColorBrush(
                    Color.FromRgb(27, 27, 36) // #1B1B24
                ),

                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(55, 55, 68) // خطوط مرزی
                ),

                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            Canvas.SetLeft(header, 0);
            Canvas.SetTop(header, 0);

            TimelineCanvas.Children.Add(header);

            var systemsTitle = new TextBlock
            {
                Text = "سیستم‌ها",
                Width = LeftColumnWidth,
                Height = HeaderHeight,

                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,

                Foreground = new SolidColorBrush(
                    Color.FromRgb(244, 244, 248) // #F4F4F8
                ),

                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas.SetLeft(systemsTitle, 0);
            Canvas.SetTop(systemsTitle, 0);

            TimelineCanvas.Children.Add(systemsTitle);

            for (int hour = StartHour; hour < EndHour; hour++)
            {
                double x =
                    LeftColumnWidth +
                    ((hour - StartHour) * HourWidth);

                var line = new Rectangle
                {
                    Width = 1,
                    Height = 5000,

                    Fill = new SolidColorBrush(
                        Color.FromRgb(55, 55, 68)
                    )
                };

                Canvas.SetLeft(line, x);
                Canvas.SetTop(line, 0);

                TimelineCanvas.Children.Add(line);

                var hourText = new TextBlock
                {
                    Text = $"{hour:00}:00",
                    Width = HourWidth,
                    Height = HeaderHeight,

                    FontFamily = FindResource("Peyda") as FontFamily,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,

                    Foreground = new SolidColorBrush(
                        Color.FromRgb(244, 244, 248)
                    ),

                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                Canvas.SetLeft(hourText, x);
                Canvas.SetTop(hourText, 0);

                TimelineCanvas.Children.Add(hourText);
            }
        }

        private void DrawSystemRows(
            List<TimelineSystemRow> systemRows,
            double totalWidth)
        {
            for (int index = 0; index < systemRows.Count; index++)
            {
                double y =
                    HeaderHeight +
                    (index * RowHeight);

                // پس‌زمینه ردیف‌ها؛ دو رنگ نزدیک برای تفکیک بهتر
                var rowBackground = new Rectangle
                {
                    Width = totalWidth,
                    Height = RowHeight,

                    Fill = new SolidColorBrush(
                        index % 2 == 0
                            ? Color.FromRgb(27, 27, 36)  // #1B1B24
                            : Color.FromRgb(32, 32, 42)  // #20202A
                    )
                };

                Canvas.SetLeft(rowBackground, 0);
                Canvas.SetTop(rowBackground, y);

                TimelineCanvas.Children.Add(rowBackground);

                // خط افقی جداکننده ردیف‌ها
                var rowLine = new Rectangle
                {
                    Width = totalWidth,
                    Height = 1,

                    Fill = new SolidColorBrush(
                        Color.FromRgb(55, 55, 68)
                    )
                };

                Canvas.SetLeft(rowLine, 0);
                Canvas.SetTop(rowLine, y + RowHeight);

                TimelineCanvas.Children.Add(rowLine);

                string systemName = systemRows[index].Label;

                // پس‌زمینه ستون نام سیستم
                var nameBackground = new Border
                {
                    Width = LeftColumnWidth,
                    Height = RowHeight,

                    Background = new SolidColorBrush(
                        Color.FromRgb(27, 27, 36)
                    ),

                    BorderBrush = new SolidColorBrush(
                        Color.FromRgb(55, 55, 68)
                    ),

                    BorderThickness = new Thickness(0, 0, 1, 0)
                };

                Canvas.SetLeft(nameBackground, 0);
                Canvas.SetTop(nameBackground, y);

                TimelineCanvas.Children.Add(nameBackground);

                // نام سیستم
                var nameText = new TextBlock
                {
                    Text = systemName,
                    Width = LeftColumnWidth,
                    Height = RowHeight,

                    FontFamily = FindResource("Peyda") as FontFamily,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,

                    Foreground = new SolidColorBrush(
                        Color.FromRgb(244, 244, 248)
                    ),

                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,

                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                Canvas.SetLeft(nameText, 0);
                Canvas.SetTop(nameText, y);

                TimelineCanvas.Children.Add(nameText);

                // خطوط عمودی ساعات
                for (int hour = StartHour; hour <= EndHour; hour++)
                {
                    double x =
                        LeftColumnWidth +
                        ((hour - StartHour) * HourWidth);

                    var gridLine = new Rectangle
                    {
                        Width = 1,
                        Height = RowHeight,

                        Fill = new SolidColorBrush(
                            Color.FromRgb(48, 48, 60)
                        )
                    };

                    Canvas.SetLeft(gridLine, x);
                    Canvas.SetTop(gridLine, y);

                    TimelineCanvas.Children.Add(gridLine);
                }
            }
        }


        // =========================================================
        // RESERVATIONS
        // =========================================================

        private void DrawReservations(
            List<TimelineSystemRow> systemRows,
            DateTime timelineDate)
        {
            string targetDate =
                timelineDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                );

            foreach (
                ReservationItem reservation
                in _reservations)
            {
                if (
                    !string.Equals(
                        reservation.Date,
                        targetDate,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                // -------------------------------------------------
                // اگر سیستم داخل رزرو موجود نبود،
                // از VariationId اصلی استفاده می‌کنیم.
                // -------------------------------------------------

                if (
                    reservation.Systems == null
                    ||
                    reservation.Systems.Count == 0
                )
                {
                    DrawReservationForSystem(
                        reservation,
                        new ReservationSystem
                        {
                            VariationId =
                                reservation.VariationId,

                            Quantity =
                                reservation.Quantity,

                            Hours =
                                BuildHoursFromReservation(
                                    reservation
                                )
                        },
                        systemRows
                    );

                    continue;
                }

                foreach (
                    ReservationSystem system
                    in reservation.Systems)
                {
                    DrawReservationForSystem(
                        reservation,
                        system,
                        systemRows
                    );
                }
            }
        }

        private void DrawReservationForSystem(
            ReservationItem reservation,
            ReservationSystem system,
            List<TimelineSystemRow> systemRows)
        {
            string systemNumber = (system.SystemNumber ?? string.Empty).Trim();
            string NormalizeRowNumber(string? value)
            {
                string s = (value ?? string.Empty).Trim()
                    .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3')
                    .Replace('۴', '4').Replace('۵', '5').Replace('۶', '6').Replace('۷', '7')
                    .Replace('۸', '8').Replace('۹', '9').Replace('٠', '0').Replace('١', '1')
                    .Replace('٢', '2').Replace('٣', '3').Replace('٤', '4').Replace('٥', '5')
                    .Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
                return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            }
            string digits = new string(NormalizeRowNumber(systemNumber).Where(char.IsDigit).ToArray());
            int rowIndex = systemNumber.Length == 0
                ? systemRows.FindIndex(r => r.VariationId == system.VariationId && r.IsLegacy)
                : systemRows.FindIndex(r => r.VariationId == system.VariationId && !r.IsLegacy &&
                    (NormalizeRowNumber(r.SystemNumber) == NormalizeRowNumber(systemNumber) ||
                     (digits.Length > 0 && new string(NormalizeRowNumber(r.SystemNumber).Where(char.IsDigit).ToArray()) == digits)));
            // Do not place an unmatched historical reservation on an arbitrary first row.
            // GetSystemRowsForTimeline adds a dedicated historical row when no catalogue row exists.
            if (rowIndex < 0 && systemNumber.Length > 0)
                rowIndex = systemRows.FindIndex(r => r.VariationId == system.VariationId && r.IsLegacy);
            if (rowIndex < 0)
                return;

            int startMinute;
            int endMinute;
            if (reservation.DurationMinutes > 0 || !string.IsNullOrWhiteSpace(reservation.EndTime))
            {
                startMinute = ParseTimeMinutes(reservation.StartTime);
                endMinute = startMinute + GetReservationDurationMinutes(reservation);
            }
            else
            {
                List<int> hours = GetReservationHours(reservation, system);
                if (hours.Count == 0) return;
                startMinute = hours.Min() * 60;
                endMinute = (hours.Max() + 1) * 60;
            }

            startMinute = Math.Max(startMinute, StartHour * 60);
            endMinute = Math.Min(endMinute, EndHour * 60);
            if (endMinute <= startMinute) return;

            double left = LeftColumnWidth
                + ((startMinute - StartHour * 60) / 60.0 * HourWidth)
                + 4;

            double top =
                HeaderHeight
                +
                (
                    rowIndex
                    * RowHeight
                )
                + 8;

            double width = Math.Max(4, ((endMinute - startMinute) / 60.0 * HourWidth) - 4);

            DrawReservationBlock(
                reservation,
                system,
                left,
                top,
                width
            );
        }

        private void DrawReservationBlock(
            ReservationItem reservation,
            ReservationSystem system,
            double left,
            double top,
            double width)
        {
            bool isManual =
                IsManualReservation(
                    reservation
                );

            Color backgroundColor;
            Color borderColor;

            if (isManual)
            {
                backgroundColor =
                    Color.FromRgb(
                        255,
                        247,
                        230
                    );

                borderColor =
                    Color.FromRgb(
                        245,
                        166,
                        35
                    );
            }
            else
            {
                backgroundColor =
                    Color.FromRgb(
                        244,
                        235,
                        255
                    );

                borderColor =
                    Color.FromRgb(
                        168,
                        85,
                        247
                    );
            }

            if (reservation.LocalSyncState == "Conflict")
            {
                backgroundColor = Color.FromRgb(255, 235, 235);
                borderColor = Color.FromRgb(220, 70, 70);
            }
            else if (reservation.LocalSyncState is "Pending" or "Sending" or "Retry")
            {
                backgroundColor = Color.FromRgb(255, 247, 230);
                borderColor = Color.FromRgb(220, 160, 55);
            }

            var block =
                new Border
                {
                    Width = width,
                    Height =
                        RowHeight - 8,

                    CornerRadius =
                        new CornerRadius(12),

                    Background =
                        new SolidColorBrush(
                            backgroundColor
                        ),

                    BorderBrush =
                        new SolidColorBrush(
                            borderColor
                        ),

                    BorderThickness =
                        new Thickness(1),

                    Padding =
                        new Thickness(
                            5,
                            2,
                            5,
                            2
                        ),

                    ToolTip =
                        BuildReservationTooltip(
                            reservation,
                            system
                        ),

                    Cursor =
                        Cursors.Hand
                };

            block.MouseLeftButtonUp +=
                (sender, e) =>
                {
                    ShowReservationDetails(
                        reservation,
                        system
                    );

                    e.Handled = true;
                };

            var panel =
                new StackPanel
                {
                    Width = Math.Max(20, width - 10),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

            string customer =
                string.IsNullOrWhiteSpace(
                    reservation.Customer
                )
                    ? "رزرو بدون نام"
                    : reservation.Customer;

            var customerText =
                new TextBlock
                {
                    Text = customer,

                    FontFamily =
                        FindResource("Peyda")
                        as FontFamily,

                    FontSize = 13,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = Math.Max(20, width - 18),
                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                26,
                                26,
                                46
                            )
                        ),

                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                };

            panel.Children.Add(
                customerText
            );

            string time =
                GetReservationTimeText(
                    reservation,
                    system
                );

            var timeText =
                new TextBlock
                {
                    Text = time,

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            0
                        ),

                    FontFamily =
                        FindResource("Peyda")
                        as FontFamily,

                    FontSize = 11,

                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                100,
                                98,
                                110
                            )
                        )
                };

            panel.Children.Add(
                timeText
            );

            string sourceText = isManual ? "رزرو دستی" : "رزرو آنلاین";
            sourceText += reservation.LocalSyncState switch
            {
                "Pending" => " • در انتظار ارسال",
                "Sending" => " • در حال ارسال",
                "Retry" => " • تلاش مجدد",
                "Conflict" => " • تعارض",
                _ => string.Empty
            };

            var sourceTextBlock =
                new TextBlock
                {
                    Text =
                        $"{sourceText} • ×{system.Quantity}",

                    Margin =
                        new Thickness(
                            0,
                            3,
                            0,
                            0
                        ),

                    FontFamily =
                        FindResource("Peyda")
                        as FontFamily,

                    FontSize = 10,

                    Foreground =
                        new SolidColorBrush(
                            borderColor
                        )
                };

            panel.Children.Add(
                sourceTextBlock
            );

            block.Child =
                panel;

            Canvas.SetLeft(
                block,
                left
            );

            Canvas.SetTop(
                block,
                top
            );

            Panel.SetZIndex(
                block,
                100
            );

            TimelineCanvas.Children.Add(
                block
            );
        }

        // =========================================================
        // CURRENT TIME
        // =========================================================

        private void DrawCurrentTimeLine(
            DateTime timelineDate,
            double totalHeight)
        {
            if (
                timelineDate.Date
                !=
                DateTime.Today
            )
            {
                return;
            }

            DateTime now =
                DateTime.Now;

            double currentHour =
                now.Hour
                +
                (
                    now.Minute / 60.0
                )
                +
                (
                    now.Second / 3600.0
                );

            if (
                currentHour < StartHour
                ||
                currentHour >= EndHour
            )
            {
                return;
            }

            double x =
                LeftColumnWidth
                +
                (
                    (currentHour - StartHour)
                    * HourWidth
                );

            var line =
                new Rectangle
                {
                    Width = 2,
                    Height = totalHeight,

                    Fill =
                        new SolidColorBrush(
                            Color.FromRgb(
                                239,
                                68,
                                68
                            )
                        ),

                    Opacity = 0.9
                };

            Canvas.SetLeft(
                line,
                x
            );

            Canvas.SetTop(
                line,
                0
            );

            Panel.SetZIndex(
                line,
                1000
            );

            TimelineCanvas.Children.Add(
                line
            );

            var dot =
                new Ellipse
                {
                    Width = 10,
                    Height = 10,

                    Fill =
                        new SolidColorBrush(
                            Color.FromRgb(
                                239,
                                68,
                                68
                            )
                        )
                };

            Canvas.SetLeft(
                dot,
                x - 4
            );

            Canvas.SetTop(
                dot,
                HeaderHeight - 5
            );

            Panel.SetZIndex(
                dot,
                1001
            );

            TimelineCanvas.Children.Add(
                dot
            );
        }

        // =========================================================
        // HOURS
        // =========================================================

        private List<int>
            GetReservationHours(
                ReservationItem reservation,
                ReservationSystem system)
        {
            var result =
                new List<int>();

            // New minute-aware reservations occupy every hour slot touched by their exact interval.
            if (reservation.DurationMinutes > 0 || !string.IsNullOrWhiteSpace(reservation.EndTime))
            {
                int startMinute = ParseTimeMinutes(reservation.StartTime);
                int endMinute = startMinute + GetReservationDurationMinutes(reservation);
                int firstHour = Math.Max(StartHour, startMinute / 60);
                int lastHourExclusive = Math.Min(EndHour, (endMinute + 59) / 60);
                for (int hour = firstHour; hour < lastHourExclusive; hour++)
                    result.Add(hour);
                return result;
            }

            if (
                system.Hours != null
                &&
                system.Hours.Count > 0
            )
            {
                foreach (
                    string hourText
                    in system.Hours)
                {
                    string normalizedHourText = (hourText ?? string.Empty).Trim();
                    int parsedHour = -1;
                    if (normalizedHourText.Contains(':'))
                    {
                        string[] parts = normalizedHourText.Split(':');
                        if (parts.Length == 2 && parts[1] == "00")
                            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHour);
                    }
                    else
                    {
                        int.TryParse(normalizedHourText, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedHour);
                    }

                    if (parsedHour >= 0)
                    {
                        int hour = parsedHour;
                        if (
                            hour >= StartHour
                            &&
                            hour < EndHour
                        )
                        {
                            result.Add(
                                hour
                            );
                        }
                    }
                }
            }

            if (result.Count > 0)
            {
                return result
                    .Distinct()
                    .OrderBy(
                        hour => hour
                    )
                    .ToList();
            }

            int startHour =
                ParseStartHour(
                    reservation.StartTime
                );

            if (
                startHour < StartHour
                ||
                startHour >= EndHour
            )
            {
                return result;
            }

            int duration =
                reservation.Hours > 0
                    ? reservation.Hours
                    : 1;

            for (
                int i = 0;
                i < duration;
                i++)
            {
                int hour =
                    startHour + i;

                if (
                    hour >= StartHour
                    &&
                    hour < EndHour
                )
                {
                    result.Add(
                        hour
                    );
                }
            }

            return result;
        }

        private static List<string>
            BuildHoursFromReservation(
                ReservationItem reservation)
        {
            var result =
                new List<string>();

            int startMinute = ParseTimeMinutes(reservation.StartTime);
            int endMinute = startMinute + GetReservationDurationMinutes(reservation);
            int firstHour = startMinute / 60;
            int lastHourExclusive = (endMinute + 59) / 60;

            for (int hour = firstHour; hour < lastHourExclusive; hour++)
            {

                if (
                    hour >= StartHour
                    &&
                    hour < EndHour
                )
                {
                    result.Add(
                        hour.ToString(
                            "00",
                            CultureInfo.InvariantCulture
                        )
                    );
                }
            }

            return result;
        }

        private static int
            ParseStartHour(
                string? startTime)
        {
            if (
                string.IsNullOrWhiteSpace(
                    startTime
                )
            )
            {
                return StartHour;
            }

            string value =
                startTime.Trim();

            if (
                TimeSpan.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    out TimeSpan time
                )
            )
            {
                return time.Hours;
            }

            string[] parts =
                value.Split(
                    ':',
                    StringSplitOptions.RemoveEmptyEntries
                );

            if (
                parts.Length > 0
                &&
                int.TryParse(
                    parts[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int hour
                )
            )
            {
                return hour;
            }

            return StartHour;
        }

        private static int ParseTimeMinutes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return StartHour * 60;

            string normalized = value.Trim()
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2')
                .Replace('۳', '3').Replace('۴', '4').Replace('۵', '5')
                .Replace('۶', '6').Replace('۷', '7').Replace('۸', '8')
                .Replace('۹', '9').Replace('٠', '0').Replace('١', '1')
                .Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
                .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7')
                .Replace('٨', '8').Replace('٩', '9');

            if (string.Equals(normalized, "24:00", StringComparison.Ordinal))
                return 24 * 60;

            if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out TimeSpan parsed) &&
                parsed >= TimeSpan.Zero && parsed <= TimeSpan.FromDays(1))
                return (int)parsed.TotalMinutes;

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour))
                return Math.Clamp(hour * 60, 0, 24 * 60);

            return StartHour * 60;
        }

        private static string FormatTimeOfDay(int totalMinutes)
        {
            totalMinutes = Math.Clamp(totalMinutes, 0, 24 * 60);
            return totalMinutes == 24 * 60
                ? "24:00"
                : $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
        }

        private static int GetReservationDurationMinutes(ReservationItem reservation)
        {
            if (reservation.DurationMinutes > 0)
                return reservation.DurationMinutes;

            int start = ParseTimeMinutes(reservation.StartTime);
            int end = ParseTimeMinutes(reservation.EndTime);
            if (end > start)
                return end - start;

            return Math.Max(1, reservation.Hours) * 60;
        }

        private static bool ReservationOverlapsHour(ReservationItem reservation, int hour)
        {
            int start = ParseTimeMinutes(reservation.StartTime);
            int end = start + GetReservationDurationMinutes(reservation);
            int hourStart = hour * 60;
            int hourEnd = hourStart + 60;
            return start < hourEnd && hourStart < end;
        }

        private static string
            GetReservationTimeText(
                ReservationItem reservation,
                ReservationSystem system)
        {
            if (reservation.DurationMinutes <= 0 && string.IsNullOrWhiteSpace(reservation.EndTime) &&
                string.IsNullOrWhiteSpace(reservation.StartTime) && system.Hours != null && system.Hours.Count > 0)
            {
                List<int> legacyHours = system.Hours
                    .Select(value =>
                    {
                        string token = (value ?? string.Empty).Trim();
                        if (token.Contains(':')) token = token.Split(':')[0];
                        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : -1;
                    })
                    .Where(h => h >= 0 && h < 24)
                    .Distinct()
                    .OrderBy(h => h)
                    .ToList();
                if (legacyHours.Count > 0)
                    return $"{legacyHours.First():00}:00 - {legacyHours.Last() + 1:00}:00";
            }

            int start = ParseTimeMinutes(reservation.StartTime);
            int end = Math.Min(24 * 60, start + GetReservationDurationMinutes(reservation));
            return $"{FormatTimeOfDay(start)} - {FormatTimeOfDay(end)}";
        }

        // =========================================================
        // VARIATION
        // =========================================================

        private string GetVariationName(
            int variationId)
        {
            GamenetVariation? variation =
                _variations.FirstOrDefault(
                    item =>
                        item.Id == variationId
                );

            if (variation != null)
            {
                if (
                    !string.IsNullOrWhiteSpace(
                        variation.Title
                    )
                )
                {
                    return variation.Title;
                }

                if (
                    !string.IsNullOrWhiteSpace(
                        variation.Name
                    )
                )
                {
                    return variation.Name;
                }
            }

            return
                $"سیستم {variationId}";
        }

        // =========================================================
        // TOOLTIP
        // =========================================================

        private static string
            BuildReservationTooltip(
                ReservationItem reservation,
                ReservationSystem system)
        {
            string customer =
                string.IsNullOrWhiteSpace(
                    reservation.Customer
                )
                    ? "بدون نام"
                    : reservation.Customer;

            string phone =
                string.IsNullOrWhiteSpace(
                    reservation.Phone
                )
                    ? "-"
                    : reservation.Phone;

            string status =
                string.IsNullOrWhiteSpace(
                    reservation.Status
                )
                    ? "-"
                    : reservation.Status;

            string source =
                IsManualReservation(
                    reservation
                )
                    ? "دستی"
                    : "آنلاین";

            return
                $"مشتری: {customer}\n" +
                $"موبایل: {phone}\n" +
                $"رزرو: {reservation.Id}\n" +
                $"سیستم: {system.VariationId}" +
                (string.IsNullOrWhiteSpace(system.SystemNumber) ? string.Empty : $" / دستگاه {system.SystemNumber}") + "\n" +
                (string.IsNullOrWhiteSpace(system.GameName) ? string.Empty : $"بازی: {system.GameName}\n") +
                $"تعداد: {system.Quantity}\n" +
                $"وضعیت: {status}\n" +
                (reservation.LocalSyncState == "Pending" ? "همگام‌سازی: در انتظار ارسال\n" :
                 reservation.LocalSyncState == "Conflict" ? "همگام‌سازی: دارای تعارض\n" :
                 reservation.LocalSyncState == "Retry" ? "همگام‌سازی: تلاش مجدد\n" :
                 reservation.LocalSyncState == "Sending" ? "همگام‌سازی: در حال ارسال\n" :
                 reservation.LocalSyncState == "Synced" ? "همگام‌سازی: تأییدشده\n" : string.Empty) +
                $"ساعت: {GetReservationTimeText(reservation, system)}\n" +
                $"مبلغ: {reservation.Amount:N0} تومان\n" +
                $"نوع: {source}";
        }

        // =========================================================
        // SEARCH
        // =========================================================

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _searchText = SearchTextBox?.Text ?? string.Empty;
            ApplyFilters();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox != null)
            {
                SearchTextBox.Clear();
                SearchTextBox.Focus();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool isEmpty = string.IsNullOrEmpty(SearchTextBox?.Text);
            if (SearchPlaceholderTextBlock != null)
                SearchPlaceholderTextBlock.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (ClearSearchButton != null)
                ClearSearchButton.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

            _searchText = SearchTextBox?.Text ?? string.Empty;
            if (IsLoaded && !_isLoading)
                ApplyFilters();
        }

        private void TimelineScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // The canvas fills the available viewport when there are few systems;
            // wider schedules remain horizontally scrollable when necessary.
            DrawTimeline();
        }

        // =========================================================
        // DATE QUICK FILTERS
        // =========================================================

        private async void TodayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _selectedDate =
                DateTime.Today;

            await LoadReservationsAsync();
        }

        private async void TomorrowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _selectedDate =
                DateTime.Today.AddDays(1);

            await LoadReservationsAsync();
        }

        private async void DayAfterTomorrowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _selectedDate =
                DateTime.Today.AddDays(2);

            await LoadReservationsAsync();
        }

        private async void AllDatesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _selectedDate = null;

            await LoadReservationsAsync();
        }

        // =========================================================
        // PREVIOUS / NEXT DATE
        // =========================================================

        private async void PreviousDayButton_Click(
            object? sender,
            RoutedEventArgs e)
        {
            DateTime date =
                _selectedDate
                ?? DateTime.Today;

            DateTime previous =
                date.AddDays(-1);

            if (
                previous.Date
                <
                DateTime.Today
            )
            {
                previous =
                    DateTime.Today;
            }

            _selectedDate =
                previous;

            await LoadReservationsAsync();
        }

        private async void NextDayButton_Click(
            object? sender,
            RoutedEventArgs e)
        {
            DateTime date =
                _selectedDate
                ?? DateTime.Today;

            _selectedDate =
                date.AddDays(1);

            await LoadReservationsAsync();
        }

        // =========================================================
        // DATE PICKER
        // =========================================================


        private void DatePickerButton_Click(object? sender, RoutedEventArgs e)
        {
            var picker = new JalaliDatePickerWindow(
                _selectedDate ?? DateTime.Today,
                DateTime.Today)
            {
                Owner = Window.GetWindow(this)
            };

            bool? result = picker.ShowDialog();

            if (result == true && picker.SelectedDate.HasValue)
            {
                _selectedDate = picker.SelectedDate.Value.Date;
                _ = LoadReservationsAsync();
            }
        }


        // =========================================================
        // STATUS FILTERS
        // =========================================================

        // =========================================================
        // STATUS FILTER BUTTONS
        // =========================================================

        private void AllStatusButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedStatus = "all";
            ApplyFilters();
        }

        private void PendingStatusButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedStatus = "pending";
            ApplyFilters();
        }

        private void ConfirmedStatusButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedStatus = "confirmed";
            ApplyFilters();
        }

        private void CompletedStatusButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedStatus = "completed";
            ApplyFilters();
        }
        private void RejectedStatusButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            _selectedStatus = "rejected";
            ApplyFilters();
        }
        private void CancelledStatusButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedStatus = "cancelled";
            ApplyFilters();
        }


        // =========================================================
        // STATUS NORMALIZATION
        // =========================================================

        private static string NormalizeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "all";

            string value = status.Trim().ToLowerInvariant();

            switch (value)
            {
                case "pending":
                case "on-hold":
                case "در انتظار":
                case "درانتظار":
                    return "pending";

                case "processing":
                case "confirmed":
                case "تأییدشده":
                case "تاییدشده":
                case "تایید شده":
                case "تأیید شده":
                case "تایید":
                case "تأیید":
                    return "confirmed";

                case "rejected":
                case "declined":
                case "رد شده":
                case "ردشده":
                case "رد":
                    return "rejected";

                case "completed":
                case "complete":
                case "تکمیل‌شده":
                case "تکمیل شده":
                case "تکمیلشده":
                    return "completed";

                case "cancelled":
                case "canceled":
                case "cancel":
                case "لغوشده":
                case "لغو شده":
                case "لغو‌شده":
                    return "cancelled";

                case "manual":
                case "دستی":
                    return "manual";

                case "all":
                case "همه":
                    return "all";

                default:
                    return value;
            }
        }

        // =========================================================
        // REFRESH RESERVATIONS
        // =========================================================

        private async void RefreshReservationsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            await LoadReservationsAsync();
        }


        // =========================================================
        // FLOOR PLAN LAYOUT SAVED EVENT
        // =========================================================

        private void FloorPlanView_LayoutSaved(int gamenetId)
        {
            // فقط رویداد مربوط به گیمنت فعلی پردازش شود
            if (_gamenet == null || gamenetId != _gamenet.Id)
                return;

            _ = Dispatcher.InvokeAsync(async () =>
            {
                // منتظر پایان بارگذاری اولیه نقشه می‌مانیم
                for (int attempt = 0;
                     _floorMapLoading && attempt < 300;
                     attempt++)
                {
                    await Task.Delay(100);
                }

                // بارگذاری مجدد چیدمان
                _floorMapLoaded = false;

                await LoadFloorMapAsync(force: true);
            });
        }
        // =========================================================
        // LIVE FLOOR MAP — server layout + local/server reservations
        // =========================================================

        private async Task RefreshDeviceAvailabilityAsync()
        {
            if (_availabilityRefreshing) return;
            DateTime date = (_selectedDate ?? DateTime.Today).Date;
            bool dateChanged = _serverAvailabilityDate != date;
            if (dateChanged)
            {
                _serverAvailabilityStates.Clear();
                _serverAvailabilityDate = date;
                _lastAvailabilityAttempt = DateTime.MinValue;
            }
            if (!dateChanged && DateTime.Now - _lastAvailabilityAttempt < TimeSpan.FromSeconds(18)) return;
            _lastAvailabilityAttempt = DateTime.Now;
            _availabilityRefreshing = true;
            try
            {
                // Query from the current minute, not the top of the hour. A query at
                // 15:00 can still report a 15:00–16:00 booking as reserved after it
                // actually ended at 15:30, keeping the tile falsely occupied.
                string availabilityStart = date == DateTime.Today
                    ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : "12:00";
                string dateIso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                using JsonDocument doc = await _extendedApiService.GetDeviceAvailabilityAsync(
                    _gamenet.Id, dateIso, availabilityStart, 1);
                JsonElement root = doc.RootElement;
                JsonElement data = GetJsonObject(root, "data");
                if (data.ValueKind != JsonValueKind.Object) data = root;
                JsonElement devices = GetJsonArray(data, "devices");
                if (devices.ValueKind == JsonValueKind.Array)
                {
                    _serverAvailabilityStates.Clear();
                    foreach (JsonElement device in devices.EnumerateArray())
                    {
                        string number = ReadJsonString(device, "number", "system_number");
                        string state = ReadJsonString(device, "status");
                        if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(state))
                            _serverAvailabilityStates[NormalizeMapSystemNumber(number)] = state;
                    }
                }
            }
            catch
            {
                // Offline mode uses the reservation snapshot already held in SQLite.
            }
            finally
            {
                _availabilityRefreshing = false;
                RenderLiveFloorMap();
            }
        }

        private string GetServerAvailabilityState(LiveMapObject item)
        {
            string key = NormalizeMapSystemNumber(item.SystemNumber);
            if (key.Length == 0) return string.Empty;
            if (_serverAvailabilityStates.TryGetValue(key, out string? exact)) return exact;
            return _serverAvailabilityStates.FirstOrDefault(pair =>
                MapSystemNumbersMatch(pair.Key, item.SystemNumber)).Value ?? string.Empty;
        }

        private async void RefreshFloorMapButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadFloorMapAsync(force: true);
        }

        private async Task LoadFloorMapAsync(bool force = false)
        {
            if (_floorMapLoading)
            {
                if (!force) return;
                // A forced refresh must not silently disappear while the initial load is running.
                for (int attempt = 0; _floorMapLoading && attempt < 300; attempt++)
                    await Task.Delay(100);
                if (_floorMapLoading)
                    return;
            }
            if (_floorMapLoaded && !force) return;

            _floorMapLoading = true;
            if (RefreshFloorMapButton != null) RefreshFloorMapButton.IsEnabled = false;
            if (FloorMapSummaryText != null) FloorMapSummaryText.Text = "در حال دریافت چیدمان...";
            // Keep registry indexes in method scope so the local-layout fallback in catch
            // uses the same authoritative identities collected before a network failure.
            var physicalById = new Dictionary<string, (string Number, int VariationId, string Category, bool Active)>(StringComparer.OrdinalIgnoreCase);
            var physicalByVariationNumber = new Dictionary<(int VariationId, string Number), (string SystemId, string Number, int VariationId, string Category, bool Active)>();
            var ambiguousPhysicalPairs = new HashSet<(int VariationId, string Number)>();
            try
            {
                // The physical registry returned by the gamenet details API is authoritative.
                // Index it by stable SystemId and by the unambiguous VariationId + Number pair.
                void AddPhysical(JsonElement source)
                {
                    if (source.ValueKind != JsonValueKind.Array) return;
                    foreach (JsonElement item in source.EnumerateArray())
                    {
                        string id = ReadJsonString(item, "system_id", "id", "physical_system_id", "systemId").Trim();
                        string number = NormalizeMapSystemNumber(ReadJsonString(item, "number", "system_number", "pc_number"));
                        int variation = ReadJsonInt(item, "variation_id", "variationId", "variation");
                        string category = ReadJsonString(item, "category", "type");
                        string activeText = ReadJsonString(item, "active");
                        bool active = !activeText.Equals("false", StringComparison.OrdinalIgnoreCase) && activeText != "0";
                        if (id.Length == 0 || number.Length == 0 || variation < 0) continue;
                        var row = (number, variation, category, active);
                        physicalById[id] = row;
                        var key = (variation, number);
                        // Duplicate composite keys are ambiguous; remove them from fallback matching.
                        if (ambiguousPhysicalPairs.Contains(key)) continue;
                        if (physicalByVariationNumber.TryGetValue(key, out var previous) && previous.SystemId != id)
                        {
                            physicalByVariationNumber.Remove(key);
                            ambiguousPhysicalPairs.Add(key);
                        }
                        else if (!physicalByVariationNumber.ContainsKey(key))
                            physicalByVariationNumber[key] = (id, number, variation, category, active);
                    }
                }

                // Always load the current physical registry from the API first.
                try
                {
                    using JsonDocument details = await _apiService.GetGamenetDetailsAsync(_gamenet.Id);
                    JsonElement detailsRoot = details.RootElement;
                    JsonElement detailData = GetJsonObject(detailsRoot, "data");
                    if (detailData.ValueKind != JsonValueKind.Object) detailData = detailsRoot;
                    JsonElement gamenet = GetJsonObject(detailData, "gamenet");
                    if (gamenet.ValueKind == JsonValueKind.Object) detailData = gamenet;
                    JsonElement detailVariations = GetJsonArray(detailData, "variations");
                    if (_variations.Count == 0 && detailVariations.ValueKind == JsonValueKind.Array)
                    {
                        try { _variations = JsonSerializer.Deserialize<List<GamenetVariation>>(detailVariations.GetRawText()) ?? new List<GamenetVariation>(); }
                        catch { }
                    }
                    AddPhysical(GetJsonArray(detailData, "physical_systems"));
                }
                catch { /* Continue with the cached layout only when the API is unavailable. */ }

                string localLayoutPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LobixApp", $"floorplan-layout-{_gamenet.Id}.json");
                bool hasPendingLocalLayout = File.Exists(localLayoutPath + ".pending") && File.Exists(localLayoutPath);
                using JsonDocument layoutDoc = hasPendingLocalLayout
                    ? JsonDocument.Parse(File.ReadAllText(localLayoutPath))
                    : await _extendedApiService.GetLayoutAsync(_gamenet.Id);
                JsonElement layoutRoot = layoutDoc.RootElement;
                JsonElement layoutData = layoutRoot.ValueKind == JsonValueKind.Object ? GetJsonObject(layoutRoot, "data") : default;
                if (layoutData.ValueKind != JsonValueKind.Object) layoutData = layoutRoot;
                // The layout endpoint also returns physical_systems. Use it only as an
                // offline/compatibility supplement, never as a number-only lookup source.
                if (physicalById.Count == 0)
                    AddPhysical(GetJsonArray(layoutData, "physical_systems"));
                JsonElement layout = GetJsonObject(layoutData, "layout");
                if (layout.ValueKind != JsonValueKind.Object) layout = layoutData;
                JsonElement objects = layout.ValueKind == JsonValueKind.Array ? layout : GetJsonArray(layout, "objects");
                if ((objects.ValueKind != JsonValueKind.Array || objects.GetArrayLength() == 0) && TryLoadLocalFloorMap(physicalById, physicalByVariationNumber, ambiguousPhysicalPairs))
                {
                    _floorMapLoaded = _liveMapObjects.Count > 0;
                    _floorMapLoadMessage = "نقشه محلی (در انتظار همگام‌سازی سرور)";
                    PopulateMapFloors();
                    await RefreshDeviceAvailabilityAsync();
                    RenderLiveFloorMap();
                    return;
                }

                _liveMapObjects.Clear();
                if (objects.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in objects.EnumerateArray())
                    {
                        string type = ReadJsonString(item, "type", "Type");
                        string label = ReadJsonString(item, "label", "name", "Name");
                        string systemId = ReadJsonString(item, "system_id", "systemId", "SystemId").Trim();
                        bool physicalFlag = ReadJsonBool(item, "is_physical_system", "IsPhysicalSystem");
                        string number = NormalizeMapSystemNumber(ReadJsonString(item, "system_number", "number", "SystemNumber"));
                        int variation = ReadJsonInt(item, "variation_id", "VariationId", "variationId");
                        string category = ReadJsonString(item, "category", "Category");
                        bool declaredSystemType = type.Equals("system", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("کامپیوتر", StringComparison.OrdinalIgnoreCase) ||
                            type.Equals("کنسول", StringComparison.OrdinalIgnoreCase);
                        // A room/area/decoration is never promoted by digits in its label.
                        // A generic legacy object is promoted only when its stable ID exists
                        // in the API registry, or it has a complete unique variation+number pair.
                        bool isSystem = physicalFlag || declaredSystemType || systemId.Length > 0;
                        bool disabled = ReadJsonString(item, "status", "Status").Equals("خاموش", StringComparison.OrdinalIgnoreCase);
                        if (isSystem)
                        {
                            bool hasPhysical = false;
                            (string Number, int VariationId, string Category, bool Active) sys = default;
                            if (systemId.Length > 0 && physicalById.TryGetValue(systemId, out sys))
                            {
                                hasPhysical = true;
                            }
                            else if (variation > 0 && number.Length > 0 &&
                                     !ambiguousPhysicalPairs.Contains((variation, number)) &&
                                     physicalByVariationNumber.TryGetValue((variation, number), out var composite))
                            {
                                hasPhysical = true;
                                systemId = composite.SystemId;
                                sys = (composite.Number, composite.VariationId, composite.Category, composite.Active);
                            }

                            // A map tile is selectable only when it resolves to one real API device.
                            if (!hasPhysical)
                            {
                                isSystem = false;
                            }
                            else
                            {
                                number = sys.Number;
                                variation = sys.VariationId;
                                if (string.IsNullOrWhiteSpace(category)) category = sys.Category;
                                disabled |= !sys.Active;
                                // Keep the actual display name supplied by the saved floor plan
                                // (e.g. Cas-6 / VIP-16); never synthesize a PC-01 label here.
                            }
                        }
                        string objectId = ReadJsonString(item, "id", "Id");
                        if (string.IsNullOrWhiteSpace(objectId))
                        {
                            objectId = isSystem
                                ? (!string.IsNullOrWhiteSpace(systemId)
                                    ? "physical:" + systemId
                                    : "physical:" + variation.ToString(CultureInfo.InvariantCulture) + ":" + NormalizeMapSystemNumber(number))
                                : "object:" + ReadJsonDouble(item, "x", "X").ToString("0.##", CultureInfo.InvariantCulture) + ":" + ReadJsonDouble(item, "y", "Y").ToString("0.##", CultureInfo.InvariantCulture) + ":" + label;
                        }
                        _liveMapObjects.Add(new LiveMapObject
                        {
                            Id = objectId,
                            Type = isSystem ? "system" : type,
                            Label = string.IsNullOrWhiteSpace(label) ? type : label,
                            SystemId = systemId,
                            SystemNumber = number,
                            VariationId = variation,
                            Category = category,
                            Floor = ReadJsonString(item, "floor", "Floor") is string floor && floor.Length > 0 ? floor : "طبقه اصلی",
                            Disabled = disabled,
                            X = ReadJsonDouble(item, "x", "X"),
                            Y = ReadJsonDouble(item, "y", "Y"),
                            Width = Math.Clamp(ReadJsonDouble(item, "width", "w", "W", fallback: 90), 24, 1600),
                            Height = Math.Clamp(ReadJsonDouble(item, "height", "h", "H", fallback: 58), 18, 1000),
                            Rotation = ReadJsonDouble(item, "rotation", "Rotation")
                        });
                    }
                }

                _floorMapLoaded = _liveMapObjects.Count > 0;
                PopulateMapFloors();
                _floorMapLoadMessage = _floorMapLoaded
                    ? $"{_liveMapObjects.Count(x => x.Type == "system")} سیستم روی نقشه"
                    : "نقشه‌ای در سرور ذخیره نشده؛ ابتدا چیدمان سالن را ذخیره کنید.";
                await RefreshDeviceAvailabilityAsync();
                RenderLiveFloorMap();
            }
            catch (Exception ex)
            {
                if (TryLoadLocalFloorMap(physicalById, physicalByVariationNumber, ambiguousPhysicalPairs))
                {
                    _floorMapLoaded = _liveMapObjects.Count > 0;
                    _floorMapLoadMessage = "نقشه محلی • سرور فعلاً در دسترس نیست";
                    PopulateMapFloors();
                }
                else
                {
                    _floorMapLoadMessage = "نقشه در دسترس نیست: " + ex.Message;
                }
                if (FloorMapSummaryText != null) FloorMapSummaryText.Text = _floorMapLoadMessage;
                RenderLiveFloorMap();
            }
            finally
            {
                _floorMapLoading = false;
                if (RefreshFloorMapButton != null) RefreshFloorMapButton.IsEnabled = true;
            }
        }

        private bool TryLoadLocalFloorMap(
            Dictionary<string, (string Number, int VariationId, string Category, bool Active)> physicalById,
            Dictionary<(int VariationId, string Number), (string SystemId, string Number, int VariationId, string Category, bool Active)> physicalByVariationNumber,
            HashSet<(int VariationId, string Number)> ambiguousPhysicalPairs)
        {
            _liveMapObjects.Clear();
            string localPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LobixApp", $"floorplan-layout-{_gamenet.Id}.json");
            string legacyPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LobixApp", "floorplan-layout.json");
            string path = File.Exists(localPath) ? localPath : legacyPath;
            if (!File.Exists(path)) return false;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    string type = ReadJsonString(item, "Type", "type");
                    string label = ReadJsonString(item, "Name", "name", "label");
                    string systemId = ReadJsonString(item, "SystemId", "system_id").Trim();
                    string number = NormalizeMapSystemNumber(ReadJsonString(item, "SystemNumber", "system_number", "number"));
                    int variationId = ReadJsonInt(item, "VariationId", "variation_id");
                    bool declaredSystem = ReadJsonBool(item, "IsPhysicalSystem", "is_physical_system") || systemId.Length > 0;
                    bool isSystem = false;
                    if (declaredSystem && systemId.Length > 0 && physicalById.TryGetValue(systemId, out var physicalByStableId))
                    {
                        number = physicalByStableId.Number;
                        variationId = physicalByStableId.VariationId;
                        isSystem = true;
                    }
                    else if (declaredSystem && variationId >= 0 && number.Length > 0 &&
                             !ambiguousPhysicalPairs.Contains((variationId, number)) &&
                             physicalByVariationNumber.TryGetValue((variationId, number), out var physicalByPair))
                    {
                        systemId = physicalByPair.SystemId;
                        number = physicalByPair.Number;
                        variationId = physicalByPair.VariationId;
                        isSystem = true;
                    }
                    _liveMapObjects.Add(new LiveMapObject
                    {
                        Id = ReadJsonString(item, "Id", "id") is string localId && !string.IsNullOrWhiteSpace(localId)
                            ? localId
                            : (isSystem
                                ? "physical:" + systemId
                                : "object:" + ReadJsonDouble(item, "x", "X").ToString("0.##", CultureInfo.InvariantCulture) + ":" + ReadJsonDouble(item, "y", "Y").ToString("0.##", CultureInfo.InvariantCulture) + ":" + label),
                        Type = isSystem ? "system" : type,
                        Label = label,
                        SystemId = systemId,
                        SystemNumber = number,
                        VariationId = variationId,
                        Category = ReadJsonString(item, "Category", "category"),
                        Floor = string.IsNullOrWhiteSpace(ReadJsonString(item, "Floor", "floor")) ? "طبقه اصلی" : ReadJsonString(item, "Floor", "floor"),
                        Disabled = ReadJsonString(item, "Status", "status").Equals("خاموش", StringComparison.OrdinalIgnoreCase),
                        X = ReadJsonDouble(item, "X", "x"),
                        Y = ReadJsonDouble(item, "Y", "y"),
                        Width = Math.Clamp(ReadJsonDouble(item, "W", "width", fallback: 90), 24, 1600),
                        Height = Math.Clamp(ReadJsonDouble(item, "H", "height", fallback: 58), 18, 1000)
                    });
                }
                return _liveMapObjects.Count > 0;
            }
            catch
            {
                _liveMapObjects.Clear();
                return false;
            }
        }

        private void PopulateMapFloors()
        {
            if (FloorMapFloorCombo == null) return;
            _suppressMapFloorChange = true;
            try
            {
                var floors = _liveMapObjects.Select(x => string.IsNullOrWhiteSpace(x.Floor) ? "طبقه اصلی" : x.Floor)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                if (floors.Count == 0) floors.Add("طبقه اصلی");
                FloorMapFloorCombo.ItemsSource = floors;
                if (!floors.Contains(_selectedMapFloor, StringComparer.OrdinalIgnoreCase)) _selectedMapFloor = floors[0];
                FloorMapFloorCombo.SelectedItem = floors.First(x => string.Equals(x, _selectedMapFloor, StringComparison.OrdinalIgnoreCase));
            }
            finally { _suppressMapFloorChange = false; }
        }

        private void FullscreenFloorMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (LiveFloorMapCanvas == null || LiveFloorMapCanvas.Parent is not Viewbox originalViewbox) return;
            var placeholder = new Canvas { Width = 1600, Height = 1000, Background = new SolidColorBrush(Color.FromRgb(16, 17, 23)) };
            originalViewbox.Child = placeholder;
            var fullscreenViewbox = new Viewbox { Stretch = Stretch.Uniform, Child = LiveFloorMapCanvas, Margin = new Thickness(18) };
            var window = new Window
            {
                Title = "نقشه زنده سالن — لابیکس",
                Width = Math.Min(1500, SystemParameters.WorkArea.Width - 40),
                Height = Math.Min(860, SystemParameters.WorkArea.Height - 40),
                MinWidth = 900,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(Color.FromRgb(13, 14, 19)),
                Content = fullscreenViewbox
            };
            window.Closed += (_, _) =>
            {
                fullscreenViewbox.Child = null;
                originalViewbox.Child = LiveFloorMapCanvas;
                RenderLiveFloorMap();
            };
            window.ShowDialog();
        }

        private void FloorMapFloorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressMapFloorChange || FloorMapFloorCombo?.SelectedItem is not string floor) return;
            _selectedMapFloor = floor;
            RenderLiveFloorMap();
        }

        private void RenderLiveFloorMap()
        {
            if (LiveFloorMapCanvas == null) return;
            // تایمر نقشه زنده؛ فقط یک بار ساخته و فعال می‌شود.
            if (_liveMapClock == null)
            {
                _liveMapClock = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };

                _liveMapClock.Tick += (_, _) =>
                {
                    if (LiveFloorMapCanvas != null && _floorMapLoaded)
                        RenderLiveFloorMap();
                };
            }

            if (!_liveMapClock.IsEnabled)
                _liveMapClock.Start();
            LiveFloorMapCanvas.Children.Clear();
            // Match the floor-plan editor's native 1600x1000 coordinate space.
            // Never compress Y coordinates: the editor stores real canvas positions.
            for (int x = 0; x <= 1600; x += 50)
                LiveFloorMapCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = 1000, Stroke = new SolidColorBrush(Color.FromRgb(28, 30, 39)), StrokeThickness = 1, IsHitTestVisible = false });
            for (int y = 0; y <= 1000; y += 50)
                LiveFloorMapCanvas.Children.Add(new Line { X1 = 0, X2 = 1600, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(28, 30, 39)), StrokeThickness = 1, IsHitTestVisible = false });

            if (!_floorMapLoaded)
            {
                if (FloorMapSummaryText != null) FloorMapSummaryText.Text = _floorMapLoadMessage;
                var hint = new TextBlock { Text = _floorMapLoadMessage, Foreground = Brushes.LightGray, FontSize = 24, FontFamily = FindResource("Peyda") as FontFamily, TextAlignment = TextAlignment.Center, Width = 1300, TextWrapping = TextWrapping.Wrap };
                Canvas.SetLeft(hint, 150); Canvas.SetTop(hint, 430); LiveFloorMapCanvas.Children.Add(hint);
                return;
            }

            int free = 0, reserved = 0, disabled = 0;
            foreach (LiveMapObject item in _liveMapObjects
    .Where(x => string.Equals(
        x.Floor,
        _selectedMapFloor,
        StringComparison.OrdinalIgnoreCase))
    .OrderBy(x => string.Equals(
        x.Type,
        "system",
        StringComparison.OrdinalIgnoreCase) ? 1 : 0))
            {
                if (item.Type != "system")
                {
                    DrawFloorDecoration(item);
                    continue;
                }
                ReservationItem? booking = FindMapReservation(item);
                string serverState = GetServerAvailabilityState(item);
                bool hasLocalSchedule = HasMapReservationForSystemOnDate(item);
                // The API availability endpoint may return a day-level/next-slot state.
                // Once we have local reservations for this system/date, those exact
                // intervals are authoritative; never turn one booking into all-day occupancy.
                bool serverSaysOccupied = !hasLocalSchedule &&
                    (serverState.Equals("reserved", StringComparison.OrdinalIgnoreCase) ||
                     serverState.Equals("in_use", StringComparison.OrdinalIgnoreCase));
                bool isDisabled = item.Disabled || serverState.Equals("off", StringComparison.OrdinalIgnoreCase);
                bool isReserved = booking != null || serverSaysOccupied;
                bool isMapSelected = _selectedLiveMapSystems.Contains(GetLiveMapSelectionKey(item));
                if (isDisabled) disabled++;
                else if (isReserved) reserved++;
                else free++;

                Color reservationColor = GetReservationMapColor(booking, item.SystemNumber);
                Color fillColor = isDisabled ? Color.FromRgb(75, 43, 48) : isReserved ? reservationColor : Color.FromRgb(29, 89, 65);
                Color borderColor = isDisabled ? Color.FromRgb(212, 91, 91) : isMapSelected ? Color.FromRgb(115, 174, 255) : isReserved ? reservationColor : Color.FromRgb(61, 188, 130);
                if (isMapSelected && !isDisabled) fillColor = Color.FromRgb(39, 68, 112);

                var button = new Button
                {
                    Width = item.Width,
                    Height = Math.Max(28, item.Height),
                    Background = new SolidColorBrush(fillColor),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(borderColor),
                    BorderThickness = new Thickness(isMapSelected ? 4 : 2),
                    Padding = new Thickness(3),
                    Cursor = Cursors.Hand,
                    FontFamily = FindResource("Peyda") as FontFamily,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Content = BuildMapSystemContent(item, booking, isDisabled, serverState),
                    ToolTip = BuildMapSystemTooltip(item, booking, isDisabled, serverState),
                    Tag = item
                };
                button.Click += LiveMapSystem_Click;
                button.RenderTransformOrigin = new Point(.5, .5);
                button.RenderTransform = new RotateTransform(item.Rotation);
                Canvas.SetLeft(button, item.X); Canvas.SetTop(button, item.Y);
                LiveFloorMapCanvas.Children.Add(button);
            }
            if (FloorMapSummaryText != null)
                FloorMapSummaryText.Text = $"{_selectedMapFloor}  •  آزاد {free}  •  رزرو {reserved}  •  غیرفعال {disabled}";
        }

        private UIElement BuildMapSystemContent(LiveMapObject item, ReservationItem? booking, bool disabled, string serverState)
        {
            double side = Math.Min(item.Width, item.Height);
            bool compact = side < 76;
            double nameSize = compact ? 14 : Math.Clamp(side * 0.19, 15, 20);
            double metaSize = compact ? 12 : Math.Clamp(side * 0.14, 13, 16);
            double timerSize = compact ? 12 : Math.Clamp(side * 0.15, 13, 17);
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 2, 3, 2)
            };

            string title = !string.IsNullOrWhiteSpace(item.Label)
                ? item.Label
                : (!string.IsNullOrWhiteSpace(item.SystemNumber) ? $"PC {item.SystemNumber}" : "سیستم");
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = FindResource("Peyda") as FontFamily,
                FontWeight = FontWeights.SemiBold,
                FontSize = nameSize,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = compact ? 34 : 44,
                Margin = new Thickness(1, 0, 1, 1)
            });

            if (!string.IsNullOrWhiteSpace(item.SystemNumber))
                panel.Children.Add(new TextBlock
                {
                    Text = $"شماره {item.SystemNumber}",
                    FontFamily = FindResource("Peyda") as FontFamily,
                    FontWeight = FontWeights.Medium,
                    FontSize = metaSize,
                    Foreground = new SolidColorBrush(Color.FromRgb(235, 239, 248)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(1, 0, 1, 1)
                });

            string statusText;
            if (disabled) statusText = "غیرفعال";
            else if (booking != null) statusText = IsManualReservation(booking) ? "رزرو دستی" : "رزرو آنلاین";
            else if (serverState.Equals("reserved", StringComparison.OrdinalIgnoreCase) || serverState.Equals("in_use", StringComparison.OrdinalIgnoreCase)) statusText = "رزرو";
            else statusText = "آزاد";

            panel.Children.Add(new TextBlock
            {
                Text = statusText,
                FontFamily = FindResource("Peyda") as FontFamily,
                FontWeight = FontWeights.SemiBold,
                FontSize = metaSize,
                Foreground = disabled ? new SolidColorBrush(Color.FromRgb(255, 220, 220)) : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(1, 1, 1, 0)
            });

            if (!disabled && booking != null)
            {
                string countdown = GetBookingCountdown(booking);
                if (!string.IsNullOrWhiteSpace(countdown))
                    panel.Children.Add(new TextBlock
                    {
                        Text = countdown.Replace("\n", "  "),
                        FontFamily = FindResource("Peyda") as FontFamily,
                        FontSize = timerSize,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(1, 2, 1, 0),
                        Padding = new Thickness(2, 1, 2, 1),
                        MaxWidth = Math.Max(30, item.Width - 8)
                    });
            }
            return panel;
        }

        private string BuildMapSystemTooltip(LiveMapObject item, ReservationItem? booking, bool disabled, string serverState)
        {
            if (disabled) return $"سیستم {item.SystemNumber} • غیرفعال";
            if (booking == null && (serverState == "reserved" || serverState == "in_use")) return $"سیستم {item.SystemNumber} • {serverState}\nجزئیات رزرو در فهرست محلی در دسترس نیست؛ ابتدا بروزرسانی کنید.";
            if (booking == null) return $"سیستم {item.SystemNumber} • آزاد\nبرای ثبت رزرو دستی کلیک کنید.";
            int duration = GetReservationDurationMinutes(booking);
            string durationText = duration >= 60
                ? $"{duration / 60} ساعت و {duration % 60} دقیقه"
                : $"{duration} دقیقه";
            return $"{booking.Customer}\n{booking.Phone}\n{FormatTimeOfDay(ParseTimeMinutes(booking.StartTime))} تا {FormatTimeOfDay(ParseTimeMinutes(booking.StartTime) + duration)} • {durationText}\n{(IsManualReservation(booking) ? "رزرو دستی" : "رزرو آنلاین")} • {GetFriendlyStatus(booking.Status)} • {booking.LocalSyncState}";
        }

        private void DrawFloorDecoration(LiveMapObject item)
        {
            Color color = item.Type.ToLowerInvariant() switch
            {
                "wall" or "دیوار" =>
                    Color.FromRgb(112, 116, 128),

                "room" or "ناحیه" or "section" =>
                    Color.FromArgb(80, 73, 82, 105),

                "entrance" or "ورودی" =>
                    Color.FromRgb(34, 91, 74),

                "desk" or "میز" =>
                    Color.FromRgb(55, 59, 72),

                _ =>
                    Color.FromRgb(48, 51, 63)
            };

            var border = new Border
            {
                Width = item.Width,
                Height = Math.Max(24, item.Height),

                Background = new SolidColorBrush(color),

                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(89, 93, 108)
                ),

                BorderThickness = new Thickness(1),

                CornerRadius = new CornerRadius(5),

                // اجازه نده تزئینات کلیک را دریافت کنند.
                IsHitTestVisible = false,

                Child = new TextBlock
                {
                    Text = item.Label,

                    Foreground = Brushes.Gainsboro,

                    FontFamily = FindResource("Peyda") as FontFamily,

                    FontSize = 18,

                    TextAlignment = TextAlignment.Center,

                    VerticalAlignment = VerticalAlignment.Center,

                    TextWrapping = TextWrapping.Wrap
                }
            };

            border.RenderTransformOrigin = new Point(0.5, 0.5);

            border.RenderTransform = new RotateTransform(item.Rotation);

            Canvas.SetLeft(border, item.X);
            Canvas.SetTop(border, item.Y);

            LiveFloorMapCanvas.Children.Add(border);
        }

        private static string NormalizeMapSystemNumber(string? value)
        {
            string digits = new((value ?? string.Empty).Trim().Select(c => c switch
            {
                >= '۰' and <= '۹' => (char)('0' + c - '۰'),
                >= '٠' and <= '٩' => (char)('0' + c - '٠'),
                _ => c
            }).ToArray());
            return digits.Length > 0 && digits.All(char.IsDigit) ? (digits.TrimStart('0') is { Length: > 0 } n ? n : "0") : digits;
        }


        private bool HasMapReservationForSystemOnDate(LiveMapObject item)
        {
            DateTime date = (_selectedDate ?? DateTime.Today).Date;
            string number = NormalizeMapSystemNumber(item.SystemNumber);

            return _allReservations.Any(r =>
                ParseDate(r.Date).Date == date &&

                // حذف سفارش‌هایی که اپراتور رد کرده است
                !string.Equals(
                    NormalizeStatus(r.OperatorStatus),
                    "rejected",
                    StringComparison.OrdinalIgnoreCase) &&

                // حذف وضعیت‌های غیرمعتبر سفارش
                !new[] { "cancelled", "canceled", "completed", "refunded", "failed" }
                    .Contains(
                        NormalizeStatus(r.Status),
                        StringComparer.OrdinalIgnoreCase) &&

                r.Systems?.Any(s =>
                    MapSystemNumbersMatch(s.SystemNumber, number) &&
                    (s.VariationId <= 0 ||
                     item.VariationId <= 0 ||
                     s.VariationId == item.VariationId)) == true);
        }


        private static bool MapSystemNumbersMatch(string? left, string? right)
        {
            string a = NormalizeMapSystemNumber(left);
            string b = NormalizeMapSystemNumber(right);
            if (a.Length == 0 || b.Length == 0) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            string digitsA = new(a.Where(char.IsDigit).ToArray());
            string digitsB = new(b.Where(char.IsDigit).ToArray());
            return digitsA.Length > 0 && digitsA == digitsB;
        }


        private ReservationItem? FindMapReservation(LiveMapObject item)
        {
            DateTime date = (_selectedDate ?? DateTime.Today).Date;
            DateTime now = DateTime.Now;
            string number = NormalizeMapSystemNumber(item.SystemNumber);

            if (number.Length == 0)
                return null;

            var matches = _allReservations
                .Where(r => ParseDate(r.Date).Date == date)

                // سفارش‌هایی که اپراتور رد کرده نباید رزرو محسوب شوند.
                .Where(r => !string.Equals(
                    NormalizeStatus(r.OperatorStatus),
                    "rejected",
                    StringComparison.OrdinalIgnoreCase))

                // فیلتر وضعیت سفارش
                .Where(r => !new[] {
                "cancelled", "canceled", "completed", "refunded", "failed"
                    }.Contains(
                        NormalizeStatus(r.Status),
                        StringComparer.OrdinalIgnoreCase))

                .Where(r => r.Systems?.Any(s =>
                    MapSystemNumbersMatch(s.SystemNumber, number) &&
                    (s.VariationId <= 0 ||
                     item.VariationId <= 0 ||
                     s.VariationId == item.VariationId)) == true)

                .Select(r =>
                {
                    DateTime start = ParseReservationStart(r.Date, r.StartTime);
                    DateTime end = start.AddMinutes(Math.Max(5, GetReservationDurationMinutes(r)));

                    return new
                    {
                        Reservation = r,
                        Start = start,
                        End = end
                    };
                })
                .ToList();

            if (date == DateTime.Today)
            {
                return matches
                    .Where(x =>
                        x.End > now &&
                        x.Start <= now.AddMinutes(30))
                    .OrderBy(x => x.Start)
                    .Select(x => x.Reservation)
                    .FirstOrDefault();
            }

            if (date > DateTime.Today)
            {
                return matches
                    .Where(x => x.End > now)
                    .OrderBy(x => x.Start)
                    .Select(x => x.Reservation)
                    .FirstOrDefault();
            }

            return null;
        }

        private static Color GetReservationMapColor(
       ReservationItem? booking,
       string? systemNumber = null)
        {
            string colorKey;

            if (booking == null)
            {
                // سیستم بدون رزرو
                colorKey = "system:" +
                           NormalizeMapSystemNumber(systemNumber);
            }
            else if (
                string.Equals(
                    booking.Source,
                    "online",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                booking.OrderId > 0
            )
            {
                // تمام آیتم‌های یک سفارش آنلاین، رنگ یکسان دارند.
                colorKey = $"online-order:{booking.OrderId}";
            }
            else if (!string.IsNullOrWhiteSpace(booking.Id))
            {
                // رزروهای دستی و سایر رزروها
                colorKey = booking.Id;
            }
            else
            {
                // شناسه جایگزین در صورت نبود شناسه اصلی
                colorKey =
                    $"{booking.OrderId}:" +
                    $"{booking.Customer}:" +
                    $"{booking.Date}:" +
                    $"{booking.StartTime}";
            }

            unchecked
            {
                uint hash = 2166136261;

                foreach (char c in colorKey)
                {
                    hash ^= c;
                    hash *= 16777619;
                }

                // تولید رنگ از روی کلید رزرو
                double hue = hash % 360;
                double saturation = 0.68;
                double value = 0.82;

                double chroma = value * saturation;

                double x = chroma *
                           (1 - Math.Abs((hue / 60.0) % 2 - 1));

                double m = value - chroma;

                double r, g, b;

                if (hue < 60)
                    (r, g, b) = (chroma, x, 0);
                else if (hue < 120)
                    (r, g, b) = (x, chroma, 0);
                else if (hue < 180)
                    (r, g, b) = (0, chroma, x);
                else if (hue < 240)
                    (r, g, b) = (0, x, chroma);
                else if (hue < 300)
                    (r, g, b) = (x, 0, chroma);
                else
                    (r, g, b) = (chroma, 0, x);

                return Color.FromRgb(
                    (byte)Math.Round((r + m) * 255),
                    (byte)Math.Round((g + m) * 255),
                    (byte)Math.Round((b + m) * 255)
                );
            }
        }

        private static string GetBookingCountdown(ReservationItem? booking)
        {
            if (booking == null)
                return string.Empty;

            DateTime start = ParseReservationStart(
                booking.Date,
                booking.StartTime
            );

            // مدت واقعی رزرو؛ بدون گرد کردن به یک ساعت
            DateTime end = start.AddMinutes(Math.Max(5, GetReservationDurationMinutes(booking)));
            DateTime now = DateTime.Now;

            if (now >= end)
                return string.Empty;

            if (now < start)
            {
                TimeSpan untilStart = start - now;

                if (untilStart > TimeSpan.FromMinutes(30))
                    return string.Empty;

                return "تا شروع\n" + FormatMapCountdown(untilStart);
            }

            return "تا پایان\n" + FormatMapCountdown(end - now);
        }

        private static string FormatMapCountdown(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int totalHours = (int)Math.Floor(span.TotalHours);
            return $"{totalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        private static string GetLiveMapSelectionKey(LiveMapObject item)
        {
            if (!string.IsNullOrWhiteSpace(item.SystemId)) return "physical:" + item.SystemId.Trim();
            string number = NormalizeMapSystemNumber(item.SystemNumber);
            if (number.Length > 0) return $"system:{item.VariationId}:{number}";
            return !string.IsNullOrWhiteSpace(item.Id) ? item.Id : $"object:{item.X:0.##}:{item.Y:0.##}:{item.Label}";
        }


        private async void LiveMapMultiSelectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // -------------------------------------------------
            // START MULTI SELECT
            // -------------------------------------------------

            if (!_liveMapMultiSelectMode)
            {
                _selectedLiveMapSystems.Clear();
                _selectedLiveMapObjects.Clear();

                _liveMapMultiSelectMode = true;

                if (LiveMapMultiSelectButton != null)
                    LiveMapMultiSelectButton.Content = "پایان انتخاب";

                RenderLiveFloorMap();

                if (FloorMapSummaryText != null)
                {
                    FloorMapSummaryText.Text =
                        "حالت انتخاب گروهی فعال است؛ سیستم‌های آزاد را انتخاب کنید.";
                }

                return;
            }

            // -------------------------------------------------
            // FINISH MULTI SELECT
            // -------------------------------------------------

            if (_selectedLiveMapObjects.Count == 0)
            {
                LobixDialog.Show(
                    "حداقل یک سیستم آزاد را از روی نقشه انتخاب کنید.",
                    "انتخاب سیستم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            // حذف موارد تکراری احتمالی
            List<LiveMapObject> selectedSystems =
                _selectedLiveMapObjects
                    .GroupBy(GetLiveMapSelectionKey)
                    .Select(group => group.First())
                    .ToList();

            if (selectedSystems.Count == 0)
                return;

            // -------------------------------------------------
            // OPEN BOOKING EDITOR
            // -------------------------------------------------

            await ShowBookingEditorAsync(
                null,
                null,
                selectedSystems
            );

            // اگر فرم با موفقیت ثبت شده باشد،
            // متد ShowBookingEditorAsync انتخاب‌ها را پاک می‌کند.
            // اگر کاربر انصراف داده باشد، انتخاب‌ها باقی می‌مانند.

            RenderLiveFloorMap();
        }



        private void LiveMapSystem_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button { Tag: LiveMapObject item })
                return;

            // -------------------------------------------------
            // DISABLED SYSTEM
            // -------------------------------------------------

            if (item.Disabled)
            {
                LobixDialog.Show(
                    "این سیستم در نقشه غیرفعال یا قفل شده است. ابتدا وضعیت آن را در طراحی سالن اصلاح کنید.",
                    "سیستم غیرفعال",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            // -------------------------------------------------
            // VALIDATE SYSTEM
            // -------------------------------------------------

            bool validVariation =
                item.VariationId > 0 ||
                (_variations.Count == 1 && _variations[0].Id == 0);

            if (string.IsNullOrWhiteSpace(item.SystemNumber) ||
                !validVariation)
            {
                LobixDialog.Show(
                    "شماره سیستم یا نوع محصول این آیتم از فهرست سیستم‌های گیمنت قابل تشخیص نیست. فهرست سیستم‌ها و اتصال آن‌ها به نوع محصول را در طراحی سالن بررسی کنید.",
                    "سیستم متصل نیست",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            // -------------------------------------------------
            // MULTI SELECT MODE
            // -------------------------------------------------

            if (_liveMapMultiSelectMode)
            {
                ReservationItem? existingBooking =
                    FindMapReservation(item);

                string serverState =
                    GetServerAvailabilityState(item);

                bool serverOccupied =
                    serverState.Equals(
                        "reserved",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    serverState.Equals(
                        "in_use",
                        StringComparison.OrdinalIgnoreCase
                    );

                bool serverDisabled =
                    serverState.Equals(
                        "off",
                        StringComparison.OrdinalIgnoreCase
                    );

                if (existingBooking != null || serverOccupied)
                {
                    LobixDialog.Show(
                        "برای رزرو گروهی فقط سیستم‌های آزاد را انتخاب کنید.",
                        "سیستم رزروشده",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    return;
                }

                if (serverDisabled)
                {
                    LobixDialog.Show(
                        "این سیستم طبق وضعیت سرور غیرفعال است و امکان انتخاب آن وجود ندارد.",
                        "سیستم غیرفعال",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    return;
                }

                string selectionKey =
                    GetLiveMapSelectionKey(item);

                // -------------------------------------------------
                // REMOVE SELECTED SYSTEM
                // -------------------------------------------------

                if (_selectedLiveMapSystems.Contains(selectionKey))
                {
                    _selectedLiveMapSystems.Remove(selectionKey);

                    _selectedLiveMapObjects.RemoveAll(
                        selected =>
                            string.Equals(
                                GetLiveMapSelectionKey(selected),
                                selectionKey,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );
                }
                else
                {
                    // -------------------------------------------------
                    // ADD SELECTED SYSTEM
                    // -------------------------------------------------

                    _selectedLiveMapSystems.Add(selectionKey);

                    _selectedLiveMapObjects.Add(item);
                }

                // -------------------------------------------------
                // REFRESH MAP
                // -------------------------------------------------

                RenderLiveFloorMap();

                if (FloorMapSummaryText != null)
                {
                    FloorMapSummaryText.Text =
                        _selectedLiveMapObjects.Count > 0
                            ? $"انتخاب گروهی: {_selectedLiveMapObjects.Count} سیستم • برای ادامه دوباره «پایان انتخاب» را بزنید."
                            : "هیچ سیستمی انتخاب نشده است.";
                }

                return;
            }

            // -------------------------------------------------
            // NORMAL SINGLE SYSTEM MODE
            // -------------------------------------------------

            var system = new ReservationSystem
            {
                VariationId = item.VariationId,
                SystemNumber = item.SystemNumber,
                Quantity = 1
            };

            ReservationItem? booking =
                FindMapReservation(item);

            string currentServerState =
                GetServerAvailabilityState(item);

            string stateText =
                item.Disabled
                    ? "غیرفعال"
                    : booking != null
                        ? "دارای رزرو"
                        : currentServerState switch
                        {
                            "reserved" => "رزرو شده در سرور",
                            "in_use" => "در حال استفاده / رزرو فعال",
                            "off" => "غیرفعال در سرور",
                            _ => "آزاد"
                        };

            string details =
                $"سیستم: {item.SystemNumber}\n" +
                $"نوع سیستم: {item.Category}\n" +
                $"وضعیت: {stateText}\n" +
                $"طبقه: {item.Floor}";

            // -------------------------------------------------
            // SHOW RESERVATION DETAILS
            // -------------------------------------------------

            if (booking != null)
            {
                var reservationSystem =
                    booking.Systems?.FirstOrDefault(x =>
                        string.Equals(
                            (x.SystemNumber ?? string.Empty).Trim(),
                            item.SystemNumber.Trim(),
                            StringComparison.OrdinalIgnoreCase
                        ) &&
                        (x.VariationId <= 0 ||
                         x.VariationId == item.VariationId)
                    )
                    ??
                    new ReservationSystem
                    {
                        VariationId = item.VariationId,
                        SystemNumber = item.SystemNumber,
                        Quantity = 1
                    };

                ShowReservationDetails(
                    booking,
                    reservationSystem
                );

                return;
            }

            // -------------------------------------------------
            // SERVER STATE DETAILS
            // -------------------------------------------------

            if (currentServerState == "reserved" ||
                currentServerState == "in_use")
            {
                details +=
                    "\n\nجزئیات رزرو در داده‌های محلی موجود نیست. فهرست رزروها را تازه‌سازی کنید.";
            }
            else
            {
                details +=
                    "\n\nبرای ثبت رزرو این سیستم، از صفحه رزرو دستی استفاده کنید.";
            }

            LobixDialog.Show(
                details,
                $"جزئیات سیستم {item.SystemNumber}",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }


        private static JsonElement GetJsonObject(JsonElement element, params string[] names)
        {
            foreach (var name in names)
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object) return value;
            return default;
        }
        private static JsonElement GetJsonArray(JsonElement element, params string[] names)
        {
            foreach (var name in names)
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array) return value;
            return default;
        }
        private static string ReadJsonString(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) return string.Empty;
            foreach (var name in names)
                if (element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) return value.ToString().Trim();
            return string.Empty;
        }
        private static int ReadJsonInt(JsonElement element, params string[] names) => int.TryParse(ReadJsonString(element, names), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        private static double ReadJsonDouble(JsonElement element, string a, string b, string c = "", string d = "", double fallback = 0)
        {
            foreach (var name in new[] { a, b, c, d }.Where(x => !string.IsNullOrWhiteSpace(x)))
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result)) return result;
            return fallback;
        }
        private static bool ReadJsonBool(JsonElement element, params string[] names)
        {
            string value = ReadJsonString(element, names);
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        // =========================================================
        // NEW MANUAL RESERVATION
        // =========================================================

        public async Task StartManualBookingAsync()
        {
            // The view starts its Local-First snapshot load in the constructor.
            // Wait briefly for that work to finish so system variations are available in the editor.
            while (_isLoading)
                await Task.Delay(80);

            if (_variations.Count == 0)
                await LoadReservationsAsync();

            await ShowBookingEditorAsync(null, null);
        }

        private async void NewReservationButton_Click(
            object? sender,
            RoutedEventArgs e)
        {
            await StartManualBookingAsync();
        }

        // =========================================================
        // RESERVATION DETAILS
        // =========================================================

        private void ShowReservationDetails(
            ReservationItem reservation,
            ReservationSystem system)
        {
            bool isManual =
                IsManualReservation(
                    reservation
                );

            var window =
                new Window
                {
                    Title =
                        "جزئیات رزرو",

                    Width = 540,
                    Height = 680,
                    MinWidth = 480,
                    MinHeight = 500,
                    MaxHeight = 900,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    FlowDirection = FlowDirection.RightToLeft,
                    Background = new SolidColorBrush(Color.FromRgb(17, 17, 22)),
                    Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 248)),
                    FontFamily = FindResource("Peyda") as FontFamily
                };

            Window? owner =
                Window.GetWindow(this);

            if (owner != null)
            {
                window.Owner = owner;
            }

            var root =
                new StackPanel
                {
                    Margin =
                        new Thickness(24)
                };

            AddDialogTitle(
                root,
                "جزئیات رزرو"
            );

            AddInfoRow(
                root,
                "وضعیت",
                GetFriendlyStatus(
                    reservation.Status
                )
            );

            AddInfoRow(
                root,
                "نوع رزرو",
                isManual
                    ? "رزرو دستی"
                    : "رزرو آنلاین"
            );

            AddInfoRow(
                root,
                "مشتری",
                string.IsNullOrWhiteSpace(
                    reservation.Customer
                )
                    ? "-"
                    : reservation.Customer
            );

            AddInfoRow(
                root,
                "شماره موبایل",
                string.IsNullOrWhiteSpace(
                    reservation.Phone
                )
                    ? "-"
                    : reservation.Phone
            );

            string displayedDate = reservation.Date;

            if (JalaliDateHelper.TryParseApiDate(
                    reservation.Date,
                    out DateTime parsedDate))
            {
                displayedDate = JalaliDateHelper.FormatDate(parsedDate);
            }

            AddInfoRow(
                root,
                "تاریخ",
                displayedDate
            );

            AddInfoRow(
                root,
                "سیستم",
                GetVariationName(
                    system.VariationId
                )
            );

            AddInfoRow(
                root,
                "تعداد",
                system.Quantity.ToString(
                    CultureInfo.InvariantCulture
                )
            );

            AddInfoRow(
                root,
                "ساعات",
                GetReservationTimeText(
                    reservation,
                    system
                )
            );

            AddInfoRow(
                root,
                "مبلغ",
                $"{reservation.Amount:N0} تومان"
            );

            AddInfoRow(
                root,
                "شناسه رزرو",
                reservation.Id
            );

            AddInfoRow(
                root,
                "توضیحات",
                string.IsNullOrWhiteSpace(
                    reservation.Note
                )
                    ? "-"
                    : reservation.Note
            );

            var actions =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    Margin =
                        new Thickness(
                            0,
                            24,
                            0,
                            0
                        )
                };

            // -----------------------------------------------------
            // فقط رزرو دستی قابلیت ویرایش/حذف دارد.
            // -----------------------------------------------------

            if (isManual)
            {
                var edit =
                    CreateDialogButton(
                        "ویرایش رزرو",
                        false
                    );

                edit.Click +=
                    async (_, _) =>
                    {
                        window.Close();

                        await ShowBookingEditorAsync(
                            reservation,
                            system
                        );
                    };

                actions.Children.Add(
                    edit
                );

                var delete =
                    CreateDialogButton(
                        "لغو رزرو",
                        true
                    );

                delete.Click += async (_, _) =>
                {
                    delete.IsEnabled = false;
                    delete.Content = "در حال لغو...";
                    try
                    {
                        await DeleteReservationAsync(reservation);
                        window.Close();
                    }
                    finally
                    {
                        if (window.IsVisible)
                        {
                            delete.IsEnabled = true;
                            delete.Content = "لغو رزرو";
                        }
                    }
                };

                actions.Children.Add(
                    delete
                );
            }

            var close =
                CreateDialogButton(
                    "بستن",
                    false
                );

            close.Click +=
                (_, _) =>
                    window.Close();

            actions.Children.Add(
                close
            );

            root.Children.Add(
                actions
            );

            window.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(Color.FromRgb(17, 17, 22)),
                Content = root
            };

            LobixDialog.AnimateWindowIn(window);
            window.ShowDialog();
        }

        // =========================================================
        // BOOKING EDITOR
        // =========================================================


        private async Task ShowBookingEditorAsync(
            ReservationItem? reservation,
            ReservationSystem? system,
            List<LiveMapObject>? selectedSystems = null)
        {
            bool isEdit = reservation != null;

            // -----------------------------------------------------
            // MULTI SYSTEM PREPARATION
            // -----------------------------------------------------

            bool isMultiSystemBooking =
                !isEdit &&
                selectedSystems != null &&
                selectedSystems.Count > 0;

            List<LiveMapObject> multiSystems =
                isMultiSystemBooking
                    ? selectedSystems!.ToList()
                    : new List<LiveMapObject>();

            if (isMultiSystemBooking)
            {
                multiSystems = multiSystems
                    .GroupBy(GetLiveMapSelectionKey)
                    .Select(g => g.First())
                    .ToList();

                if (multiSystems.Any(x => x.VariationId <= 0))
                {
                    LobixDialog.Show(
                        "نوع یکی از سیستم‌های انتخاب‌شده معتبر نیست.",
                        "خطا",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                foreach (LiveMapObject item in multiSystems)
                {
                    if (!_variations.Any(v => v.Id == item.VariationId))
                    {
                        LobixDialog.Show(
                            $"نوع سیستم شماره «{item.SystemNumber}» در اطلاعات گیم‌نت پیدا نشد.",
                            "خطا",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                        return;
                    }
                }
            }

            // -----------------------------------------------------
            // INITIAL VALUES
            // -----------------------------------------------------

            DateTime initialDate =
                isEdit
                    ? ParseDate(reservation!.Date)
                    : (_selectedDate ?? DateTime.Today);

            int initialStartMinutes = isEdit
                ? ParseTimeMinutes(reservation!.StartTime)
                : Math.Clamp((DateTime.Now.Hour * 60) + (DateTime.Now.Minute / 5 * 5), StartHour * 60, EndHour * 60 - 5);

            int initialDurationMinutes = isEdit
                ? GetReservationDurationMinutes(reservation!)
                : 60;

            string initialCustomer =
                isEdit ? reservation!.Customer : string.Empty;

            string initialPhone =
                isEdit ? reservation!.Phone : string.Empty;

            string initialNote =
                isEdit ? reservation!.Note : string.Empty;

            int initialVariation =
                isMultiSystemBooking
                    ? multiSystems.First().VariationId
                    : system?.VariationId
                        ?? _variations.FirstOrDefault()?.Id
                        ?? 0;

            int initialQuantity =
                system?.Quantity ?? 1;

            string initialSystemNumber =
                system?.SystemNumber ??
                (isEdit
                    ? reservation!.Systems?.FirstOrDefault()?.SystemNumber
                        ?? string.Empty
                    : string.Empty);

            // -----------------------------------------------------
            // WINDOW
            // -----------------------------------------------------

            var window = new Window
            {
                Title = isEdit
                    ? "ویرایش رزرو"
                    : isMultiSystemBooking
                        ? "ثبت رزرو چندسیستمی"
                        : "ثبت رزرو دستی",

                Width = 780,
                Height = 760,
                MinWidth = 740,
                MinHeight = 700,
                MaxHeight = 900,

                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,

                FlowDirection = FlowDirection.RightToLeft,

                Background = new SolidColorBrush(
                    Color.FromRgb(27, 27, 36)
                ),

                ResizeMode = ResizeMode.NoResize
            };

            Window? owner = Window.GetWindow(this);

            if (owner != null)
                window.Owner = owner;

            var root = new StackPanel
            {
                Margin = new Thickness(18)
            };

            AddDialogTitle(
                root,
                isEdit
                    ? "ویرایش رزرو دستی"
                    : isMultiSystemBooking
                        ? "ثبت رزرو چندسیستمی"
                        : "ثبت رزرو دستی"
            );

            var fieldsPanel = new WrapPanel
            {
                Width = 720,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 0)
            };

            root.Children.Add(fieldsPanel);

            // -----------------------------------------------------
            // CUSTOMER
            // -----------------------------------------------------

            TextBox customerBox = CreateTextBox(
                "نام مشتری",
                initialCustomer
            );

            fieldsPanel.Children.Add(
                CreateCompactField("نام مشتری", customerBox)
            );

            // -----------------------------------------------------
            // PHONE
            // -----------------------------------------------------

            TextBox phoneBox = CreateTextBox(
                "شماره موبایل",
                initialPhone
            );

            fieldsPanel.Children.Add(
                CreateCompactField("شماره موبایل", phoneBox)
            );

            // -----------------------------------------------------
            // DATE
            // -----------------------------------------------------

            DateTime selectedBookingDate = initialDate.Date;

            Button datePickerButton = new Button
            {
                Content = JalaliDateHelper.FormatDate(selectedBookingDate),
                Height = 38,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(12, 0, 12, 0),
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 13,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(244, 244, 248)
                ),
                Background = new SolidColorBrush(
                    Color.FromRgb(32, 32, 42)
                ),
                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(52, 52, 66)
                ),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            datePickerButton.Click += (_, _) =>
            {
                var picker = new JalaliDatePickerWindow(
                    selectedBookingDate,
                    DateTime.Today)
                {
                    Owner = window
                };

                if (picker.ShowDialog() == true &&
                    picker.SelectedDate.HasValue)
                {
                    selectedBookingDate =
                        picker.SelectedDate.Value.Date;

                    datePickerButton.Content =
                        JalaliDateHelper.FormatDate(selectedBookingDate);
                }
            };

            fieldsPanel.Children.Add(
                CreateCompactField("تاریخ شمسی", datePickerButton)
            );

            // -----------------------------------------------------
            // SYSTEM TYPE
            // -----------------------------------------------------

            ComboBox systemCombo = new ComboBox
            {
                Height = 38,
                Background = new SolidColorBrush(
                    Color.FromRgb(32, 32, 42)
                ),
                Foreground = new SolidColorBrush(
                    Color.FromRgb(244, 244, 248)
                ),
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 13,
                ItemsSource = _variations,
                DisplayMemberPath = "Title",
                IsEnabled = !isMultiSystemBooking
            };

            if (_variations.Count > 0)
            {
                GamenetVariation? selected =
                    _variations.FirstOrDefault(
                        item => item.Id == initialVariation
                    );

                systemCombo.SelectedItem =
                    selected ?? _variations.FirstOrDefault();
            }

            fieldsPanel.Children.Add(
                CreateCompactField("سیستم", systemCombo)
            );

            // -----------------------------------------------------
            // SYSTEM NUMBER
            // -----------------------------------------------------

            ComboBox systemNumberCombo = new ComboBox
            {
                Height = 38,

                Background = new SolidColorBrush(
                    Color.FromRgb(32, 32, 42)
                ),

                Foreground = new SolidColorBrush(
                    Color.FromRgb(244, 244, 248)
                ),

                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 13,

                IsEnabled = false,

                HorizontalContentAlignment =
                    HorizontalAlignment.Right
            };

            fieldsPanel.Children.Add(
                CreateCompactField("شماره دستگاه", systemNumberCombo)
            );

            TextBlock systemNumberHint = new TextBlock
            {
                Text = "ابتدا نوع سیستم را انتخاب کنید.",

                Foreground = new SolidColorBrush(
                    Color.FromRgb(170, 170, 185)
                ),

                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };

            fieldsPanel.Children.Add(systemNumberHint);

            // -----------------------------------------------------
            // MULTI SYSTEM DISPLAY
            // -----------------------------------------------------

            if (isMultiSystemBooking)
            {
                systemCombo.IsEnabled = false;
                systemNumberCombo.IsEnabled = false;

                systemNumberHint.Text =
                    $"حالت رزرو گروهی فعال است؛ {multiSystems.Count} دستگاه انتخاب شده.";

                systemNumberHint.Foreground =
                    new SolidColorBrush(Color.FromRgb(168, 130, 255));

                TextBlock selectedSystemsText = new TextBlock
                {
                    Text = string.Join(
                        "، ",
                        multiSystems.Select(item =>
                        {
                            string name =
                                !string.IsNullOrWhiteSpace(item.Label)
                                    ? item.Label
                                    : !string.IsNullOrWhiteSpace(item.SystemNumber)
                                        ? item.SystemNumber
                                        : item.Id;

                            GamenetVariation? v =
                                _variations.FirstOrDefault(
                                    x => x.Id == item.VariationId
                                );

                            return $"{v?.Title ?? v?.Name ?? "سیستم"} - {name}";
                        })
                    ),

                    Foreground = new SolidColorBrush(
                        Color.FromRgb(220, 220, 235)
                    ),

                    FontFamily = FindResource("Peyda") as FontFamily,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 10)
                };

                fieldsPanel.Children.Add(
                    CreateCompactField(
                        "دستگاه‌های انتخاب‌شده",
                        selectedSystemsText
                    )
                );
            }

            // -----------------------------------------------------
            // QUANTITY
            // -----------------------------------------------------

            ComboBox quantityCombo = new ComboBox
            {
                Height = 38,
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 13,
                IsEnabled = !isMultiSystemBooking
            };

            for (int quantity = 1; quantity <= 20; quantity++)
                quantityCombo.Items.Add(quantity);

            quantityCombo.SelectedItem =
                Math.Clamp(
                    isMultiSystemBooking ? multiSystems.Count : initialQuantity,
                    1,
                    20
                );

            fieldsPanel.Children.Add(
                CreateCompactField("تعداد", quantityCombo)
            );

            // -----------------------------------------------------
            // UPDATE SYSTEM NUMBERS
            // -----------------------------------------------------

            void UpdateSystemNumbers()
            {
                if (isMultiSystemBooking)
                    return;

                if (systemCombo.SelectedItem is not GamenetVariation variation)
                {
                    systemNumberCombo.ItemsSource = null;
                    systemNumberCombo.SelectedIndex = -1;
                    systemNumberCombo.IsEnabled = false;
                    systemNumberHint.Text =
                        "ابتدا نوع سیستم را انتخاب کنید.";

                    quantityCombo.IsEnabled = true;
                    return;
                }

                List<string> numbers =
                    variation.SystemNumbers?
                        .Where(number =>
                            !string.IsNullOrWhiteSpace(number))
                        .Select(number => number.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    ?? new List<string>();

                systemNumberCombo.ItemsSource = numbers;
                systemNumberCombo.SelectedIndex = -1;
                systemNumberCombo.IsEnabled = numbers.Count > 0;

                if (numbers.Count > 0)
                {
                    systemNumberHint.Text =
                        "شماره دستگاه موردنظر را انتخاب کنید.";

                    quantityCombo.SelectedItem = 1;
                    quantityCombo.IsEnabled = false;
                }
                else
                {
                    systemNumberHint.Text =
                        "شماره‌ای از سرور دریافت نشد؛ رزرو به‌صورت تعدادی انجام می‌شود.";

                    quantityCombo.IsEnabled = true;
                }
            }

            systemCombo.SelectionChanged += (_, _) =>
            {
                UpdateSystemNumbers();
            };

            systemNumberCombo.SelectionChanged += (_, _) =>
            {
                if (isMultiSystemBooking)
                    return;

                if (systemNumberCombo.SelectedItem != null)
                {
                    quantityCombo.SelectedItem = 1;
                    quantityCombo.IsEnabled = false;
                }
                else if (systemCombo.SelectedItem is GamenetVariation variation &&
                         (variation.SystemNumbers == null ||
                          variation.SystemNumbers.Count == 0))
                {
                    quantityCombo.IsEnabled = true;
                }
            };

            if (!isMultiSystemBooking)
            {
                UpdateSystemNumbers();

                if (!string.IsNullOrWhiteSpace(initialSystemNumber))
                {
                    string? exactSystem =
                        systemNumberCombo.Items
                            .Cast<object>()
                            .Select(x => x?.ToString() ?? string.Empty)
                            .FirstOrDefault(x =>
                                string.Equals(
                                    x,
                                    initialSystemNumber,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );

                    if (exactSystem != null)
                        systemNumberCombo.SelectedItem = exactSystem;
                }
            }

            // -----------------------------------------------------
            // START / END TIME (separate hour + minute selectors)
            // -----------------------------------------------------


            // -----------------------------------------------------
            // START / END TIME - HOUR AND MINUTE SELECTORS
            // -----------------------------------------------------

            ComboBox CreateTimeCombo()
            {
                return new ComboBox
                {
                    Width = 76,
                    MinWidth = 76,
                    Height = 38,
                    FontFamily = FindResource("Peyda") as FontFamily,
                    FontSize = 13,
                    IsEditable = false,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
            }

            ComboBox startHourCombo = CreateTimeCombo();
            ComboBox startMinuteCombo = CreateTimeCombo();
            ComboBox endHourCombo = CreateTimeCombo();
            ComboBox endMinuteCombo = CreateTimeCombo();

            // ساعت شروع
            for (int hour = StartHour; hour < EndHour; hour++)
            {
                startHourCombo.Items.Add(
                    hour.ToString("00", CultureInfo.InvariantCulture));
            }

            // دقیقه شروع با فاصله ۵ دقیقه
            for (int minute = 0; minute < 60; minute += 5)
            {
                startMinuteCombo.Items.Add(
                    minute.ToString("00", CultureInfo.InvariantCulture));
            }

            // مقدار اولیه شروع
            int safeInitialStart = Math.Clamp(
                initialStartMinutes,
                StartHour * 60,
                EndHour * 60 - 5);

            startHourCombo.SelectedItem =
                (safeInitialStart / 60).ToString("00", CultureInfo.InvariantCulture);

            startMinuteCombo.SelectedItem =
                (safeInitialStart % 60).ToString("00", CultureInfo.InvariantCulture);

            // دریافت زمان شروع به دقیقه
            int GetStartMinutes()
            {
                int hour = int.TryParse(
                    startHourCombo.SelectedItem?.ToString(), out int h)
                    ? h : StartHour;

                int minute = int.TryParse(
                    startMinuteCombo.SelectedItem?.ToString(), out int m)
                    ? m : 0;

                return (hour * 60) + minute;
            }

            // دریافت زمان پایان به دقیقه
            int GetEndMinutes()
            {
                int hour = int.TryParse(
                    endHourCombo.SelectedItem?.ToString(), out int h)
                    ? h : StartHour;

                int minute = int.TryParse(
                    endMinuteCombo.SelectedItem?.ToString(), out int m)
                    ? m : 0;

                return (hour * 60) + minute;
            }

            // ساخت گزینه‌های دقیقه پایان
            void RefreshEndMinuteOptions(int preferredMinute = -1)
            {
                if (endHourCombo.SelectedItem == null)
                    return;

                int startValue = GetStartMinutes();

                int endHour = int.Parse(
                    endHourCombo.SelectedItem.ToString()!);

                int minimumMinute = endHour == startValue / 60
                    ? startValue % 60 + 5
                    : 0;

                endMinuteCombo.Items.Clear();

                int firstMinute = ((minimumMinute + 4) / 5) * 5;

                int lastMinute = endHour == EndHour ? 0 : 55;

                for (int minute = firstMinute;
                     minute <= lastMinute;
                     minute += 5)
                {
                    endMinuteCombo.Items.Add(
                        minute.ToString("00", CultureInfo.InvariantCulture));
                }

                if (endMinuteCombo.Items.Count == 0)
                {
                    endMinuteCombo.SelectedIndex = -1;
                    return;
                }

                string preferred = preferredMinute >= 0
                    ? preferredMinute.ToString("00", CultureInfo.InvariantCulture)
                    : endMinuteCombo.Items[0].ToString()!;

                endMinuteCombo.SelectedItem =
                    endMinuteCombo.Items.Contains(preferred)
                        ? preferred
                        : endMinuteCombo.Items[0];
            }

            // ساخت گزینه‌های ساعت پایان
            void RefreshEndHourOptions(int preferredEnd = -1)
            {
                int startValue = GetStartMinutes();

                int oldEnd = preferredEnd >= 0
                    ? preferredEnd
                    : (endHourCombo.SelectedItem != null &&
                       endMinuteCombo.SelectedItem != null
                        ? GetEndMinutes()
                        : startValue + 60);

                endHourCombo.Items.Clear();

                int firstEndHour = startValue / 60;

                if (startValue % 60 >= 55)
                    firstEndHour++;

                for (int hour = firstEndHour; hour <= EndHour; hour++)
                {
                    endHourCombo.Items.Add(
                        hour.ToString("00", CultureInfo.InvariantCulture));
                }

                int desiredHour = Math.Clamp(
                    oldEnd / 60,
                    firstEndHour,
                    EndHour);

                endHourCombo.SelectedItem =
                    desiredHour.ToString("00", CultureInfo.InvariantCulture);

                RefreshEndMinuteOptions(oldEnd % 60);
            }

            // مقدار اولیه پایان
            int initialEnd = Math.Clamp(
                safeInitialStart + Math.Max(5, initialDurationMinutes),
                safeInitialStart + 5,
                EndHour * 60);

            RefreshEndHourOptions(initialEnd);

            // کنترل‌های ساعت و دقیقه شروع
            var startTimePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                FlowDirection = FlowDirection.LeftToRight
            };

            startTimePanel.Children.Add(startHourCombo);

            startTimePanel.Children.Add(new TextBlock
            {
                Text = ":",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
                FontSize = 16
            });

            startTimePanel.Children.Add(startMinuteCombo);

            // کنترل‌های ساعت و دقیقه پایان
            var endTimePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                FlowDirection = FlowDirection.LeftToRight
            };

            endTimePanel.Children.Add(endHourCombo);

            endTimePanel.Children.Add(new TextBlock
            {
                Text = ":",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
                FontSize = 16
            });

            endTimePanel.Children.Add(endMinuteCombo);

            // افزودن به فرم
            fieldsPanel.Children.Add(
                CreateCompactField("ساعت شروع (ساعت : دقیقه)", startTimePanel));

            fieldsPanel.Children.Add(
                CreateCompactField("ساعت پایان (ساعت : دقیقه)", endTimePanel));

            // نمایش مدت رزرو
            TextBlock durationSummary = new TextBlock
            {
                Text = string.Empty,
                Foreground = new SolidColorBrush(Color.FromRgb(168, 130, 255)),
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 8)
            };

            void UpdateDurationSummary()
            {
                int duration = GetEndMinutes() - GetStartMinutes();

                durationSummary.Text = duration > 0
                    ? $"مدت رزرو: {duration / 60} ساعت و {duration % 60} دقیقه"
                    : "ساعت پایان باید بعد از ساعت شروع باشد.";
            }

            // تغییر ساعت یا دقیقه شروع
            void StartTimeChanged()
            {
                int oldEnd = endHourCombo.SelectedItem != null &&
                             endMinuteCombo.SelectedItem != null
                    ? GetEndMinutes()
                    : GetStartMinutes() + 60;

                RefreshEndHourOptions(oldEnd);
                UpdateDurationSummary();
            }

            startHourCombo.SelectionChanged += (_, _) =>
                StartTimeChanged();

            startMinuteCombo.SelectionChanged += (_, _) =>
                StartTimeChanged();

            // تغییر ساعت پایان
            endHourCombo.SelectionChanged += (_, _) =>
            {
                RefreshEndMinuteOptions();
                UpdateDurationSummary();
            };

            // تغییر دقیقه پایان
            endMinuteCombo.SelectionChanged += (_, _) =>
                UpdateDurationSummary();

            fieldsPanel.Children.Add(
                CreateCompactField("مدت واقعی", durationSummary));

            UpdateDurationSummary();

            // -----------------------------------------------------
            // NOTE
            // -----------------------------------------------------

            TextBox noteBox = CreateTextBox(
                "توضیحات",
                initialNote
            );

            noteBox.Height = 80;
            noteBox.AcceptsReturn = true;
            noteBox.TextWrapping = TextWrapping.Wrap;

            fieldsPanel.Children.Add(
                CreateCompactField("توضیحات", noteBox)
            );

            // -----------------------------------------------------
            // VALIDATION
            // -----------------------------------------------------

            TextBlock validationText = new TextBlock
            {
                Foreground = new SolidColorBrush(
                    Color.FromRgb(220, 38, 38)
                ),

                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };

            root.Children.Add(validationText);

            // -----------------------------------------------------
            // BUTTONS
            // -----------------------------------------------------

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var cancel = CreateDialogButton("انصراف", false);

            cancel.Click += (_, _) => window.Close();

            buttons.Children.Add(cancel);

            var save = CreateDialogButton(
                isEdit ? "ذخیره تغییرات" : "ثبت رزرو",
                false
            );

            buttons.Children.Add(save);
            root.Children.Add(buttons);

            // =====================================================
            // SAVE
            // =====================================================

            save.Click += async (_, _) =>
            {
                validationText.Text = string.Empty;
                save.IsEnabled = false;
                save.Content = "در حال بررسی...";

                try
                {
                    // -------------------------------------------------
                    // DATE
                    // -------------------------------------------------

                    DateTime selectedDate = selectedBookingDate.Date;

                    if (selectedDate == DateTime.MinValue)
                    {
                        validationText.Text =
                            "تاریخ رزرو را انتخاب کنید.";
                        return;
                    }

                    if (selectedDate < DateTime.Today)
                    {
                        validationText.Text =
                            "ثبت رزرو برای تاریخ گذشته مجاز نیست.";
                        return;
                    }

                    // -------------------------------------------------
                    // CUSTOMER
                    // -------------------------------------------------

                    string customer = customerBox.Text.Trim();

                    if (string.IsNullOrWhiteSpace(customer))
                    {
                        validationText.Text =
                            "نام مشتری را وارد کنید.";
                        return;
                    }

                    // -------------------------------------------------
                    // PHONE
                    // -------------------------------------------------

                    string phone = phoneBox.Text.Trim();

                    if (string.IsNullOrWhiteSpace(phone))
                    {
                        validationText.Text =
                            "شماره موبایل را وارد کنید.";
                        return;
                    }

                   

                    // -------------------------------------------------
                    // START / DURATION
                    // -------------------------------------------------

                    if (startHourCombo.SelectedItem == null ||
                        startMinuteCombo.SelectedItem == null ||
                        endHourCombo.SelectedItem == null ||
                        endMinuteCombo.SelectedItem == null)
                    {
                        validationText.Text =
                            "ساعت و دقیقه شروع و پایان را انتخاب کنید.";
                        return;
                    }

                    int startMinute = GetStartMinutes();
                    int endMinute = GetEndMinutes();

                    int durationMinutes = endMinute - startMinute;

                    if (durationMinutes < 5 ||
                        durationMinutes % 5 != 0 ||
                        startMinute < StartHour * 60 ||
                        endMinute > EndHour * 60)
                    {
                        validationText.Text =
                            "بازه زمانی رزرو معتبر نیست. حداقل مدت رزرو ۵ دقیقه است.";
                        return;
                    }


                    startMinute = GetStartMinutes();
                    endMinute = GetEndMinutes();
                    durationMinutes = endMinute - startMinute;

                    if (durationMinutes < 5 || durationMinutes % 5 != 0 ||
                        startMinute < StartHour * 60 || endMinute > EndHour * 60)
                    {
                        validationText.Text = "بازه زمانی رزرو معتبر نیست. حداقل مدت رزرو ۵ دقیقه است.";
                        return;
                    }

                    // -------------------------------------------------
                    // SYSTEM REQUESTS
                    // -------------------------------------------------

                    List<BookingSystemRequest> bookingSystems =
                        new List<BookingSystemRequest>();

                    decimal amount = 0;

                    if (isMultiSystemBooking)
                    {
                        if (multiSystems.Count == 0)
                        {
                            validationText.Text =
                                "هیچ دستگاهی برای ثبت رزرو انتخاب نشده است.";
                            return;
                        }

                        // جلوگیری از ارسال دستگاه تکراری
                        var uniqueSystems = multiSystems
                            .GroupBy(GetLiveMapSelectionKey)
                            .Select(g => g.First())
                            .ToList();

                        if (uniqueSystems.Count != multiSystems.Count)
                        {
                            validationText.Text =
                                "در لیست دستگاه‌های انتخاب‌شده مورد تکراری وجود دارد.";
                            return;
                        }

                        foreach (LiveMapObject item in uniqueSystems)
                        {
                            GamenetVariation? variation =
                                _variations.FirstOrDefault(
                                    v => v.Id == item.VariationId
                                );

                            if (variation == null)
                            {
                                validationText.Text =
                                    $"نوع سیستم شماره «{item.SystemNumber}» معتبر نیست.";
                                return;
                            }

                            int workStart = ParseWorkHour(
                                variation.WorkStart,
                                StartHour
                            );

                            int workEnd = ParseWorkHour(
                                variation.WorkEnd,
                                EndHour
                            );

                            if (workEnd <= workStart)
                            {
                                workStart = StartHour;
                                workEnd = EndHour;
                            }

                            if (startMinute < workStart * 60 ||
                                endMinute > workEnd * 60)
                            {
                                validationText.Text =
                                    $"سیستم «{variation.Title ?? variation.Name}» فقط از ساعت " +
                                    $"{workStart:00}:00 تا {workEnd:00}:00 فعال است.";

                                return;
                            }

                            string number =
                                item.SystemNumber?.Trim() ?? string.Empty;

                            if (string.IsNullOrWhiteSpace(number) &&
                                string.IsNullOrWhiteSpace(item.SystemId))
                            {
                                validationText.Text =
                                    "یکی از دستگاه‌ها نه شماره دستگاه دارد و نه شناسه فیزیکی؛ امکان ثبت دقیق آن وجود ندارد.";

                                return;
                            }

                            bookingSystems.Add(
                                new BookingSystemRequest
                                {
                                    VariationId = variation.Id,
                                    Quantity = 1,
                                    SystemNumber = number,
                                    SystemId =
                                        string.IsNullOrWhiteSpace(item.SystemId)
                                            ? null
                                            : item.SystemId.Trim()
                                }
                            );

                            amount += variation.Price * durationMinutes / 60m;
                        }
                    }
                    else
                    {
                        if (systemCombo.SelectedItem is not GamenetVariation variation)
                        {
                            validationText.Text =
                                "سیستم را انتخاب کنید.";
                            return;
                        }

                        string systemNumber =
                            systemNumberCombo.SelectedItem?.ToString()?.Trim()
                            ?? string.Empty;

                        if (variation.SystemNumbers != null &&
                            variation.SystemNumbers.Count > 0)
                        {
                            if (string.IsNullOrWhiteSpace(systemNumber))
                            {
                                validationText.Text =
                                    "شماره دستگاه را انتخاب کنید.";
                                return;
                            }

                            bool validSystemNumber =
                                variation.SystemNumbers.Any(number =>
                                    string.Equals(
                                        number?.Trim(),
                                        systemNumber,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                );

                            if (!validSystemNumber)
                            {
                                validationText.Text =
                                    "شماره دستگاه انتخاب‌شده متعلق به این سیستم نیست.";
                                return;
                            }
                        }

                        int quantity =
                            string.IsNullOrWhiteSpace(systemNumber)
                                ? Convert.ToInt32(quantityCombo.SelectedItem)
                                : 1;

                        if (quantity <= 0)
                        {
                            validationText.Text =
                                "تعداد دستگاه معتبر نیست.";
                            return;
                        }

                        int workStart = ParseWorkHour(
                            variation.WorkStart,
                            StartHour
                        );

                        int workEnd = ParseWorkHour(
                            variation.WorkEnd,
                            EndHour
                        );

                        if (workEnd <= workStart)
                        {
                            workStart = StartHour;
                            workEnd = EndHour;
                        }

                        if (startMinute < workStart * 60 ||
                            endMinute > workEnd * 60)
                        {
                            validationText.Text =
                                $"این سیستم فقط از ساعت {workStart:00}:00 تا {workEnd:00}:00 فعال است.";
                            return;
                        }

                        bookingSystems.Add(
                            new BookingSystemRequest
                            {
                                VariationId = variation.Id,
                                Quantity = quantity,
                                SystemNumber = systemNumber
                            }
                        );

                        amount = variation.Price * quantity * durationMinutes / 60m;
                    }

                    amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

                    // -------------------------------------------------
                    // CAPACITY
                    // -------------------------------------------------

                    bool isOnline =
                        await _apiService.CheckConnectionAsync();

                    if (isOnline)
                    {
                        bool available;

                        if (isMultiSystemBooking)
                        {
                            available = await CheckMultiSystemCapacityAsync(
                                multiSystems,
                                selectedDate,
                                startMinute,
                                durationMinutes,
                                validationText
                            );
                        }
                        else
                        {
                            BookingSystemRequest selected =
                                bookingSystems.First();

                            available = await CheckCapacityAsync(
                                selected.VariationId,
                                selectedDate,
                                startMinute,
                                durationMinutes,
                                selected.Quantity,
                                validationText,
                                isEdit ? reservation?.Id : null
                            );
                        }

                        if (!available)
                            return;
                    }
                    else
                    {
                        validationText.Text =
                            "اینترنت قطع است؛ رزرو روی این دستگاه ذخیره می‌شود و پس از اتصال، ظرفیت توسط سرور بررسی خواهد شد.";
                    }

                    // -------------------------------------------------
                    // REQUEST
                    // -------------------------------------------------

                    var request = new BookingCreateRequest
                    {
                        ProductId = _gamenet.Id,

                        Date = selectedDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        ),

                        StartTime = FormatTimeOfDay(startMinute),

                        EndTime = FormatTimeOfDay(endMinute),

                        DurationMinutes = durationMinutes,

                        Hours = (int)Math.Ceiling(durationMinutes / 60.0),

                        Customer = customer,

                        CustomerId = string.Empty,

                        Phone = phone,

                        Amount = amount,

                        Note = noteBox.Text.Trim(),

                        Systems = bookingSystems
                    };

                    // -------------------------------------------------
                    // CREATE / UPDATE
                    // -------------------------------------------------

                    if (!isEdit)
                    {
                        if (!isOnline)
                        {
                            string localId =
                                "offline-" + Guid.NewGuid().ToString("N");

                            var localRecord =
                                OfflineBookingMapper.ToReservation(
                                    request,
                                    localId,
                                    "Pending"
                                );

                            await LocalDatabase.Instance.SaveLocalChangeWithPayloadAsync(
                                _gamenet.Id,
                                "ManualBooking",
                                localId,
                                localRecord,
                                "Create",
                                request
                            );
                        }
                        else
                        {
                            string idempotencyKey =
                                $"desktop_{Guid.NewGuid():N}";

                            await _apiService.CreateBookingAsync(
                                request,
                                idempotencyKey
                            );
                        }
                    }
                    else
                    {
                        MessageBoxResult confirm = LobixDialog.Show(
                            "اطلاعات رزرو انتخاب‌شده با اطلاعات جدید جایگزین می‌شود. ادامه می‌دهید؟",
                            "تأیید ویرایش",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question
                        );

                        if (confirm != MessageBoxResult.Yes)
                            return;

                        if (!isOnline)
                        {
                            var localRecord =
                                OfflineBookingMapper.ToReservation(
                                    request,
                                    reservation!.Id,
                                    "Pending"
                                );

                            await LocalDatabase.Instance.SaveLocalChangeWithPayloadAsync(
                                _gamenet.Id,
                                "ManualBooking",
                                reservation.Id,
                                localRecord,
                                "Update",
                                request
                            );
                        }
                        else
                        {
                            await _apiService.UpdateBookingAsync(
                                _gamenet.Id,
                                reservation!.Id,
                                request,
                                "desktop_" + Guid.NewGuid().ToString("N")
                            );
                        }
                    }

                    // -------------------------------------------------
                    // SUCCESS
                    // -------------------------------------------------

                    window.Close();

                    LobixDialog.Show(
                        !isOnline
                            ? "تغییرات روی این دستگاه ذخیره شد و در انتظار تأیید سرور است."
                            : isEdit
                                ? "رزرو با موفقیت ویرایش شد."
                                : isMultiSystemBooking
                                    ? $"رزرو {bookingSystems.Count} دستگاه با موفقیت ثبت شد."
                                    : "رزرو دستی با موفقیت ثبت شد.",

                        "لابیکس",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    _selectedDate = selectedDate;

                    if (isMultiSystemBooking)
                    {
                        _selectedLiveMapSystems.Clear();
                        _selectedLiveMapObjects.Clear();

                        _liveMapMultiSelectMode = false;

                        if (LiveMapMultiSelectButton != null)
                        {
                            LiveMapMultiSelectButton.Content =
                                "انتخاب چند سیستم";
                        }
                    }

                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    validationText.Text = UserFacingErrors.GetMessage(
                        ex,
                        "ثبت یا ویرایش رزرو انجام نشد. اطلاعات را بررسی و دوباره تلاش کنید."
                    );
                }
                finally
                {
                    save.IsEnabled = true;

                    save.Content =
                        isEdit
                            ? "ذخیره تغییرات"
                            : "ثبت رزرو";
                }
            };

            window.Content = root;

            LobixDialog.AnimateWindowIn(window);
            window.ShowDialog();
        }

        // =========================================================
        // CAPACITY
        // =========================================================


        private async Task<bool> CheckCapacityAsync(
            int variationId,
            DateTime date,
            int startMinute,
            int durationMinutes,
            int quantity,
            TextBlock errorText,
            string? excludedReservationId)
        {
            try
            {
                if (variationId <= 0 || quantity <= 0 || durationMinutes < 5)
                {
                    errorText.Text = "اطلاعات سیستم یا بازه زمانی معتبر نیست.";
                    return false;
                }

                int endMinute = startMinute + durationMinutes;
                if (startMinute < 0 || endMinute > 24 * 60 || endMinute <= startMinute)
                {
                    errorText.Text = "بازه زمانی رزرو معتبر نیست.";
                    return false;
                }

                string targetDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                AvailabilityResponse response = await _apiService.GetAvailabilityAsync(
                    _gamenet.Id, variationId, targetDate);
                AvailabilityData? availability = response.Availability;

                if (availability?.Hours == null || availability.Hours.Count == 0)
                {
                    errorText.Text = "سرور برای این سیستم اطلاعات ظرفیت ساعتی برنگرداند.";
                    return false;
                }

                ReservationItem? editedReservation = null;
                if (!string.IsNullOrWhiteSpace(excludedReservationId))
                {
                    editedReservation = _allReservations.FirstOrDefault(item =>
                        string.Equals(item.Id, excludedReservationId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(ParseDate(item.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            targetDate, StringComparison.Ordinal));
                }

                int firstHour = startMinute / 60;
                int lastHourExclusive = (endMinute + 59) / 60;

                for (int hour = firstHour; hour < lastHourExclusive; hour++)
                {
                    AvailabilityHour? serverHour = availability.Hours.FirstOrDefault(item => item.Hour == hour);
                    if (serverHour == null)
                    {
                        errorText.Text = $"ساعت {hour:00}:00 در بازه کاری این سیستم نیست.";
                        return false;
                    }

                    int capacity = serverHour.Capacity > 0
                        ? serverHour.Capacity
                        : serverHour.Available + serverHour.Reserved;

                    // Calculate concurrent occupancy from exact reservation intervals.
                    // This allows adjacent reservations inside the same clock-hour (for example
                    // 10:00-10:30 and 10:30-11:00) when their real intervals do not overlap.
                    int requestedStart = Math.Max(startMinute, hour * 60);
                    int requestedEnd = Math.Min(endMinute, (hour + 1) * 60);
                    var events = new List<(int Minute, int Delta)>();
                    int knownServerBookingsInHour = 0;

                    foreach (ReservationItem existing in _allReservations)
                    {
                        if (string.Equals(existing.Id, excludedReservationId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!string.Equals(ParseDate(existing.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            targetDate, StringComparison.Ordinal))
                            continue;
                        if (string.Equals(NormalizeStatus(existing.OperatorStatus), "rejected", StringComparison.OrdinalIgnoreCase) ||
                            new[] { "cancelled", "canceled", "completed", "refunded", "failed", "rejected" }
                                .Contains(NormalizeStatus(existing.Status), StringComparer.OrdinalIgnoreCase))
                            continue;

                        int existingStart = ParseTimeMinutes(existing.StartTime);
                        int existingEnd = existingStart + GetReservationDurationMinutes(existing);
                        if (existingStart >= requestedEnd || requestedStart >= existingEnd)
                            continue;

                        int existingQuantity = 0;
                        if (existing.Systems != null && existing.Systems.Count > 0)
                            existingQuantity = existing.Systems.Where(x => x.VariationId == variationId)
                                .Sum(x => Math.Max(0, x.Quantity));
                        else if (existing.VariationId == variationId)
                            existingQuantity = Math.Max(0, existing.Quantity);

                        if (existingQuantity <= 0)
                            continue;

                        int clippedStart = Math.Max(requestedStart, existingStart);
                        int clippedEnd = Math.Min(requestedEnd, existingEnd);
                        events.Add((clippedStart, existingQuantity));
                        events.Add((clippedEnd, -existingQuantity));

                        if (string.Equals(existing.LocalSyncState, "Synced", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(existing.LocalSyncState))
                            knownServerBookingsInHour += existingQuantity;
                    }

                    events.Sort((a, b) => a.Minute.CompareTo(b.Minute));
                    int concurrent = 0;
                    int maxConcurrent = 0;
                    for (int i = 0; i < events.Count;)
                    {
                        int minute = events[i].Minute;
                        int delta = 0;
                        while (i < events.Count && events[i].Minute == minute)
                            delta += events[i++].Delta;
                        concurrent += delta;
                        maxConcurrent = Math.Max(maxConcurrent, concurrent);
                    }

                    // If the local sync snapshot has no matching server records at all, use the
                    // server's hourly count as a conservative fallback rather than risk overbooking.
                    if (knownServerBookingsInHour == 0 && events.Count == 0)
                        maxConcurrent = Math.Max(maxConcurrent, serverHour.Reserved);

                    int available = Math.Max(0, capacity - maxConcurrent);
                    if (quantity > available)
                    {
                        GamenetVariation? variation = _variations.FirstOrDefault(item => item.Id == variationId);
                        string variationName = variation?.Title ?? variation?.Name ?? variationId.ToString();
                        errorText.Text =
                            $"ظرفیت سیستم «{variationName}» در بازه {FormatTimeOfDay(startMinute)} تا {FormatTimeOfDay(endMinute)} کافی نیست.\n" +
                            $"ظرفیت کل: {capacity}\n" +
                            $"حداکثر دستگاه‌های هم‌زمان در این بازه: {maxConcurrent}\n" +
                            $"ظرفیت آزاد: {available}\n" +
                            $"تعداد درخواستی: {quantity}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorText.Text = UserFacingErrors.GetMessage(
                    ex,
                    "امکان بررسی ظرفیت از سرور وجود ندارد. برای جلوگیری از رزرو اشتباه، عملیات متوقف شد.");
                return false;
            }
        }

        // =========================================================
        // MULTI SYSTEM CAPACITY
        // =========================================================

        private async Task<bool> CheckMultiSystemCapacityAsync(
            IEnumerable<LiveMapObject> selectedSystems,
            DateTime date,
            int startMinute,
            int durationMinutes,
            TextBlock errorText)
        {
            try
            {
                List<LiveMapObject> systems = selectedSystems.ToList();
                if (systems.Count == 0)
                {
                    errorText.Text = "هیچ سیستمی برای رزرو انتخاب نشده است.";
                    return false;
                }
                if (systems.Any(x => x.VariationId <= 0))
                {
                    errorText.Text = "نوع یکی از سیستم‌های انتخاب‌شده معتبر نیست.";
                    return false;
                }

                foreach (var group in systems.GroupBy(x => x.VariationId))
                {
                    bool available = await CheckCapacityAsync(
                        group.Key, date, startMinute, durationMinutes, group.Count(), errorText, null);
                    if (!available) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                errorText.Text = UserFacingErrors.GetMessage(ex, "بررسی ظرفیت سیستم‌های انتخاب‌شده انجام نشد.");
                return false;
            }
        }

        // =========================================================
        // DELETE
        // =========================================================

        private async Task DeleteReservationAsync(ReservationItem reservation)
        {
            if (!IsManualReservation(reservation))
            {
                LobixDialog.Show(
                    "رزروهای آنلاین از داخل اپراتور قابل حذف نیستند.",
                    "لابیکس",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            MessageBoxResult result = LobixDialog.Show(
                $"رزرو «{reservation.Customer}» لغو شود؟\n\nاین عملیات قابل بازگشت نیست.",
                "تأیید لغو رزرو",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                bool isOnline = await _apiService.CheckConnectionAsync();

                bool isUnpublishedLocalCreate = reservation.Id.StartsWith(
                    "offline-",
                    StringComparison.OrdinalIgnoreCase);

                if (isUnpublishedLocalCreate)
                {
                    // حذف رزروی که هنوز به سرور ارسال نشده است
                    await LocalDatabase.Instance.DeleteLocalEntityAsync(
                        _gamenet.Id,
                        "ManualBooking",
                        reservation.Id);
                }
                else if (!isOnline)
                {
                    // ثبت لغو برای همگام‌سازی پس از اتصال مجدد
                    await LocalDatabase.Instance.PutEntityAsync(
                        "ManualBooking",
                        reservation.Id,
                        _gamenet.Id,
                        reservation,
                        0,
                        "Synced");

                    await LocalDatabase.Instance.DeleteLocalEntityAsync(
                        _gamenet.Id,
                        "ManualBooking",
                        reservation.Id);
                }
                else
                {
                    // ابتدا لغو را روی سرور انجام می‌دهیم
                    await _apiService.DeleteBookingAsync(
                        _gamenet.Id,
                        reservation.Id,
                        "desktop_" + Guid.NewGuid().ToString("N"));

                    // فقط پس از موفقیت سرور، نسخه محلی را حذف‌شده علامت می‌زنیم
                    await LocalDatabase.Instance.MarkLocalEntityDeletedAsync(
                        _gamenet.Id,
                        "ManualBooking",
                        reservation.Id);
                }

                LobixDialog.Show(
                    isOnline && !isUnpublishedLocalCreate
                        ? "رزرو با موفقیت لغو شد."
                        : "لغو رزرو ثبت شد و در صورت نیاز پس از اتصال به سرور همگام‌سازی می‌شود.",
                    "لابیکس",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "DELETE RESERVATION ERROR: " + ex);

                LobixDialog.Show(
                    UserFacingErrors.GetMessage(
                        ex,
                        "لغو رزرو انجام نشد. اتصال را بررسی کنید و دوباره تلاش کنید."),
                    "لغو رزرو ناموفق بود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        // =========================================================
        // FULLSCREEN TIMELINE
        // =========================================================

        private void FullscreenTimelineButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // اگر پنجره از قبل باز است، همان را فعال کن.
            if (_timelineFullscreenWindow != null)
            {
                _timelineFullscreenWindow.Activate();
                return;
            }

            if (TimelineCanvas == null || TimelineScrollViewer == null)
                return;

            var fullscreenWindow = new Window
            {
                Title = "لابیکس | جدول تمام‌صفحه رزروها",
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(Color.FromRgb(17, 17, 22)),
                Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 248)),
                FontFamily = FindResource("Peyda") as FontFamily
            };

            Window? owner = Window.GetWindow(this);

            if (owner != null && owner != fullscreenWindow)
            {
                fullscreenWindow.Owner = owner;
            }

            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 17, 22))
            };

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(58) });

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // نوار بالای پنجره
            var toolbar = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(27, 27, 36)),
                Margin = new Thickness(0),
            };

            toolbar.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            toolbar.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            toolbar.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "جدول زمان‌بندی رزروها",
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(22, 0, 12, 0)
            };

            Grid.SetColumn(title, 0);
            toolbar.Children.Add(title);

            var fullscreenSearch = new TextBox
            {
                Text = SearchTextBox?.Text ?? string.Empty,
                Width = 340,
                Height = 36,
                Margin = new Thickness(10, 0, 14, 0),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 13,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(32, 32, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 80)),
                BorderThickness = new Thickness(1),
                ToolTip = "جست‌وجو در نام، موبایل، کد رزرو یا سیستم"
            };
            fullscreenSearch.TextChanged += (_, _) =>
            {
                if (SearchTextBox != null && SearchTextBox.Text != fullscreenSearch.Text)
                    SearchTextBox.Text = fullscreenSearch.Text;
            };
            Grid.SetColumn(fullscreenSearch, 1);
            toolbar.Children.Add(fullscreenSearch);

            var closeButton = new Button
            {
                Content = "✕  بستن  (Esc)",
                FontFamily = FindResource("Peyda") as FontFamily,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(219, 60, 69)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(219, 60, 69)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 8, 18, 8),
                Margin = new Thickness(8, 8, 16, 8),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };

            closeButton.Click += (_, _) => fullscreenWindow.Close();

            Grid.SetColumn(closeButton, 2);
            toolbar.Children.Add(closeButton);

            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            // جدول اصلی؛ همان Canvas فعلی جابه‌جا می‌شود و دوباره ساخته نمی‌شود.
            var fullscreenScrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
                PanningMode = PanningMode.Both,
                Background = new SolidColorBrush(Color.FromRgb(17, 17, 22))
            };

            fullscreenScrollViewer.Resources.Add(
                typeof(ScrollBar),
                new Style(typeof(ScrollBar))
                {
                    BasedOn = FindResource("DarkScrollBarStyle") as Style
                });

            Grid.SetRow(fullscreenScrollViewer, 1);
            root.Children.Add(fullscreenScrollViewer);

            // یک Canvas نمی‌تواند هم‌زمان فرزند دو ScrollViewer باشد.
            // ابتدا آن را از صفحه اصلی جدا می‌کنیم.
            TimelineScrollViewer.Content = null;
            fullscreenScrollViewer.Content = TimelineCanvas;

            _activeTimelineScrollViewer = fullscreenScrollViewer;
            _timelineFullscreenWindow = fullscreenWindow;

            fullscreenWindow.Content = root;

            // بستن با Escape
            fullscreenWindow.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    fullscreenWindow.Close();
                }
            };

            // با تغییر ابعاد پنجره، تایم‌لاین را متناسب با فضای جدید رسم کن.
            fullscreenScrollViewer.SizeChanged += (_, _) =>
            {
                DrawTimeline();
            };

            fullscreenWindow.Loaded += (_, _) =>
            {
                DrawTimeline();
            };

            // هنگام بسته شدن، Canvas را به صفحه اصلی برگردان.
            fullscreenWindow.Closing += (_, _) =>
            {
                fullscreenScrollViewer.Content = null;

                TimelineScrollViewer.Content = TimelineCanvas;

                _activeTimelineScrollViewer = TimelineScrollViewer;
                _timelineFullscreenWindow = null;

                DrawTimeline();
            };

            fullscreenWindow.Show();
            fullscreenWindow.Activate();
        }

        // =========================================================
        // BACK
        // =========================================================

        private void BackToDashboardButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (
                Window.GetWindow(this)
                is MainWindow mainWindow
            )
            {
                mainWindow.ShowDashboard();
            }
        }

        // =========================================================
        // UI HELPERS
        // =========================================================

        private void AddDialogTitle(
            Panel panel,
            string text)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = text,

                    FontFamily =
                        FindResource("Peyda")
                        as FontFamily,

                    FontSize = 21,

                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
    new SolidColorBrush(
        Color.FromRgb(244, 244, 248)
    ),

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            20
                        )
                }
            );
        }

        private static void AddInfoRow(
            Panel panel,
            string title,
            string value)
        {
            var row =
                new Grid
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10
                        )
                };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            120
                        )
                }
            );

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star
                        )
                }
            );

            var titleText =
                new TextBlock
                {
                    Text = title,

                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(165, 167, 181)
                        )
                };

            Grid.SetColumn(
                titleText,
                0
            );

            row.Children.Add(
                titleText
            );

            var valueText =
                new TextBlock
                {
                    Text = value,

                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(244, 244, 248)
                        ),

                    TextWrapping =
                        TextWrapping.Wrap
                };

            Grid.SetColumn(
                valueText,
                1
            );

            row.Children.Add(
                valueText
            );

            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(27, 27, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(52, 52, 66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = row
            });
        }

        private TextBox CreateTextBox(
            string placeholder,
            string value)
        {
            return new TextBox
            {
                Text = value,

                Height = 38,

                Padding =
                    new Thickness(
                        10,
                        7,
                        10,
                        7
                    ),

                FontFamily =
                    FindResource("Peyda")
                    as FontFamily,

                FontSize = 13,

                Background =
    new SolidColorBrush(
        Color.FromRgb(32, 32, 42)
    ),

                Foreground =
    new SolidColorBrush(
        Color.FromRgb(244, 244, 248)
    ),

                BorderBrush =
    new SolidColorBrush(
        Color.FromRgb(52, 52, 66)
    )
            };
        }

        private Border CreateCompactField(string title, UIElement control)
        {
            Border field = new Border
            {
                Width = 345,
                Margin = new Thickness(0, 0, 12, 5),
                Padding = new Thickness(12),
                Background = new SolidColorBrush(
                    Color.FromRgb(35, 35, 45)
                ),
                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(65, 65, 80)
                ),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };

            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            TextBlock titleText = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };

            panel.Children.Add(titleText);

            if (control is FrameworkElement element)
            {
                panel.Children.Add(element);
            }

            field.Child = panel;

            return field;
        }

        private Border CreateField(
            string label,
            Control control)
        {
            var panel =
                new StackPanel
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            12
                        )
                };

            panel.Children.Add(
                new TextBlock
                {
                    Text = label,

                    FontFamily =
                        FindResource("Peyda")
                        as FontFamily,

                    FontSize = 12,

                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
    new SolidColorBrush(
        Color.FromRgb(176, 176, 191)
    ),

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            5
                        )
                }
            );

            panel.Children.Add(
                control
            );

            return new Border
            {
                Child = panel
            };
        }

        private Button CreateDialogButton(
            string text,
            bool danger)
        {
            return new Button
            {
                Content = text,

                Width = 125,
                Height = 40,

                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0
                    ),

                FontFamily =
                    FindResource("Peyda")
                    as FontFamily,

                FontSize = 13,

                FontWeight =
                    FontWeights.SemiBold,

                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(danger
                    ? Color.FromRgb(74, 32, 38)
                    : Color.FromRgb(42, 29, 36)),
                Foreground = new SolidColorBrush(danger
                    ? Color.FromRgb(255, 185, 190)
                    : Color.FromRgb(248, 248, 252)),
                BorderBrush = new SolidColorBrush(danger
                    ? Color.FromRgb(219, 60, 69)
                    : Color.FromRgb(79, 48, 58)),
                BorderThickness = new Thickness(1)
            };
        }

        // =========================================================
        // DATE / TIME HELPERS
        // =========================================================

        private static DateTime ParseReservationStart(string dateValue, string timeValue)
        {
            DateTime date = ParseDate(dateValue).Date;
            string normalized = (timeValue ?? string.Empty).Trim()
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3')
                .Replace('۴', '4').Replace('۵', '5').Replace('۶', '6').Replace('۷', '7')
                .Replace('۸', '8').Replace('۹', '9').Replace('٫', ':');
            if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out TimeSpan time))
                return date.Add(time);
            // Backward compatibility for legacy values such as "15".
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour))
                return date.AddHours(Math.Clamp(hour, 0, 23));
            return date;
        }

        private static DateTime ParseDate(
            string value)
        {
            if (
                DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime date
                )
            )
            {
                return date;
            }

            return DateTime.Today;
        }

        private static int ParseWorkHour(
            string? value,
            int fallback)
        {
            if (
                string.IsNullOrWhiteSpace(
                    value
                )
            )
            {
                return fallback;
            }

            if (
                TimeSpan.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    out TimeSpan time
                )
            )
            {
                return Math.Clamp(
                    time.Hours,
                    0,
                    24
                );
            }

            if (
                int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int hour
                )
            )
            {
                return Math.Clamp(
                    hour,
                    0,
                    24
                );
            }

            return fallback;
        }

        private static string
    ToPersianDate(
        DateTime date)
        {
            return JalaliDateHelper.FormatDate(date);
        }

        private static string
     GetFriendlyStatus(
         string? status)
        {
            switch (
                NormalizeStatus(status)
            )
            {
                case "pending":
                    return "در انتظار";

                case "confirmed":
                    return "تأیید شده";

                case "completed":
                    return "تکمیل شده";

                case "cancelled":
                    return "لغو شده";

                case "rejected":
                    return "رد شده";

                case "manual":
                    return "دستی";

                default:
                    return
                        string.IsNullOrWhiteSpace(status)
                            ? "-"
                            : status;
            }
        }
        private static bool
            ContainsText(
                string? source,
                string search)
        {
            if (
                string.IsNullOrWhiteSpace(
                    source
                )
            )
            {
                return false;
            }

            string normalizedSource = NormalizeSearchDigits(source);
            string normalizedSearch = NormalizeSearchDigits(search.Trim());
            return normalizedSource.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSearchDigits(string value)
        {
            return value
                .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2')
                .Replace('۳', '3').Replace('۴', '4').Replace('۵', '5')
                .Replace('۶', '6').Replace('۷', '7').Replace('۸', '8')
                .Replace('۹', '9')
                .Replace('٠', '0').Replace('١', '1').Replace('٢', '2')
                .Replace('٣', '3').Replace('٤', '4').Replace('٥', '5')
                .Replace('٦', '6').Replace('٧', '7').Replace('٨', '8')
                .Replace('٩', '9');
        }

        // =========================================================
        // RESERVATION TYPE
        // =========================================================

        private static bool
            IsManualReservation(
                ReservationItem reservation)
        {
            if (
                reservation == null
            )
            {
                return false;
            }

            if (
                reservation.Id.StartsWith(
                    "manual_",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (
                reservation.Source.Contains(
                    "manual",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (
                string.Equals(
                    NormalizeStatus(
                        reservation.Status
                    ),
                    "manual",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            return false;
        }
    }
}
