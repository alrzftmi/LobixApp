using LobixApp.Models;
using LobixApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace LobixApp.Views
{
    public partial class FloorPlanView : UserControl
    {
        private const double CanvasWidth = 1600;
        private const double CanvasHeight = 1000;
        private const double GridSize = 25;

        private readonly List<PlanItem> _items = new();
        private readonly Stack<List<PlanItem>> _undo = new();
        private readonly Stack<List<PlanItem>> _redo = new();
        private readonly List<ReservationDisplay> _reservations = new();

        private readonly ObservableCollection<ReservationDisplay>
            _selectedReservations = new();

        private readonly ObservableCollection<UnplacedSystem>
            _unplacedSystems = new();

        private readonly LobixApiService _apiService = new();

        private LobixExtendedApiService? _extendedApiService;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private string _layoutPath = string.Empty;
        private string _pendingPath = string.Empty;

        private int _gamenetId;
        private string _gamenetName = "گیم‌نت";
        private string _currentFloor = "همکف";

        private bool _hasApiContext;
        private bool _isInitialized;
        private bool _isLoading;
        private bool _isRendering;
        private bool _isDirty;
        private bool _showGrid = true;
        private bool _isCardsView;
        private bool _isPanning;
        private bool _dragging;
        private bool _systemsLoaded;
        private bool _remoteLayoutLoaded;
        private bool _isSaving;
        private bool _resizing;
        private bool _dragUndoSaved;
        private bool _resizeUndoSaved;

        private Point _dragStartMouse;

        private PlanItem? _resizeItem;
        private FrameworkElement? _resizeVisual;
        private ResizeDirection _resizeDirection;
        private Point _resizeStartMouse;
        private Rect _resizeStartBounds;

        private enum ResizeDirection
        {
            North,
            South,
            East,
            West,
            NorthEast,
            NorthWest,
            SouthEast,
            SouthWest
        }

        private sealed class ResizeHandleInfo
        {
            public string ItemId { get; }
            public ResizeDirection Direction { get; }

            public ResizeHandleInfo(
                string itemId,
                ResizeDirection direction)
            {
                ItemId = itemId;
                Direction = direction;
            }
        }
        private double _zoom = 1;

        private Point _lastMouse;
        private Point _panOrigin;

        private PlanItem? _selected;

        private readonly HashSet<string> _selectedIds = new();
        private readonly Dictionary<string, Point> _dragOrigins = new();

        private CancellationTokenSource? _loadCts;

        public static event Action<int>? LayoutSaved;

        // =====================================================
        // MODELS
        // =====================================================

        private sealed class PlanItem
        {
            public string Id { get; set; } =
                Guid.NewGuid().ToString("N");

            public string Name { get; set; } = "";
            public string Type { get; set; } = "computer";
            public string Status { get; set; } = "free";
            public string Floor { get; set; } = "همکف";

            public string SystemId { get; set; } = "";
            public string SystemNumber { get; set; } = "";

            public int VariationId { get; set; }

            public bool IsPhysicalSystem { get; set; }
            public bool Locked { get; set; }

            public string GroupId { get; set; } = "";

            public double X { get; set; }
            public double Y { get; set; }

            public double W { get; set; } = 110;
            public double H { get; set; } = 75;

            public double Rotation { get; set; }

            public PlanItem Clone()
            {
                return new PlanItem
                {
                    Id = Id,
                    Name = Name,
                    Type = Type,
                    Status = Status,
                    Floor = Floor,

                    SystemId = SystemId,
                    SystemNumber = SystemNumber,
                    VariationId = VariationId,

                    IsPhysicalSystem = IsPhysicalSystem,
                    Locked = Locked,
                    GroupId = GroupId,

                    X = X,
                    Y = Y,
                    W = W,
                    H = H,
                    Rotation = Rotation
                };
            }
        }

        private sealed class ReservationDisplay
        {
            public string Id { get; set; } = "";
            public string Source { get; set; } = "";
            public string Title { get; set; } = "";
            public string Date { get; set; } = "";
            public string StartTime { get; set; } = "";
            public string Customer { get; set; } = "";
            public string Phone { get; set; } = "";
            public string GameName { get; set; } = "";
            public string Status { get; set; } = "";
            public string SystemNumber { get; set; } = "";

            public int VariationId { get; set; }

            public List<int> Hours { get; set; } = new();

            public Brush Color { get; set; } = Brushes.Gray;

            public string DateTimeText =>
                $"{Date}  |  {string.Join("، ",
                    Hours.OrderBy(x => x).Select(x => $"{x:00}:00"))}";
        }

        private sealed class UnplacedSystem
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
        }

        private sealed class LayoutDocument
        {
            public int GamenetId { get; set; }
            public string GamenetName { get; set; } = "";
            public DateTime SavedAt { get; set; }

            public List<PlanItem> Items { get; set; } = new();
        }

        // =====================================================
        // CONSTRUCTORS
        // =====================================================

        public FloorPlanView()
        {
            InitializeComponent();

            string folder = IOPath.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "LobixApp");

            Directory.CreateDirectory(folder);

            _layoutPath = IOPath.Combine(
                folder,
                "floorplan-layout.json");

            _pendingPath = _layoutPath + ".pending";

            InitializeView();
        }

        public FloorPlanView(object? gamenet, object? session) : this()
        {
            _gamenetId = ReadInt(
                gamenet,
                "Id", "id",
                "ProductId", "product_id");

            _gamenetName = ReadString(
                gamenet,
                "Name", "name",
                "Title", "title");

            if (string.IsNullOrWhiteSpace(_gamenetName))
                _gamenetName = $"گیم‌نت {_gamenetId}";

            string token = ReadString(
                session,
                "Token", "token",
                "AccessToken", "access_token");

            if (_gamenetId > 0 &&
                !string.IsNullOrWhiteSpace(token))
            {
                _apiService.SetAuthentication(token);

                _extendedApiService =
                    new LobixExtendedApiService(token);

                _hasApiContext = true;

                string folder = IOPath.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "LobixApp");

                Directory.CreateDirectory(folder);

                // فایل محلی هر گیمنت کاملاً مستقل است.
                _layoutPath = IOPath.Combine(
                    folder,
                    $"floorplan-layout-{_gamenetId}.json");

                _pendingPath = _layoutPath + ".pending";
            }

            if (GamenetTitle != null)
                GamenetTitle.Text = _gamenetName;
        }

        private void InitializeView()
        {
            ReservationList.ItemsSource = _selectedReservations;
            UnplacedSystemsList.ItemsSource = _unplacedSystems;

            MapCanvas.Width = CanvasWidth;
            MapCanvas.Height = CanvasHeight;

            MapCanvas.MouseWheel += MapWheel;
            MapCanvas.MouseLeftButtonDown += MapCanvas_MouseLeftButtonDown;
            MapCanvas.MouseMove += MapCanvas_MouseMove;
            MapCanvas.MouseLeftButtonUp += MapCanvas_MouseLeftButtonUp;
        }

        // =====================================================
        // INITIAL LOAD
        // =====================================================

        private async void FloorPlanView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _isLoading = true;

            FloorPlanLoadingOverlay.Visibility =
                Visibility.Visible;

            try
            {
                _systemsLoaded = false;
                _remoteLayoutLoaded = false;

                LoadLocalLayout();

                if (_hasApiContext)
                {
                    await LoadPhysicalSystemsAsync();

                    await RefreshReservationsAsync();

                    await LoadRemoteLayoutAsync();
                }

                PopulateFloors();
                RenderCurrentView();
                UpdateSummary();
                UpdateDetails();
                UpdateDirtyState();

                ConnectionDot.Fill = _hasApiContext
                    ? Brushes.LimeGreen
                    : Brushes.Gray;

                ConnectionText.Text = _hasApiContext
                    ? "متصل به حساب"
                    : "حالت محلی";
            }
            catch (Exception ex)
            {
                _apiLoadFailed = true;

                Footer.Text =
                    "خطا در بارگذاری اطلاعات گیمنت: " + ex.Message;

                ServerStatus.Text =
                    "دریافت اطلاعات سرور ناموفق بود.";
            }
            finally
            {
                _isLoading = false;

                FloorPlanLoadingOverlay.Visibility =
                    Visibility.Collapsed;
            }
        }

        private bool _apiLoadFailed;

        private void FloorPlanView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
        }

        // =====================================================
        // JSON HELPERS
        // =====================================================

        private static JsonElement FindProperty(
            JsonElement element,
            params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return default;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (names.Any(n =>
                    string.Equals(
                        n,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return property.Value;
                }
            }

            return default;
        }

        private static JsonElement FindObject(
            JsonElement element,
            params string[] names)
        {
            JsonElement result = FindProperty(element, names);

            return result.ValueKind == JsonValueKind.Object
                ? result
                : default;
        }

        private static JsonElement FindArray(
            JsonElement element,
            params string[] names)
        {
            JsonElement result = FindProperty(element, names);

            return result.ValueKind == JsonValueKind.Array
                ? result
                : default;
        }

        private static string FindString(
            JsonElement element,
            params string[] names)
        {
            JsonElement value = FindProperty(element, names);

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            if (value.ValueKind == JsonValueKind.Number)
                return value.ToString();

            if (value.ValueKind == JsonValueKind.True)
                return "true";

            if (value.ValueKind == JsonValueKind.False)
                return "false";

            return "";
        }

        private static int FindInt(
            JsonElement element,
            params string[] names)
        {
            string value = FindString(element, names);

            return int.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out int result)
                ? result
                : 0;
        }

        private static double FindDouble(
            JsonElement element,
            string name,
            double fallback)
        {
            JsonElement value = FindProperty(element, name);

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out double result) &&
                double.IsFinite(result))
            {
                return result;
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    value.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out result) &&
                double.IsFinite(result))
            {
                return result;
            }

            return fallback;
        }

        private static bool FindBool(
            JsonElement element,
            params string[] names)
        {
            JsonElement value = FindProperty(element, names);

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int number))
            {
                return number != 0;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string text = value.GetString() ?? "";

                return text == "1" ||
                       text.Equals(
                           "true",
                           StringComparison.OrdinalIgnoreCase) ||
                       text.Equals(
                           "yes",
                           StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string ReadString(
            object? source,
            params string[] names)
        {
            if (source == null)
                return "";

            foreach (string name in names)
            {
                PropertyInfo? property = source.GetType().GetProperty(
                    name,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null)
                    continue;

                object? value = property.GetValue(source);

                if (value != null)
                {
                    return Convert.ToString(
                        value,
                        CultureInfo.InvariantCulture) ?? "";
                }
            }

            return "";
        }

        private static int ReadInt(
            object? source,
            params string[] names)
        {
            string value = ReadString(source, names);

            return int.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out int result)
                ? result
                : 0;
        }

        // =====================================================
        // PHYSICAL SYSTEMS API
        // =====================================================

        private async Task LoadPhysicalSystemsAsync()
        {
            if (!_hasApiContext ||
                _gamenetId <= 0 ||
                _extendedApiService == null)
            {
                throw new InvalidOperationException(
                    "اطلاعات اتصال یا شناسه گیمنت معتبر نیست.");
            }

            _systemsLoaded = false;

            using JsonDocument response =
                await _extendedApiService.GetSystemsAsync(_gamenetId);

            JsonElement root = response.RootElement;

            int responseId = FindInt(
                root,
                "product_id",
                "gamenet_id",
                "productId",
                "gamenetId");

            if (responseId != _gamenetId)
            {
                throw new InvalidOperationException(
                    $"شناسه گیمنت پاسخ سیستم‌ها معتبر نیست. " +
                    $"درخواست: {_gamenetId} | پاسخ: {responseId}");
            }

            JsonElement systemsElement =
                FindArray(root, "physical_systems");

            if (systemsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "آرایه physical_systems در پاسخ سرور وجود ندارد.");
            }

            var incoming = new List<PlanItem>();

            foreach (JsonElement system in systemsElement.EnumerateArray())
            {
                string systemId = FindString(system, "system_id");

                string number = FindString(
                    system,
                    "number",
                    "system_number");

                int variationId = FindInt(
                    system,
                    "variation_id");

                string category = FindString(
                    system,
                    "category",
                    "name",
                    "title");

                bool active = FindBool(system, "active");

                if (string.IsNullOrWhiteSpace(systemId))
                    continue;

                string displayName =
                    !string.IsNullOrWhiteSpace(category)
                        ? $"{category} #{number}"
                        : $"سیستم #{number}";

                if (string.IsNullOrWhiteSpace(number))
                {
                    displayName = string.IsNullOrWhiteSpace(category)
                        ? $"سیستم {systemId}"
                        : $"{category} {systemId}";
                }

                string type = InferType(
                    displayName,
                    new Dictionary<int, string>(),
                    variationId);

                incoming.Add(new PlanItem
                {
                    Id = "physical-" + systemId,

                    SystemId = systemId,
                    SystemNumber = number,
                    VariationId = variationId,

                    Name = displayName,
                    Type = type,
                    Status = active ? "free" : "offline",
                    Floor = "همکف",

                    IsPhysicalSystem = true,

                    X = 50,
                    Y = 50,

                    W = type == "console" ? 125 : 110,
                    H = 75
                });
            }

            if (incoming.Count == 0)
            {
                throw new InvalidDataException(
                    "سرور فهرست خالی سیستم‌های فیزیکی برگردانده است.");
            }

            // چاپ سیستم‌های دریافتی از سرور
            System.Diagnostics.Debug.WriteLine("===== INCOMING API SYSTEMS =====");

            foreach (var item in incoming)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"API | Id={item.Id} | SystemId={item.SystemId} | " +
                    $"Name={item.Name} | Number={item.SystemNumber} | " +
                    $"VariationId={item.VariationId}");
            }

            ReconcilePhysicalSystems(incoming);

            // چاپ سیستم‌های نهایی نقشه
            System.Diagnostics.Debug.WriteLine("===== FINAL PHYSICAL SYSTEMS =====");

            foreach (var item in _items.Where(x => x.IsPhysicalSystem))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"MAP | Id={item.Id} | SystemId={item.SystemId} | " +
                    $"Name={item.Name} | Number={item.SystemNumber} | " +
                    $"VariationId={item.VariationId}");
            }

            _systemsLoaded = true;

            UpdateUnplacedSystems();

            ServerStatus.Text =
                $"سیستم‌های گیمنت {_gamenetId} دریافت شدند: {incoming.Count}";
        }


        private void ReconcilePhysicalSystems(
            List<PlanItem> incoming)
        {
            // =========================================
            // 1. ذخیره سیستم‌های فیزیکی و آیتم‌های دستی
            // =========================================

            List<PlanItem> oldPhysical = _items
                .Where(x => x.IsPhysicalSystem)
                .ToList();

            List<PlanItem> manualItems = _items
                .Where(x => !x.IsPhysicalSystem)
                .ToList();

            List<PlanItem> reconciled = new();

            List<PlanItem> newSystems = new();

            // جلوگیری از تطبیق چندباره یک سیستم قدیمی
            HashSet<PlanItem> matchedOldSystems = new();

            // =========================================
            // 2. شناسایی سیستم‌های قبلی و جدید
            // =========================================

            foreach (PlanItem fresh in incoming)
            {
                PlanItem? old = oldPhysical.FirstOrDefault(x =>
                    !matchedOldSystems.Contains(x) &&
                    !string.IsNullOrWhiteSpace(x.SystemId) &&
                    !string.IsNullOrWhiteSpace(fresh.SystemId) &&
                    string.Equals(
                        x.SystemId.Trim(),
                        fresh.SystemId.Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (old != null)
                {
                    // سیستم قبلاً وجود داشته است.
                    // موقعیت و تنظیمات نقشه از نسخه قبلی حفظ می‌شود.

                    matchedOldSystems.Add(old);

                    PlanItem preserved = old.Clone();

                    // اطلاعات هویتی و اطلاعات به‌روز API
                    preserved.Id = fresh.Id;
                    preserved.SystemId = fresh.SystemId;
                    preserved.SystemNumber = fresh.SystemNumber;
                    preserved.VariationId = fresh.VariationId;

                    preserved.Name = fresh.Name;
                    preserved.Type = fresh.Type;
                    preserved.Status = fresh.Status;

                    preserved.IsPhysicalSystem = true;

                    // X و Y و ابعاد و تنظیمات چیدمان
                    // از نسخه قبلی حفظ می‌شوند.

                    reconciled.Add(preserved);
                }
                else
                {
                    // سیستم جدید است.
                    // فعلاً جانمایی نمی‌شود تا ابتدا
                    // سیستم‌های قبلی و آیتم‌های دستی
                    // به عنوان مانع ثبت شوند.

                    fresh.IsPhysicalSystem = true;

                    newSystems.Add(fresh);
                }
            }

            // =========================================
            // 3. ساخت نقشه با آیتم‌های دستی و قبلی
            // =========================================

            _items.Clear();

            // آیتم‌های دستی حفظ می‌شوند.
            _items.AddRange(manualItems);

            // سیستم‌های فیزیکی قبلی با موقعیت قبلی
            // به نقشه برمی‌گردند.
            _items.AddRange(reconciled);

            // =========================================
            // 4. جانمایی سیستم‌های جدید
            // =========================================

            foreach (PlanItem item in newSystems)
            {
                // سیستم جدید باید با در نظر گرفتن تمام
                // آیتم‌هایی که تاکنون روی نقشه هستند
                // در فضای آزاد قرار بگیرد.

                if (!TryPlaceItem(item))
                {
                    // اگر فضای آزاد پیدا نشد،
                    // سیستم به عنوان جانمایی‌نشده باقی می‌ماند.

                    item.X = -1;
                    item.Y = -1;
                }

                // بعد از جانمایی، به نقشه اضافه می‌شود
                // تا مانع سیستم جدید بعدی باشد.

                _items.Add(item);
            }

            // =========================================
            // 5. پاک‌سازی انتخاب‌های نامعتبر
            // =========================================

            HashSet<string> validIds = _items
                .Select(x => x.Id)
                .ToHashSet();

            _selectedIds.RemoveWhere(id =>
                !validIds.Contains(id));

            if (_selected != null &&
                !validIds.Contains(_selected.Id))
            {
                _selected = null;
            }

            // =========================================
            // 6. به‌روزرسانی سیستم‌های جانمایی‌نشده
            // =========================================

            UpdateUnplacedSystems();
        }

        private double FindNextPositionX(List<PlanItem> current)
        {
            double x = 50;

            if (current.Count > 0)
                x = current.Max(i => i.X + i.W) + 30;

            return x > CanvasWidth - 150 ? 50 : x;
        }

        private double FindNextPositionY(List<PlanItem> current)
        {
            if (current.Count == 0)
                return 50;

            double x = FindNextPositionX(current);

            if (x <= 60)
                return current.Max(i => i.Y + i.H) + 30;

            return 50;
        }


        private void UpdateUnplacedSystems()
        {
            _unplacedSystems.Clear();

            foreach (PlanItem item in _items.Where(x =>
                x.IsPhysicalSystem))
            {
                bool invalidPosition =
                    item.X < 0 ||
                    item.Y < 0 ||
                    item.X < LayoutMargin ||
                    item.Y < LayoutMargin ||
                    item.X + item.W > CanvasWidth - LayoutMargin ||
                    item.Y + item.H > CanvasHeight - LayoutMargin;

                if (!invalidPosition)
                    continue;

                _unplacedSystems.Add(new UnplacedSystem
                {
                    Id = item.Id,
                    Name = item.Name
                });
            }

            UnplacedCountText.Text =
                $"{_unplacedSystems.Count} سیستم";
        }


        // =====================================================
        // RESERVATIONS
        // =====================================================

        private async Task RefreshReservationsAsync()
        {
            if (!_hasApiContext || _gamenetId <= 0)
                return;

            try
            {
                string from = DateTime.Today.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

                string to = DateTime.Today.AddDays(30).ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

                GamenetSyncResponse sync =
                    await _apiService.SyncAsync(
                        _gamenetId,
                        from,
                        to);

                if (sync?.Data == null)
                    return;

                if (sync.Data.ProductId != _gamenetId)
                {
                    throw new InvalidOperationException(
                        "اطلاعات رزرو دریافتی متعلق به گیمنت دیگری است.");
                }

                BuildReservationList(sync);

                ApplyCurrentReservationStatuses();

                // رسم مجدد نقشه با وضعیت‌های جدید سیستم‌ها
                RenderCurrentView();

                LastSyncText.Text =
                    "آخرین همگام‌سازی: " +
                    DateTime.Now.ToString("HH:mm:ss");

                ConnectionDot.Fill = Brushes.LimeGreen;
                ConnectionText.Text = "همگام‌سازی موفق";
            }
            catch (Exception ex)
            {
                ConnectionDot.Fill = Brushes.Orange;
                ConnectionText.Text = "اطلاعات ذخیره‌شده / آفلاین";

                Footer.Text =
                    "همگام‌سازی رزروها انجام نشد: " + ex.Message;
            }
        }


        private void BuildReservationList(GamenetSyncResponse sync)
        {
            _reservations.Clear();

            if (sync?.Data == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[LOBIX] BuildReservationList: Sync data is null.");

                return;
            }

            // =========================================
            // ONLINE BOOKINGS
            // =========================================

            foreach (OnlineBooking booking in
                sync.Data.OnlineBookings ?? new List<OnlineBooking>())
            {
                if (booking == null || booking.OrderId <= 0)
                    continue;

                string orderStatus = (booking.Status ?? "")
                    .Trim()
                    .ToLowerInvariant();

                string operatorStatus = (booking.OperatorStatus ?? "")
                    .Trim()
                    .ToLowerInvariant();

                // Debug: وضعیت واقعی سفارش دریافتی از API
                System.Diagnostics.Debug.WriteLine(
                    $"REJECT_CHECK | ORDER={booking.OrderId} | " +
                    $"STATUS={orderStatus} | OPERATOR={operatorStatus} | " +
                    $"ITEM={booking.ItemId} | SYSTEM={booking.SystemNumber} | " +
                    $"DATE={booking.DateIso} | TIME={booking.StartTime}");

                // سفارش‌های غیرقابل رزرو
                if (orderStatus == "cancelled" ||
                    orderStatus == "canceled" ||
                    orderStatus == "failed" ||
                    orderStatus == "refunded")
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LOBIX SKIP] Order {booking.OrderId}: " +
                        $"WooCommerce status = {orderStatus}");

                    continue;
                }

                // سفارش ردشده توسط اپراتور
                if (operatorStatus == "rejected")
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LOBIX SKIP] Order {booking.OrderId}: " +
                        $"Operator rejected | Item={booking.ItemId}");

                    continue;
                }

                int variation = booking.VariationId > 0
                    ? booking.VariationId
                    : booking.VarId;

                int start = ParseHour(booking.StartTime);

                if (start < 0 || start > 23)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LOBIX SKIP] Invalid start time: " +
                        $"Order={booking.OrderId}, Time={booking.StartTime}");

                    continue;
                }

                int duration = Math.Clamp(booking.Hours, 0, 24);

                if (duration <= 0)
                    continue;

                List<int> hours = Enumerable
                    .Range(0, duration)
                    .Select(i => start + i)
                    .Where(hour => hour >= 0 && hour <= 23)
                    .Distinct()
                    .OrderBy(hour => hour)
                    .ToList();

                if (hours.Count == 0)
                    continue;

                // هر آیتم سفارش شناسه مستقل دارد.
                string reservationId = booking.ItemId > 0
                    ? $"online:{booking.OrderId}:{booking.ItemId}"
                    : $"online:{booking.OrderId}:{variation}:{booking.StartTime}";

                ReservationDisplay reservation = new ReservationDisplay
                {
                    Id = reservationId,
                    Source = "online",

                    Title = $"رزرو آنلاین #{booking.OrderId}",

                    Date = NormalizeDate(booking.DateIso),
                    StartTime = booking.StartTime,

                    Customer = booking.Customer,
                    Phone = booking.Phone,

                    GameName = booking.GameName,

                    Status = booking.Status,

                    SystemNumber = booking.SystemNumber,
                    VariationId = variation,

                    Hours = hours,

                    Color = GetReservationColor(reservationId)
                };

                _reservations.Add(reservation);
            }

            // =========================================
            // MANUAL BOOKINGS
            // =========================================

            foreach (ManualBooking booking in
                sync.Data.ManualBookings ?? new List<ManualBooking>())
            {
                if (booking == null)
                    continue;

                if (string.IsNullOrWhiteSpace(booking.Id))
                    continue;

                string manualDate = NormalizeDate(booking.Date);

                foreach (ManualBookingSystem system in
                    booking.Systems ?? new List<ManualBookingSystem>())
                {
                    if (system == null)
                        continue;

                    List<int> hours = (system.Hours ?? new List<string>())
                        .Select(ParseHour)
                        .Where(hour => hour >= 0 && hour <= 23)
                        .Distinct()
                        .OrderBy(hour => hour)
                        .ToList();

                    if (hours.Count == 0)
                        continue;

                    string reservationId =
                        $"manual:{booking.Id}:{system.SystemNumber}:{system.VariationId}";

                    ReservationDisplay reservation = new ReservationDisplay
                    {
                        Id = reservationId,
                        Source = "manual",

                        Title = "رزرو دستی",

                        Date = manualDate,

                        Customer = booking.Customer,
                        Phone = booking.Phone,

                        GameName = system.GameName,

                        Status = "manual",

                        SystemNumber = system.SystemNumber,
                        VariationId = system.VariationId,

                        Hours = hours,

                        Color = GetReservationColor(reservationId)
                    };

                    _reservations.Add(reservation);
                }
            }

            // =========================================
            // SORT RESERVATIONS
            // =========================================

            _reservations.Sort((a, b) =>
            {
                int result = string.CompareOrdinal(a.Date, b.Date);

                if (result != 0)
                    return result;

                result = string.CompareOrdinal(a.StartTime, b.StartTime);

                if (result != 0)
                    return result;

                return string.CompareOrdinal(a.Id, b.Id);
            });

            // =========================================
            // FINAL DEBUG
            // =========================================

            System.Diagnostics.Debug.WriteLine(
                "========== FINAL RESERVATIONS ==========");

            foreach (ReservationDisplay reservation in _reservations)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"FINAL_RESERVATION | " +
                    $"ID={reservation.Id} | " +
                    $"SOURCE={reservation.Source} | " +
                    $"TITLE={reservation.Title} | " +
                    $"DATE={reservation.Date} | " +
                    $"TIME={reservation.StartTime} | " +
                    $"SYSTEM={reservation.SystemNumber} | " +
                    $"VARIATION={reservation.VariationId} | " +
                    $"HOURS={string.Join(",", reservation.Hours)}");
            }

            System.Diagnostics.Debug.WriteLine(
                $"TOTAL RESERVATIONS: {_reservations.Count}");

            System.Diagnostics.Debug.WriteLine(
                "=========================================");
        }

        private void ApplyCurrentReservationStatuses()
        {
            DateTime now = DateTime.Now;

            string today = now.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

            foreach (PlanItem item in _items.Where(
                x => x.IsPhysicalSystem))
            {
                if (string.Equals(
                    item.Status,
                    "offline",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool active = _reservations.Any(r =>
                    ReservationMatchesItem(r, item) &&
                    r.Date == today &&
                    r.Hours.Contains(now.Hour));

                item.Status = active ? "reserved" : "free";
            }
        }



        private bool ReservationMatchesItem(
            ReservationDisplay reservation,
            PlanItem item)
        {
            if (string.IsNullOrWhiteSpace(item.SystemNumber) ||
                string.IsNullOrWhiteSpace(reservation.SystemNumber))
            {
                return false;
            }

            // شناسه تنوع باید دقیقاً برابر باشد.
            if (reservation.VariationId != item.VariationId)
                return false;

            // استخراج شماره انتهایی سیستم
            // مثال: Cas-1 => 1
            // مثال: Casual-7 => 7
            // مثال: VIP-15 => 15

            static string NormalizeSystemNumber(string value)
            {
                value = value.Trim();

                int separatorIndex = value.LastIndexOf('-');

                if (separatorIndex >= 0 &&
                    separatorIndex < value.Length - 1)
                {
                    string suffix = value[(separatorIndex + 1)..].Trim();

                    if (int.TryParse(
                        suffix,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int number))
                    {
                        return number.ToString(
                            CultureInfo.InvariantCulture);
                    }
                }

                if (int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int directNumber))
                {
                    return directNumber.ToString(
                        CultureInfo.InvariantCulture);
                }

                return value;
            }

            string itemNumber =
                NormalizeSystemNumber(item.SystemNumber);

            string reservationNumber =
                NormalizeSystemNumber(reservation.SystemNumber);

            return string.Equals(
                itemNumber,
                reservationNumber,
                StringComparison.OrdinalIgnoreCase);
        }



        private static int ParseHour(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return -1;

            string text = value.Trim();

            if (text.Contains(':'))
                text = text.Split(':')[0];

            if (int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int hour))
            {
                return hour;
            }

            if (int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out hour))
            {
                return hour;
            }

            return -1;
        }

        private static string NormalizeDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date))
            {
                return date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);
            }

            return value.Length >= 10
                ? value.Substring(0, 10)
                : value;
        }

        private static Brush GetReservationColor(string id)
        {
            unchecked
            {
                int hash = 17;

                foreach (char c in id)
                    hash = hash * 31 + c;

                byte r = (byte)(90 + Math.Abs(hash % 140));
                byte g = (byte)(75 + Math.Abs((hash >> 8) % 145));
                byte b = (byte)(80 + Math.Abs((hash >> 16) % 145));

                return new SolidColorBrush(
                    Color.FromRgb(r, g, b));
            }
        }

        private void UpdateSelectedReservations()
        {
            _selectedReservations.Clear();

            if (_selected == null ||
                !_selected.IsPhysicalSystem)
            {
                ReservationHint.Text =
                    "یک سیستم فیزیکی را انتخاب کن.";

                return;
            }

            var matching = _reservations
                .Where(r => ReservationMatchesItem(r, _selected))
                .OrderBy(r => r.Date)
                .ThenBy(r => r.StartTime)
                .ToList();

            foreach (ReservationDisplay reservation in matching)
                _selectedReservations.Add(reservation);

            ReservationHint.Text = matching.Count == 0
                ? "برای این سیستم رزروی ثبت نشده است."
                : $"{matching.Count} مورد رزرو برای این سیستم";
        }

        // =====================================================
        // TYPE / STATUS HELPERS
        // =====================================================

        private static string InferType(
            string name,
            Dictionary<int, string> variations,
            int variationId)
        {
            string source = name;

            if (variations.TryGetValue(
                variationId,
                out string? variation))
            {
                source += " " + variation;
            }

            source = source.ToLowerInvariant();

            if (source.Contains("console") ||
                source.Contains("ps5") ||
                source.Contains("xbox") ||
                source.Contains("کنسول"))
            {
                return "console";
            }

            return "computer";
        }

        private static string NormalizeStatus(string status)
        {
            string value = (status ?? "").Trim().ToLowerInvariant();

            if (value.Contains("offline") ||
                value.Contains("disable") ||
                value.Contains("خاموش"))
                return "offline";

            if (value.Contains("busy") ||
                value.Contains("occupied") ||
                value.Contains("استفاده"))
                return "occupied";

            if (value.Contains("reserve") ||
                value.Contains("رزرو"))
                return "reserved";

            if (value.Contains("instock") ||
                value.Contains("available") ||
                value.Contains("free") ||
                value.Contains("آزاد"))
                return "free";

            return "unknown";
        }

        // =====================================================
        // LOCAL LAYOUT
        // =====================================================


        private void LoadLocalLayout()
        {
            if (!File.Exists(_layoutPath))
                return;

            try
            {
                string json = File.ReadAllText(
                    _layoutPath,
                    Encoding.UTF8);

                LayoutDocument document =
                    ParseLayoutDocument(json);

                if (document.Items == null)
                {
                    throw new InvalidDataException(
                        "فهرست آیتم‌های نقشه محلی معتبر نیست.");
                }

                if (_hasApiContext &&
                    document.GamenetId != 0 &&
                    document.GamenetId != _gamenetId)
                {
                    throw new InvalidDataException(
                        "شناسه گیمنت فایل محلی با گیمنت فعلی مطابقت ندارد.");
                }

                _items.Clear();

                foreach (PlanItem item in document.Items)
                {
                    if (!IsValidItem(item))
                        continue;

                    // ابعاد غیرمعتبر را وارد Canvas نمی‌کنیم.
                    item.W = Math.Clamp(
                        item.W,
                        10,
                        CanvasWidth - LayoutMargin * 2);

                    item.H = Math.Clamp(
                        item.H,
                        10,
                        CanvasHeight - LayoutMargin * 2);

                    if (item.X < 0 || item.Y < 0)
                    {
                        item.X = -1;
                        item.Y = -1;
                    }
                    else
                    {
                        item.X = Math.Clamp(
                            item.X,
                            LayoutMargin,
                            CanvasWidth - LayoutMargin - item.W);

                        item.Y = Math.Clamp(
                            item.Y,
                            LayoutMargin,
                            CanvasHeight - LayoutMargin - item.H);
                    }

                    _items.Add(item);
                }

                UpdateUnplacedSystems();
            }
            catch (Exception ex)
            {
                // در صورت خطا فایل اصلی را پاک یا بازنویسی نکن.
                MessageBox.Show(
                    "خطا در خواندن نقشه محلی:\n" + ex.Message +
                    "\n\nفایل اصلی دست‌نخورده باقی مانده است.",
                    "خطای نقشه محلی",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void SaveLocalLayout()
        {
            string? folder =
                IOPath.GetDirectoryName(_layoutPath);

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException(
                    "مسیر ذخیره‌سازی معتبر نیست.");
            }

            Directory.CreateDirectory(folder);

            var document = new LayoutDocument
            {
                GamenetId = _hasApiContext ? _gamenetId : 0,

                GamenetName = _gamenetName,

                SavedAt = DateTime.Now,

                Items = _items
                    .Select(x => x.Clone())
                    .ToList()
            };

            string json = JsonSerializer.Serialize(
                document,
                _jsonOptions);

            string temp = _layoutPath + ".tmp";

            File.WriteAllText(
                temp,
                json,
                Encoding.UTF8);

            if (File.Exists(_layoutPath))
            {
                File.Replace(temp, _layoutPath, null);
            }
            else
            {
                File.Move(temp, _layoutPath);
            }

            _isDirty = false;

            UpdateDirtyState();
        }

        // =====================================================
        // REMOTE LAYOUT LOAD
        // =====================================================

        private async Task LoadRemoteLayoutAsync()
        {
            if (!_hasApiContext ||
                _gamenetId <= 0 ||
                _extendedApiService == null)
            {
                return;
            }

            if (!_systemsLoaded)
            {
                throw new InvalidOperationException(
                    "قبل از دریافت نقشه باید سیستم‌های فیزیکی دریافت شوند.");
            }

            _remoteLayoutLoaded = false;

            // تغییرات محلی ارسال‌نشده نباید با نقشه سرور جایگزین شوند.
            if (File.Exists(_pendingPath))
            {
                ServerStatus.Text =
                    "تغییرات محلی این گیمنت در انتظار همگام‌سازی هستند.";

                return;
            }

            using JsonDocument response =
                await _extendedApiService.GetLayoutAsync(_gamenetId);

            JsonElement root = response.RootElement;

            int responseId = FindInt(
                root,
                "product_id",
                "gamenet_id",
                "productId",
                "gamenetId");

            if (responseId != _gamenetId)
            {
                throw new InvalidOperationException(
                    $"نقشه دریافتی متعلق به گیمنت دیگری است. " +
                    $"درخواست: {_gamenetId} | پاسخ: {responseId}");
            }

            JsonElement layout = FindObject(root, "layout");

            if (layout.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "ساختار layout در پاسخ سرور معتبر نیست.");
            }

            JsonElement objects = FindArray(layout, "objects");

            if (objects.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "آرایه layout.objects در پاسخ سرور وجود ندارد.");
            }

            var physicalBySystemId = _items
                .Where(x =>
                    x.IsPhysicalSystem &&
                    !string.IsNullOrWhiteSpace(x.SystemId))
                .ToDictionary(
                    x => x.SystemId,
                    x => x,
                    StringComparer.OrdinalIgnoreCase);

            var remoteManualItems = new List<PlanItem>();

            var matchedPhysicalIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (JsonElement obj in objects.EnumerateArray())
            {
                string remoteId = FindString(obj, "id");

                string remoteType = FindString(obj, "type");

                double x = FindDouble(obj, "x", 50);
                double y = FindDouble(obj, "y", 50);

                double width = FindDouble(obj, "width", 110);
                double height = FindDouble(obj, "height", 75);

                double rotation = FindDouble(obj, "rotation", 0);

                string systemId = FindString(obj, "system_id");

                if (string.IsNullOrWhiteSpace(remoteType))
                    continue;

                if (remoteType.Equals(
                    "system",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(systemId))
                        continue;

                    if (!physicalBySystemId.TryGetValue(
                        systemId,
                        out PlanItem? physical))
                    {
                        continue;
                    }

                    physical.W = Math.Clamp(width, 10, 2000);
                    physical.H = Math.Clamp(height, 10, 2000);

                    physical.X = Math.Clamp(
                        x,
                        0,
                        CanvasWidth - physical.W);

                    physical.Y = Math.Clamp(
                        y,
                        0,
                        CanvasHeight - physical.H);

                    physical.Rotation = rotation;

                    string floor = FindString(obj, "floor");

                    if (!string.IsNullOrWhiteSpace(floor))
                        physical.Floor = floor;

                    matchedPhysicalIds.Add(systemId);

                    continue;
                }

                // اشیای دستی و بخش‌ها
                string type = remoteType.ToLowerInvariant() switch
                {
                    "section" => "area",
                    "area" => "area",
                    "wall" => "wall",
                    "door" => "door",
                    "label" => "label",
                    "desk" => "desk",
                    "decoration" => "decoration",
                    _ => "area"
                };

                string itemName =
                    FindString(obj, "label", "name");

                string itemFloor =
                    FindString(obj, "floor");

                var manual = new PlanItem
                {
                    Id = string.IsNullOrWhiteSpace(remoteId)
                        ? Guid.NewGuid().ToString("N")
                        : remoteId,

                    Name = string.IsNullOrWhiteSpace(itemName)
                        ? (type == "area" ? "بخش" : "آیتم")
                        : itemName,

                    Type = type,

                    Status = "free",

                    Floor = string.IsNullOrWhiteSpace(itemFloor)
                        ? "همکف"
                        : itemFloor,

                    X = x,
                    Y = y,

                    W = width,
                    H = height,

                    Rotation = rotation,

                    IsPhysicalSystem = false
                };

                if (IsValidItem(manual))
                    remoteManualItems.Add(manual);
            }

            // سیستم‌های فیزیکی که در نقشه نیستند حذف نمی‌شوند.
            // فقط اشیای دستی از نسخه سرور جایگزین می‌شوند.
            _items.RemoveAll(x => !x.IsPhysicalSystem);

            _items.AddRange(remoteManualItems);

            _remoteLayoutLoaded = true;

            _isDirty = false;

            SaveLocalLayout();

            UpdateUnplacedSystems();

            ServerStatus.Text =
                $"نقشه گیمنت {_gamenetId} دریافت شد؛ " +
                $"{matchedPhysicalIds.Count} سیستم در نقشه قرار دارند.";
        }

        // =====================================================
        // REMOTE LAYOUT SAVE
        // =====================================================

        private async Task<bool> SaveLayoutToServerAsync()
        {
            if (!_hasApiContext ||
                _gamenetId <= 0 ||
                _extendedApiService == null)
            {
                return false;
            }

            if (!_systemsLoaded)
            {
                ServerStatus.Text =
                    "ذخیره متوقف شد؛ فهرست سیستم‌های این گیمنت تأیید نشده است.";

                return false;
            }

            if (_isSaving)
                return false;

            _isSaving = true;

            try
            {
                var objects = _items.Select(item => new
                {
                    id = item.Id,

                    type = item.IsPhysicalSystem
                        ? "system"
                        : item.Type == "area"
                            ? "section"
                            : item.Type,

                    x = item.X,
                    y = item.Y,

                    width = item.W,
                    height = item.H,

                    rotation = item.Rotation,

                    label = item.Name,

                    system_id = item.IsPhysicalSystem
                        ? item.SystemId
                        : null,

                    floor = item.Floor
                }).ToList();

                var layout = new
                {
                    version = 1,
                    width = CanvasWidth,
                    height = CanvasHeight,
                    objects
                };

                using JsonDocument response =
                    await _extendedApiService.SaveLayoutAsync(
                        _gamenetId,
                        layout);

                JsonElement root = response.RootElement;

                int responseId = FindInt(
                    root,
                    "product_id",
                    "gamenet_id",
                    "productId",
                    "gamenetId");

                if (responseId > 0 &&
                    responseId != _gamenetId)
                {
                    throw new InvalidOperationException(
                        "پاسخ ذخیره‌سازی متعلق به گیمنت دیگری است.");
                }

                JsonElement successElement =
                    FindProperty(root, "success");

                if (successElement.ValueKind ==
                    JsonValueKind.False)
                {
                    throw new InvalidOperationException(
                        FindString(root, "message", "error"));
                }

                if (File.Exists(_pendingPath))
                    File.Delete(_pendingPath);

                _isDirty = false;

                UpdateDirtyState();

                ServerStatus.Text =
                    $"نقشه گیمنت {_gamenetId} روی سرور ذخیره شد.";

                return true;
            }
            catch (Exception ex)
            {
                File.WriteAllText(
                    _pendingPath,
                    DateTime.Now.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                ServerStatus.Text =
                    "ذخیره محلی؛ ارسال به سرور ناموفق بود.";

                Footer.Text =
                    "خطای ذخیره نقشه: " + ex.Message;

                return false;
            }
            finally
            {
                _isSaving = false;
            }
        }

        private static bool IsValidItem(PlanItem item)
        {
            return !string.IsNullOrWhiteSpace(item.Id) &&
                   double.IsFinite(item.X) &&
                   double.IsFinite(item.Y) &&
                   double.IsFinite(item.W) &&
                   double.IsFinite(item.H) &&
                   double.IsFinite(item.Rotation) &&
                   item.W >= 10 &&
                   item.H >= 10 &&
                   item.W <= 2000 &&
                   item.H <= 2000;
        }

        // =====================================================
        // SAVE / RELOAD / SYNC
        // =====================================================

        private async void Save_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (_hasApiContext && !_systemsLoaded)
                {
                    MessageBox.Show(
                        "سیستم‌های گیمنت هنوز از سرور تأیید نشده‌اند. ذخیره متوقف شد.",
                        "Lobix",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                SaveLocalLayout();

                bool remoteSaved = false;

                if (_hasApiContext)
                    remoteSaved = await SaveLayoutToServerAsync();

                if (remoteSaved && _gamenetId > 0)
                    LayoutSaved?.Invoke(_gamenetId);

                Footer.Text = remoteSaved || !_hasApiContext
                    ? "نقشه ذخیره شد."
                    : "نقشه محلی ذخیره شد؛ همگام‌سازی سرور ناموفق بود.";
            }
            catch (Exception ex)
            {
                Footer.Text = "خطا در ذخیره: " + ex.Message;
            }
        }

        private async void Reload_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_isDirty)
            {
                MessageBoxResult result = MessageBox.Show(
                    "تغییرات ذخیره‌نشده از بین می‌روند. ادامه می‌دهی؟",
                    "بارگذاری مجدد",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            _isLoading = true;

            FloorPlanLoadingOverlay.Visibility =
                Visibility.Visible;

            try
            {
                _items.Clear();

                _selected = null;
                _selectedIds.Clear();

                _systemsLoaded = false;
                _remoteLayoutLoaded = false;

                _undo.Clear();
                _redo.Clear();

                LoadLocalLayout();

                if (_hasApiContext)
                {
                    await LoadPhysicalSystemsAsync();

                    await RefreshReservationsAsync();

                    await LoadRemoteLayoutAsync();
                }

                PopulateFloors();
                RenderCurrentView();
                UpdateSummary();
                UpdateDetails();
                UpdateDirtyState();

                Footer.Text = "نقشه به‌روزرسانی شد.";
            }
            catch (Exception ex)
            {
                Footer.Text =
                    "خطا در بارگذاری مجدد: " + ex.Message;

                ServerStatus.Text =
                    "بارگذاری اطلاعات سرور ناموفق بود.";
            }
            finally
            {
                _isLoading = false;

                FloorPlanLoadingOverlay.Visibility =
                    Visibility.Collapsed;
            }
        }

        private async void SyncSystems_Click(
            object sender,
            RoutedEventArgs e)
        {
            FloorPlanLoadingOverlay.Visibility =
                Visibility.Visible;

            try
            {
                if (!_hasApiContext || _gamenetId <= 0)
                {
                    throw new InvalidOperationException(
                        "اتصال معتبر به حساب گیمنت وجود ندارد.");
                }

                _systemsLoaded = false;
                _remoteLayoutLoaded = false;

                await LoadPhysicalSystemsAsync();

                await RefreshReservationsAsync();

                await LoadRemoteLayoutAsync();

                PopulateFloors();
                RenderCurrentView();
                UpdateSummary();
                UpdateDetails();

                SaveLocalLayout();

                bool saved = await SaveLayoutToServerAsync();

                if (saved)
                {
                    LayoutSaved?.Invoke(_gamenetId);

                    Footer.Text =
                        "سیستم‌ها و نقشه با موفقیت همگام‌سازی شدند.";
                }
                else
                {
                    Footer.Text =
                        "سیستم‌ها دریافت شدند، اما ذخیره نقشه روی سرور موفق نبود.";
                }
            }
            catch (Exception ex)
            {
                Footer.Text =
                    "خطا در همگام‌سازی: " + ex.Message;
            }
            finally
            {
                FloorPlanLoadingOverlay.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void UpdateDirtyState()
        {
            DirtyText.Text = _isDirty
                ? "تغییرات ذخیره‌نشده"
                : "تغییرات ذخیره شده";

            DirtyText.Foreground = _isDirty
                ? Brushes.Orange
                : Brushes.LightGreen;
        }

        private void MarkDirty()
        {
            if (_isLoading || _isRendering)
                return;

            _isDirty = true;

            UpdateDirtyState();
        }

        // =====================================================
        // UNDO / REDO
        // =====================================================

        private void SaveUndo()
        {
            _undo.Push(
                _items.Select(x => x.Clone()).ToList());

            if (_undo.Count > 100)
            {
                var temp = _undo
                    .Reverse()
                    .Take(100)
                    .Reverse()
                    .ToList();

                _undo.Clear();

                foreach (var item in temp)
                    _undo.Push(item);
            }

            _redo.Clear();
        }

        private void Undo_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_undo.Count == 0)
                return;

            _redo.Push(
                _items.Select(x => x.Clone()).ToList());

            RestoreItems(_undo.Pop());
        }

        private void Redo_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_redo.Count == 0)
                return;

            _undo.Push(
                _items.Select(x => x.Clone()).ToList());

            RestoreItems(_redo.Pop());
        }

        private void RestoreItems(List<PlanItem> snapshot)
        {
            _items.Clear();

            _items.AddRange(
                snapshot.Select(x => x.Clone()));

            MarkDirty();

            PopulateFloors();
            RenderCurrentView();
            UpdateSummary();
            UpdateDetails();
        }

        // =====================================================
        // FLOORS / FILTERS
        // =====================================================

        private void PopulateFloors()
        {
            string selected = _currentFloor;

            var floors = _items
                .Select(x =>
                    string.IsNullOrWhiteSpace(x.Floor)
                        ? "همکف"
                        : x.Floor)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (floors.Count == 0)
                floors.Add("همکف");

            FloorSelector.Items.Clear();
            ItemFloor.Items.Clear();

            foreach (string floor in floors)
            {
                FloorSelector.Items.Add(floor);
                ItemFloor.Items.Add(floor);
            }

            if (!floors.Contains(selected))
                selected = floors[0];

            _currentFloor = selected;

            FloorSelector.SelectedItem = selected;

            if (_selected != null)
                ItemFloor.SelectedItem = _selected.Floor;
        }

        private void Floor_Changed(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (FloorSelector.SelectedItem is string floor)
            {
                _currentFloor = floor;

                RenderCurrentView();
            }
        }

        private void Filter_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isInitialized)
                return;

            RenderCurrentView();
        }

        private string SelectedTag(
            ComboBox? combo,
            string fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem item)
                return Convert.ToString(item.Tag) ?? fallback;

            return fallback;
        }

        private bool IsItemVisible(PlanItem item)
        {
            if (!string.Equals(
                item.Floor,
                _currentFloor,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string type = SelectedTag(TypeFilter, "all");

            if (type != "all" && item.Type != type)
                return false;

            string search = SearchBox.Text?.Trim() ?? "";

            if (search.Length > 0 &&
                !item.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) &&
                !item.SystemNumber.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        // =====================================================
        // RENDER
        // =====================================================

        private void RenderCurrentView()
        {
            if (MapCanvas == null)
                return;

            _isRendering = true;

            try
            {
                MapCanvas.Children.Clear();

                if (_showGrid)
                    DrawGrid();

                if (_isCardsView)
                {
                    RenderCards();
                    return;
                }

                // فقط آیتم‌هایی که باید در نمای فعلی دیده شوند
                List<PlanItem> visibleItems = _items
                    .Where(IsItemVisible)
                    .ToList();

                // ترتیب لایه‌ها:
                // 1. ناحیه‌ها در پایین‌ترین لایه
                // 2. سایر اجزای سالن در لایه میانی
                // 3. سیستم‌های فیزیکی در بالاترین لایه
                IEnumerable<PlanItem> orderedItems = visibleItems
                    .OrderBy(item =>
                    {
                        if (item.Type == "area")
                            return 0;

                        if (item.IsPhysicalSystem)
                            return 2;

                        return 1;
                    });

                foreach (PlanItem item in orderedItems)
                {
                    DrawItem(item);
                }

                UpdateSummary();
            }
            finally
            {
                _isRendering = false;
            }
        }

        private void DrawGrid()
        {
            for (double x = 0; x <= CanvasWidth; x += GridSize)
            {
                MapCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = CanvasHeight,

                    Stroke = new SolidColorBrush(
                        Color.FromArgb(28, 180, 190, 210)),

                    StrokeThickness = 0.6,

                    IsHitTestVisible = false
                });
            }

            for (double y = 0; y <= CanvasHeight; y += GridSize)
            {
                MapCanvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = CanvasWidth,
                    Y2 = y,

                    Stroke = new SolidColorBrush(
                        Color.FromArgb(28, 180, 190, 210)),

                    StrokeThickness = 0.6,

                    IsHitTestVisible = false
                });
            }
        }

        private void DrawItem(PlanItem item)
        {
            bool isSelected = _selectedIds.Contains(item.Id);

            var border = new Border
            {
                Width = item.W,
                Height = item.H,

                CornerRadius = new CornerRadius(
                    item.Type == "wall" ? 2 : 9),

                Background = GetItemBrush(item),

                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(219, 60, 69))
                    : GetStatusBrush(item.Status),

                BorderThickness = new Thickness(
                    isSelected ? 2 : 1),

                Cursor = item.Locked
                    ? Cursors.Arrow
                    : Cursors.SizeAll,

                Tag = item,

                RenderTransformOrigin = new Point(0.5, 0.5),

                RenderTransform = new RotateTransform(item.Rotation)
            };

            var root = new Grid();

            var content = new Grid();

            if (item.Type == "wall")
            {
                content.Children.Add(new Border
                {
                    Background = new SolidColorBrush(
                        Color.FromRgb(93, 101, 120)),

                    CornerRadius = new CornerRadius(2)
                });
            }
            else if (item.Type == "door")
            {
                content.Children.Add(new TextBlock
                {
                    Text = "🚪",
                    FontSize = 22,

                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else if (item.Type == "area")
            {
                content.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 12,
                    Foreground = Brushes.White,

                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else if (item.Type == "label")
            {
                content.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 13,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,

                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                string icon = item.IsPhysicalSystem
                    ? "▣"
                    : item.Type switch
                    {
                        "console" => "🎮",
                        "desk" => "▰",
                        "decoration" => "✦",
                        _ => "▣"
                    };

                // آیکن سیستم فیزیکی
                if (item.IsPhysicalSystem)
                {
                    var systemIcon = new Grid
                    {
                        Width = 34,
                        Height = 30,
                        Margin = new Thickness(0, 0, 0, 3)
                    };

                    systemIcon.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(
                            Color.FromRgb(219, 60, 69)),

                        CornerRadius = new CornerRadius(5),
                        Margin = new Thickness(3)
                    });

                    systemIcon.Children.Add(new TextBlock
                    {
                        Text = "▣",
                        FontSize = 20,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,

                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    });

                    stack.Children.Add(systemIcon);
                }
                else
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = icon,
                        FontSize = 20,

                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }

                stack.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,

                    HorizontalAlignment = HorizontalAlignment.Center
                });

                stack.Children.Add(new TextBlock
                {
                    Text = GetStatusLabel(item.Status),
                    FontSize = 9,
                    Foreground = GetStatusBrush(item.Status),

                    HorizontalAlignment = HorizontalAlignment.Center
                });

                content.Children.Add(stack);

                DrawReservationMarkers(content, item);
            }

            root.Children.Add(content);

            // دستگیره‌های تغییر اندازه
            // فقط برای یک آیتم انتخاب‌شده و قفل‌نشده
            if (isSelected &&
                !item.Locked &&
                !_isCardsView &&
                _selectedIds.Count == 1)
            {
                AddResizeHandle(
                    root, item, ResizeDirection.NorthWest,
                    HorizontalAlignment.Left, VerticalAlignment.Top);

                AddResizeHandle(
                    root, item, ResizeDirection.North,
                    HorizontalAlignment.Center, VerticalAlignment.Top);

                AddResizeHandle(
                    root, item, ResizeDirection.NorthEast,
                    HorizontalAlignment.Right, VerticalAlignment.Top);

                AddResizeHandle(
                    root, item, ResizeDirection.West,
                    HorizontalAlignment.Left, VerticalAlignment.Center);

                AddResizeHandle(
                    root, item, ResizeDirection.East,
                    HorizontalAlignment.Right, VerticalAlignment.Center);

                AddResizeHandle(
                    root, item, ResizeDirection.SouthWest,
                    HorizontalAlignment.Left, VerticalAlignment.Bottom);

                AddResizeHandle(
                    root, item, ResizeDirection.South,
                    HorizontalAlignment.Center, VerticalAlignment.Bottom);

                AddResizeHandle(
                    root, item, ResizeDirection.SouthEast,
                    HorizontalAlignment.Right, VerticalAlignment.Bottom);
            }

            border.Child = root;

            Canvas.SetLeft(border, item.X);
            Canvas.SetTop(border, item.Y);

            border.MouseLeftButtonDown += Item_MouseLeftButtonDown;
            border.MouseMove += Item_MouseMove;
            border.MouseLeftButtonUp += Item_MouseLeftButtonUp;

            ToolTipService.SetToolTip(
                border,
                $"{item.Name}\n" +
                $"{GetStatusLabel(item.Status)}\n" +
                $"X: {item.X:0}  Y: {item.Y:0}\n" +
                $"W: {item.W:0}  H: {item.H:0}\n" +
                $"Rotation: {item.Rotation:0}°");

            MapCanvas.Children.Add(border);
        }
        private void AddResizeHandle(
    Grid root,
    PlanItem item,
    ResizeDirection direction,
    HorizontalAlignment horizontal,
    VerticalAlignment vertical)
        {
            Cursor cursor = direction switch
            {
                ResizeDirection.NorthWest or
                ResizeDirection.SouthEast => Cursors.SizeNWSE,

                ResizeDirection.NorthEast or
                ResizeDirection.SouthWest => Cursors.SizeNESW,

                ResizeDirection.North or
                ResizeDirection.South => Cursors.SizeNS,

                _ => Cursors.SizeWE
            };

            var handle = new Border
            {
                Width = 10,
                Height = 10,

                Background = new SolidColorBrush(
                    Color.FromRgb(219, 60, 69)),

                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),

                CornerRadius = new CornerRadius(2),

                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,

                Margin = new Thickness(-5),

                Cursor = cursor,

                Tag = new ResizeHandleInfo(item.Id, direction)
            };

            Panel.SetZIndex(handle, 100);

            handle.MouseLeftButtonDown += ResizeHandle_MouseLeftButtonDown;
            handle.MouseMove += ResizeHandle_MouseMove;
            handle.MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;

            root.Children.Add(handle);
        }

        private void DrawReservationMarkers(
            Panel panel,
            PlanItem item)
        {
            var matches = _reservations
                .Where(r => ReservationMatchesItem(r, item))
                .Where(r => r.Date == DateTime.Today.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture))
                .ToList();

            if (matches.Count == 0)
                return;

            var markerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Bottom,

                Margin = new Thickness(3, 0, 3, 3),

                IsHitTestVisible = false
            };

            foreach (ReservationDisplay reservation in
                matches.Take(6))
            {
                markerPanel.Children.Add(new Border
                {
                    Width = 9,
                    Height = 5,

                    Margin = new Thickness(1),

                    CornerRadius = new CornerRadius(2),

                    Background = reservation.Color
                });
            }

            panel.Children.Add(markerPanel);
        }

        private Brush GetItemBrush(PlanItem item)
        {
            return item.Type switch
            {
                "computer" => new SolidColorBrush(
                    Color.FromRgb(35, 55, 75)),

                "console" => new SolidColorBrush(
                    Color.FromRgb(66, 43, 75)),

                "desk" => new SolidColorBrush(
                    Color.FromRgb(83, 69, 48)),

                "wall" => new SolidColorBrush(
                    Color.FromRgb(70, 75, 88)),

                "door" => new SolidColorBrush(
                    Color.FromRgb(48, 70, 59)),

                "area" => new SolidColorBrush(
                    Color.FromArgb(55, 90, 120, 160)),

                "label" => Brushes.Transparent,

                "decoration" => new SolidColorBrush(
                    Color.FromRgb(57, 62, 75)),

                _ => new SolidColorBrush(
                    Color.FromRgb(48, 53, 66))
            };
        }

        private static Brush GetStatusBrush(string status)
        {
            return status switch
            {
                "free" => Brushes.LimeGreen,
                "reserved" => Brushes.Goldenrod,
                "occupied" => Brushes.IndianRed,
                "offline" => Brushes.Gray,
                _ => Brushes.SlateGray
            };
        }

        private static string GetStatusLabel(string status)
        {
            return status switch
            {
                "free" => "آزاد",
                "reserved" => "رزرو",
                "occupied" => "در حال استفاده",
                "offline" => "خاموش",
                _ => "نامشخص"
            };
        }

        private void RenderCards()
        {
            double x = 25;
            double y = 25;

            double cardWidth = 180;
            double cardHeight = 115;

            int column = 0;

            foreach (PlanItem item in _items.Where(IsItemVisible))
            {
                var card = new Border
                {
                    Width = cardWidth,
                    Height = cardHeight,

                    Background = GetItemBrush(item),

                    BorderBrush = GetStatusBrush(item.Status),

                    BorderThickness = new Thickness(1),

                    CornerRadius = new CornerRadius(9),

                    Padding = new Thickness(10),

                    Tag = item,

                    Cursor = Cursors.Hand
                };

                var stack = new StackPanel();

                stack.Children.Add(new TextBlock
                {
                    Text = item.Name,

                    FontWeight = FontWeights.Bold,

                    FontSize = 13
                });

                stack.Children.Add(new TextBlock
                {
                    Text = GetStatusLabel(item.Status),

                    Foreground = GetStatusBrush(item.Status),

                    Margin = new Thickness(0, 8, 0, 0)
                });

                card.Child = stack;

                card.MouseLeftButtonDown +=
                    Item_MouseLeftButtonDown;

                Canvas.SetLeft(
                    card,
                    x + column * (cardWidth + 15));

                Canvas.SetTop(card, y);

                MapCanvas.Children.Add(card);

                column++;

                if (column >= 6)
                {
                    column = 0;
                    y += cardHeight + 15;
                }
            }
        }

        private void Cards_Click(
            object sender,
            RoutedEventArgs e)
        {
            _isCardsView = true;

            RenderCurrentView();
        }

        private void MapView_Click(
            object sender,
            RoutedEventArgs e)
        {
            _isCardsView = false;

            RenderCurrentView();
        }

        private void Grid_Click(
            object sender,
            RoutedEventArgs e)
        {
            _showGrid = !_showGrid;

            RenderCurrentView();
        }

        // =====================================================
        // SHARED LAYOUT / JSON HELPERS
        // =====================================================

        private const double LayoutMargin = 30;
        private const double LayoutGap = 20;

        private static double GetItemWidth(PlanItem item)
        {
            return Math.Clamp(
                double.IsFinite(item.W) ? item.W : 100,
                10,
                CanvasWidth - LayoutMargin * 2);
        }

        private static double GetItemHeight(PlanItem item)
        {
            return Math.Clamp(
                double.IsFinite(item.H) ? item.H : 75,
                10,
                CanvasHeight - LayoutMargin * 2);
        }

        private static bool RectanglesOverlap(
            double x1,
            double y1,
            double w1,
            double h1,
            double x2,
            double y2,
            double w2,
            double h2,
            double gap = LayoutGap)
        {
            return
                x1 < x2 + w2 + gap &&
                x1 + w1 + gap > x2 &&
                y1 < y2 + h2 + gap &&
                y1 + h1 + gap > y2;
        }

        private bool IsPositionFree(
            double x,
            double y,
            double width,
            double height,
            string floor,
            IEnumerable<PlanItem>? obstacles = null,
            string? ignoreId = null)
        {
            if (x < LayoutMargin ||
                y < LayoutMargin ||
                x + width > CanvasWidth - LayoutMargin ||
                y + height > CanvasHeight - LayoutMargin)
            {
                return false;
            }

            IEnumerable<PlanItem> source =
                obstacles ?? _items;

            foreach (PlanItem other in source)
            {
                if (other.Id == ignoreId)
                    continue;

                if (other.Floor != floor)
                    continue;

                double otherW = GetItemWidth(other);
                double otherH = GetItemHeight(other);

                if (RectanglesOverlap(
                    x, y, width, height,
                    other.X, other.Y,
                    otherW, otherH))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryFindFreePosition(
            double width,
            double height,
            string floor,
            out double resultX,
            out double resultY,
            IEnumerable<PlanItem>? obstacles = null,
            string? ignoreId = null)
        {
            width = Math.Clamp(
                width,
                10,
                CanvasWidth - LayoutMargin * 2);

            height = Math.Clamp(
                height,
                10,
                CanvasHeight - LayoutMargin * 2);

            IEnumerable<PlanItem> source =
                obstacles ?? _items;

            // جست‌وجوی ردیفی از بالا به پایین.
            // هر نقطه فقط زمانی پذیرفته می‌شود که با هیچ آیتمی
            // در همان طبقه برخورد نداشته باشد.

            double maxX = CanvasWidth - LayoutMargin - width;
            double maxY = CanvasHeight - LayoutMargin - height;

            for (double y = LayoutMargin;
                 y <= maxY;
                 y += GridSize)
            {
                for (double x = LayoutMargin;
                     x <= maxX;
                     x += GridSize)
                {
                    if (IsPositionFree(
                        x,
                        y,
                        width,
                        height,
                        floor,
                        source,
                        ignoreId))
                    {
                        resultX = x;
                        resultY = y;

                        return true;
                    }
                }
            }

            resultX = -1;
            resultY = -1;

            return false;
        }

        private bool TryPlaceItem(
            PlanItem item,
            IEnumerable<PlanItem>? obstacles = null)
        {
            if (!TryFindFreePosition(
                item.W,
                item.H,
                item.Floor,
                out double x,
                out double y,
                obstacles,
                item.Id))
            {
                // سیستم در فهرست سیستم‌های جانمایی‌نشده باقی می‌ماند.
                item.X = -1;
                item.Y = -1;

                return false;
            }

            item.X = x;
            item.Y = y;

            return true;
        }

        private static JsonElement? FindJsonProperty(
            JsonElement element,
            params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                foreach (string name in names)
                {
                    if (string.Equals(
                        property.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return property.Value;
                    }
                }
            }

            return null;
        }

        private static string JsonString(
            JsonElement element,
            string fallback = "")
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? fallback;

            if (element.ValueKind == JsonValueKind.Number ||
                element.ValueKind == JsonValueKind.True ||
                element.ValueKind == JsonValueKind.False)
            {
                return element.ToString();
            }

            return fallback;
        }

        private static double JsonDouble(
            JsonElement element,
            double fallback)
        {
            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetDouble(out double value) &&
                double.IsFinite(value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    element.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out value) &&
                double.IsFinite(value))
            {
                return value;
            }

            return fallback;
        }

        private static int JsonInt(
            JsonElement element,
            int fallback = 0)
        {
            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt32(out int value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), out value))
            {
                return value;
            }

            return fallback;
        }

        private static bool JsonBool(
            JsonElement element,
            bool fallback = false)
        {
            if (element.ValueKind == JsonValueKind.True)
                return true;

            if (element.ValueKind == JsonValueKind.False)
                return false;

            if (element.ValueKind == JsonValueKind.Number)
                return element.GetInt32() != 0;

            if (element.ValueKind == JsonValueKind.String &&
                bool.TryParse(element.GetString(), out bool value))
            {
                return value;
            }

            return fallback;
        }

        private static string GetJsonString(
            JsonElement element,
            string fallback,
            params string[] names)
        {
            JsonElement? value = FindJsonProperty(element, names);

            return value.HasValue
                ? JsonString(value.Value, fallback)
                : fallback;
        }

        private static double GetJsonDouble(
            JsonElement element,
            double fallback,
            params string[] names)
        {
            JsonElement? value = FindJsonProperty(element, names);

            return value.HasValue
                ? JsonDouble(value.Value, fallback)
                : fallback;
        }

        private static int GetJsonInt(
            JsonElement element,
            int fallback,
            params string[] names)
        {
            JsonElement? value = FindJsonProperty(element, names);

            return value.HasValue
                ? JsonInt(value.Value, fallback)
                : fallback;
        }

        private static bool GetJsonBool(
            JsonElement element,
            bool fallback,
            params string[] names)
        {
            JsonElement? value = FindJsonProperty(element, names);

            return value.HasValue
                ? JsonBool(value.Value, fallback)
                : fallback;
        }

        private static JsonElement? FindLayoutItemsArray(
            JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            string[] arrayNames =
            {
        "items",
        "objects",
        "systems"
    };

            foreach (string name in arrayNames)
            {
                JsonElement? value =
                    FindJsonProperty(root, name);

                if (value.HasValue &&
                    value.Value.ValueKind == JsonValueKind.Array)
                {
                    return value.Value;
                }
            }

            string[] wrapperNames =
            {
        "layout",
        "data",
        "result",
        "document"
    };

            foreach (string wrapperName in wrapperNames)
            {
                JsonElement? wrapper =
                    FindJsonProperty(root, wrapperName);

                if (wrapper.HasValue)
                {
                    JsonElement? nested =
                        FindLayoutItemsArray(wrapper.Value);

                    if (nested.HasValue)
                        return nested.Value;
                }
            }

            return null;
        }

        private static PlanItem ParseLayoutItem(JsonElement element)
        {
            string id = GetJsonString(
                element,
                Guid.NewGuid().ToString("N"),
                "id",
                "Id");

            string systemId = GetJsonString(
                element,
                "",
                "system_id",
                "systemId",
                "SystemId");

            string systemNumber = GetJsonString(
                element,
                "",
                "system_number",
                "systemNumber",
                "SystemNumber");

            bool physical = GetJsonBool(
                element,
                !string.IsNullOrWhiteSpace(systemId),
                "is_physical_system",
                "isPhysicalSystem",
                "IsPhysicalSystem");

            string type = GetJsonString(
                element,
                physical ? "computer" : "decoration",
                "type",
                "Type");

            string name = GetJsonString(
                element,
                physical
                    ? systemNumber
                    : "آیتم واردشده",
                "name",
                "label",
                "title",
                "Name");

            string floor = GetJsonString(
                element,
                "طبقه اول",
                "floor",
                "Floor");

            return new PlanItem
            {
                Id = string.IsNullOrWhiteSpace(id)
                    ? Guid.NewGuid().ToString("N")
                    : id,

                Name = name,
                Type = type,

                Status = NormalizeStatus(
                    GetJsonString(element, "free", "status", "Status")),

                Floor = floor,

                SystemId = systemId,
                SystemNumber = systemNumber,

                VariationId = GetJsonInt(
                    element,
                    0,
                    "variation_id",
                    "variationId",
                    "VariationId"),

                IsPhysicalSystem = physical,

                Locked = GetJsonBool(
                    element,
                    false,
                    "locked",
                    "Locked"),

                GroupId = GetJsonString(
                    element,
                    "",
                    "group_id",
                    "groupId",
                    "GroupId"),

                X = GetJsonDouble(element, 50, "x", "X"),
                Y = GetJsonDouble(element, 50, "y", "Y"),

                W = GetJsonDouble(
                    element,
                    110,
                    "w",
                    "width",
                    "W"),

                H = GetJsonDouble(
                    element,
                    75,
                    "height",
                    "h",
                    "H"),

                Rotation = GetJsonDouble(
                    element,
                    0,
                    "rotation",
                    "Rotation")
            };
        }

        private static LayoutDocument ParseLayoutDocument(
            string json)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);

            JsonElement root = parsed.RootElement;

            JsonElement? array = FindLayoutItemsArray(root);

            if (!array.HasValue)
            {
                throw new InvalidDataException(
                    "ساختار فایل JSON معتبر نیست یا آرایه آیتم‌ها پیدا نشد.");
            }

            int gamenetId = GetJsonInt(
                root,
                0,
                "gamenet_id",
                "gamenetId",
                "GamenetId");

            string gamenetName = GetJsonString(
                root,
                "",
                "gamenet_name",
                "gamenetName",
                "GamenetName");

            // اطلاعات ممکن است داخل wrapper قرار گرفته باشند.
            if (root.ValueKind == JsonValueKind.Object)
            {
                JsonElement? layout =
                    FindJsonProperty(root, "layout", "data", "document");

                if (layout.HasValue)
                {
                    if (gamenetId == 0)
                    {
                        gamenetId = GetJsonInt(
                            layout.Value,
                            0,
                            "gamenet_id",
                            "gamenetId",
                            "GamenetId");
                    }

                    if (string.IsNullOrWhiteSpace(gamenetName))
                    {
                        gamenetName = GetJsonString(
                            layout.Value,
                            "",
                            "gamenet_name",
                            "gamenetName",
                            "GamenetName");
                    }
                }
            }

            var document = new LayoutDocument
            {
                GamenetId = gamenetId,
                GamenetName = gamenetName,
                SavedAt = DateTime.Now,
                Items = new List<PlanItem>()
            };

            foreach (JsonElement element in array.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                PlanItem item = ParseLayoutItem(element);

                if (IsValidItem(item))
                    document.Items.Add(item);
            }

            return document;
        }

        private bool IsInsideCanvas(PlanItem item)
        {
            return
                item.X >= LayoutMargin &&
                item.Y >= LayoutMargin &&
                item.X + item.W <= CanvasWidth - LayoutMargin &&
                item.Y + item.H <= CanvasHeight - LayoutMargin;
        }

        // =====================================================
        // SELECTION / DRAG
        // =====================================================

        private void Item_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.Tag is not PlanItem item)
            {
                return;
            }

            if (_resizing)
                return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (!_selectedIds.Add(item.Id))
                    _selectedIds.Remove(item.Id);
            }
            else if (!_selectedIds.Contains(item.Id))
            {
                _selectedIds.Clear();
                _selectedIds.Add(item.Id);
            }

            // سیستم کلیک‌شده باید آیتم اصلی انتخاب باشد.
            // اگر با Ctrl از انتخاب خارج شده، اولین آیتم باقی‌مانده انتخاب شود.
            _selected = _selectedIds.Contains(item.Id)
                ? item
                : _items.FirstOrDefault(
                    x => _selectedIds.Contains(x.Id));

            _dragging =
                !_isCardsView &&
                _selectedIds.Any(id =>
                    _items.Any(x => x.Id == id && !x.Locked));

            _dragUndoSaved = false;

            _dragStartMouse = e.GetPosition(MapCanvas);
            _lastMouse = _dragStartMouse;

            _dragOrigins.Clear();

            foreach (string id in _selectedIds)
            {
                PlanItem? selected =
                    _items.FirstOrDefault(x => x.Id == id);

                if (selected != null)
                {
                    _dragOrigins[id] =
                        new Point(selected.X, selected.Y);
                }
            }

            UpdateDetails();

            // بازسازی برای نمایش دستگیره‌های انتخاب
            RenderCurrentView();

            if (_dragging)
            {
                FrameworkElement? visual =
                    MapCanvas.Children
                        .OfType<FrameworkElement>()
                        .FirstOrDefault(x =>
                            x.Tag is PlanItem p &&
                            p.Id == item.Id);

                visual?.CaptureMouse();
            }

            e.Handled = true;
        }

        private void Item_MouseMove(
    object sender,
    MouseEventArgs e)
        {
            if (!_dragging ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(MapCanvas);

            double dx = current.X - _dragStartMouse.X;
            double dy = current.Y - _dragStartMouse.Y;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (Math.Abs(dx) > Math.Abs(dy))
                    dy = 0;
                else
                    dx = 0;
            }

            // Undo فقط یک بار در شروع حرکت واقعی
            if (!_dragUndoSaved)
            {
                if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
                    return;

                SaveUndo();
                _dragUndoSaved = true;
            }

            foreach (string id in _selectedIds)
            {
                PlanItem? item =
                    _items.FirstOrDefault(x => x.Id == id);

                if (item == null || item.Locked)
                    continue;

                if (!_dragOrigins.TryGetValue(id, out Point origin))
                    continue;

                double newX = origin.X + dx;
                double newY = origin.Y + dy;

                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    newX = Math.Round(newX / GridSize) * GridSize;
                    newY = Math.Round(newY / GridSize) * GridSize;
                }

                item.X = Math.Clamp(
                    newX,
                    0,
                    CanvasWidth - item.W);

                item.Y = Math.Clamp(
                    newY,
                    0,
                    CanvasHeight - item.H);

                // فقط کنترل موجود جابه‌جا می‌شود؛
                // Canvas در هر حرکت پاک و دوباره ساخته نمی‌شود.
                FrameworkElement? visual =
                    MapCanvas.Children
                        .OfType<FrameworkElement>()
                        .FirstOrDefault(x =>
                            x.Tag is PlanItem p &&
                            p.Id == item.Id);

                if (visual != null)
                {
                    Canvas.SetLeft(visual, item.X);
                    Canvas.SetTop(visual, item.Y);
                }
            }

            if (_dragUndoSaved)
                MarkDirty();
        }

        private void Item_MouseLeftButtonUp(
    object sender,
    MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;

            _dragging = false;

            if (sender is FrameworkElement element)
                element.ReleaseMouseCapture();

            _dragOrigins.Clear();

            _dragUndoSaved = false;

            RenderCurrentView();

            UpdateDetails();
            UpdateSummary();
            UpdateUnplacedSystems();

            e.Handled = true;
        }
        private void ResizeHandle_MouseLeftButtonDown(
    object sender,
    MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement handle ||
                handle.Tag is not ResizeHandleInfo info)
            {
                return;
            }

            PlanItem? item =
                _items.FirstOrDefault(x => x.Id == info.ItemId);

            if (item == null || item.Locked || _isCardsView)
                return;

            _selectedIds.Clear();
            _selectedIds.Add(item.Id);

            _selected = item;

            _dragging = false;
            _resizing = true;

            _resizeUndoSaved = false;

            _resizeItem = item;
            _resizeDirection = info.Direction;

            _resizeStartMouse = e.GetPosition(MapCanvas);

            _resizeStartBounds = new Rect(
                item.X,
                item.Y,
                item.W,
                item.H);

            _resizeVisual =
                MapCanvas.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(x =>
                        x.Tag is PlanItem p &&
                        p.Id == item.Id);

            handle.CaptureMouse();

            UpdateDetails();

            e.Handled = true;
        }
        private void ResizeHandle_MouseMove(
    object sender,
    MouseEventArgs e)
        {
            if (!_resizing ||
                _resizeItem == null ||
                _resizeVisual == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(MapCanvas);

            double dx = current.X - _resizeStartMouse.X;
            double dy = current.Y - _resizeStartMouse.Y;

            if (!_resizeUndoSaved)
            {
                if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
                    return;

                SaveUndo();
                _resizeUndoSaved = true;
            }

            const double minWidth = 20;
            const double minHeight = 20;

            double left = _resizeStartBounds.Left;
            double top = _resizeStartBounds.Top;
            double right = _resizeStartBounds.Right;
            double bottom = _resizeStartBounds.Bottom;

            bool west =
                _resizeDirection == ResizeDirection.West ||
                _resizeDirection == ResizeDirection.NorthWest ||
                _resizeDirection == ResizeDirection.SouthWest;

            bool east =
                _resizeDirection == ResizeDirection.East ||
                _resizeDirection == ResizeDirection.NorthEast ||
                _resizeDirection == ResizeDirection.SouthEast;

            bool north =
                _resizeDirection == ResizeDirection.North ||
                _resizeDirection == ResizeDirection.NorthWest ||
                _resizeDirection == ResizeDirection.NorthEast;

            bool south =
                _resizeDirection == ResizeDirection.South ||
                _resizeDirection == ResizeDirection.SouthWest ||
                _resizeDirection == ResizeDirection.SouthEast;

            if (west)
                left = Math.Clamp(
                    _resizeStartBounds.Left + dx,
                    0,
                    right - minWidth);

            if (east)
                right = Math.Clamp(
                    _resizeStartBounds.Right + dx,
                    left + minWidth,
                    CanvasWidth);

            if (north)
                top = Math.Clamp(
                    _resizeStartBounds.Top + dy,
                    0,
                    bottom - minHeight);

            if (south)
                bottom = Math.Clamp(
                    _resizeStartBounds.Bottom + dy,
                    top + minHeight,
                    CanvasHeight);

            // Snap به Grid مگر اینکه Alt نگه داشته شده باشد
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                if (west)
                    left = Math.Clamp(
                        Math.Round(left / GridSize) * GridSize,
                        0,
                        right - minWidth);

                if (east)
                    right = Math.Clamp(
                        Math.Round(right / GridSize) * GridSize,
                        left + minWidth,
                        CanvasWidth);

                if (north)
                    top = Math.Clamp(
                        Math.Round(top / GridSize) * GridSize,
                        0,
                        bottom - minHeight);

                if (south)
                    bottom = Math.Clamp(
                        Math.Round(bottom / GridSize) * GridSize,
                        top + minHeight,
                        CanvasHeight);
            }

            _resizeItem.X = left;
            _resizeItem.Y = top;
            _resizeItem.W = right - left;
            _resizeItem.H = bottom - top;

            _resizeVisual.Width = _resizeItem.W;
            _resizeVisual.Height = _resizeItem.H;

            Canvas.SetLeft(_resizeVisual, _resizeItem.X);
            Canvas.SetTop(_resizeVisual, _resizeItem.Y);

            MarkDirty();

            e.Handled = true;
        }
        private void ResizeHandle_MouseLeftButtonUp(
    object sender,
    MouseButtonEventArgs e)
        {
            if (!_resizing)
                return;

            if (sender is FrameworkElement handle)
                handle.ReleaseMouseCapture();

            _resizing = false;

            _resizeItem = null;
            _resizeVisual = null;

            _resizeUndoSaved = false;

            RenderCurrentView();

            UpdateDetails();
            UpdateSummary();
            UpdateUnplacedSystems();

            e.Handled = true;
        }
        private void MapCanvas_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.Space))
            {
                _isPanning = true;

                _panOrigin = e.GetPosition(MapScroll);

                MapCanvas.CaptureMouse();

                e.Handled = true;
            }
        }

        private void MapCanvas_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_isPanning ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point current = e.GetPosition(MapScroll);

            Vector delta = current - _panOrigin;

            MapScroll.ScrollToHorizontalOffset(
                MapScroll.HorizontalOffset - delta.X);

            MapScroll.ScrollToVerticalOffset(
                MapScroll.VerticalOffset - delta.Y);

            _panOrigin = current;
        }

        private void MapCanvas_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;

                MapCanvas.ReleaseMouseCapture();
            }
        }

        // =====================================================
        // DETAILS / SUMMARY
        // =====================================================

        private void UpdateDetails()
        {
            if (_selected == null)
            {
                SelectionHint.Text = _selectedIds.Count > 1
                    ? $"{_selectedIds.Count} آیتم انتخاب شده"
                    : "برای مشاهده مشخصات، یک آیتم را انتخاب کنید.";

                DuplicateButton.IsEnabled = false;

                RemoveButton.IsEnabled = _items.Any(x =>
                    _selectedIds.Contains(x.Id) &&
                    !x.IsPhysicalSystem);

                UpdateSelectedReservations();

                return;
            }

            SelectionHint.Text = _selectedIds.Count > 1
                ? $"{_selectedIds.Count} آیتم انتخاب شده"
                : _selected.IsPhysicalSystem
                    ? "سیستم فیزیکی متصل به گیمنت"
                    : "آیتم طراحی دستی";

            // کنترل فعال و غیرفعال بودن دکمه‌ها
            DuplicateButton.IsEnabled =
                !_selected.IsPhysicalSystem &&
                _selectedIds.Count == 1;

            RemoveButton.IsEnabled =
                _selectedIds.Any(id =>
                    _items.Any(x =>
                        x.Id == id &&
                        !x.IsPhysicalSystem));

            NameBox.Text = _selected.Name;

            WidthBox.Text = _selected.W.ToString(
                "0.##",
                CultureInfo.InvariantCulture);

            HeightBox.Text = _selected.H.ToString(
                "0.##",
                CultureInfo.InvariantCulture);

            XBox.Text = _selected.X.ToString(
                "0.##",
                CultureInfo.InvariantCulture);

            YBox.Text = _selected.Y.ToString(
                "0.##",
                CultureInfo.InvariantCulture);

            ItemType.SelectedItem = ItemType.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (string?)x.Tag == _selected.Type);

            ItemStatus.SelectedItem = ItemStatus.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x =>
                    (string?)x.Tag == _selected.Status);

            ItemFloor.SelectedItem = _selected.Floor;

            UpdateSelectedReservations();
        }

        private void UpdateSummary()
        {
            int systems =
                _items.Count(x => x.IsPhysicalSystem);

            int free =
                _items.Count(x =>
                    x.IsPhysicalSystem &&
                    x.Status == "free");

            int reserved =
                _items.Count(x =>
                    x.IsPhysicalSystem &&
                    x.Status == "reserved");

            int occupied =
                _items.Count(x =>
                    x.IsPhysicalSystem &&
                    x.Status == "occupied");

            TotalSystemsText.Text =
                systems.ToString(CultureInfo.InvariantCulture);

            FreeSystemsText.Text =
                free.ToString(CultureInfo.InvariantCulture);

            ReservedSystemsText.Text =
                reserved.ToString(CultureInfo.InvariantCulture);

            OccupiedSystemsText.Text =
                occupied.ToString(CultureInfo.InvariantCulture);

            Summary.Text =
                $"کل آیتم‌ها: {_items.Count}\n" +
                $"سیستم فیزیکی: {systems}\n" +
                $"اشیای دستی: {_items.Count(x => !x.IsPhysicalSystem)}\n" +
                $"طبقات: {_items.Select(x => x.Floor).Distinct().Count()}\n" +
                $"رزروهای دریافت‌شده: {_reservations.Select(x => x.Id).Distinct().Count()}";
        }

        // =====================================================
        // ADD ITEMS
        // =====================================================

        private void AddSystem_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("computer", "کامپیوتر", 110, 75);

        private void AddConsole_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("console", "کنسول", 125, 85);

        private void AddDesk_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("desk", "میز", 130, 65);

        private void AddWall_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("wall", "دیوار", 250, 15);

        private void AddDoor_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("door", "در", 85, 20);

        private void AddLabel_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("label", "برچسب جدید", 150, 40);

        private void AddArea_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("area", "ناحیه جدید", 260, 180);

        private void AddDecoration_Click(
            object sender,
            RoutedEventArgs e)
            => AddItem("decoration", "امکانات", 100, 75);


        private void AddItem(
            string type,
            string name,
            double width,
            double height)
        {
            var item = new PlanItem
            {
                Name = "",
                Type = type,
                Status = "free",
                Floor = _currentFloor,
                W = width,
                H = height
            };

            int number = 1;

            string finalName;

            do
            {
                finalName = $"{name} {number++}";
            }
            while (_items.Any(x =>
                string.Equals(
                    x.Name,
                    finalName,
                    StringComparison.OrdinalIgnoreCase)));

            item.Name = finalName;

            if (!TryPlaceItem(item))
            {
                MessageBox.Show(
                    "فضای خالی کافی در این طبقه وجود ندارد. ابتدا تعدادی از آیتم‌ها را جابه‌جا کنید یا طبقه دیگری انتخاب کنید.",
                    "فضای نقشه پر است",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            SaveUndo();

            _items.Add(item);

            _selected = item;

            _selectedIds.Clear();
            _selectedIds.Add(item.Id);

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateSummary();
            UpdateUnplacedSystems();
            PopulateFloors();
        }



        private void PlaceUnplaced_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not string id)
            {
                return;
            }

            PlanItem? item =
                _items.FirstOrDefault(x => x.Id == id);

            if (item == null)
                return;

            string previousFloor = item.Floor;

            item.Floor = _currentFloor;

            if (!TryFindFreePosition(
                item.W,
                item.H,
                item.Floor,
                out double x,
                out double y,
                _items,
                item.Id))
            {
                item.Floor = previousFloor;

                MessageBox.Show(
                    "در طبقه انتخاب‌شده فضای خالی برای این سیستم وجود ندارد.",
                    "عدم امکان جانمایی",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            SaveUndo();

            item.X = x;
            item.Y = y;

            _selected = item;

            _selectedIds.Clear();
            _selectedIds.Add(item.Id);

            UpdateUnplacedSystems();

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateSummary();
        }


        // =====================================================
        // EDIT / DELETE / LOCK / DUPLICATE
        // =====================================================

        private void Apply_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            if (!TryNumber(WidthBox.Text, out double w) ||
                !TryNumber(HeightBox.Text, out double h) ||
                !TryNumber(XBox.Text, out double x) ||
                !TryNumber(YBox.Text, out double y) ||
                w < 10 ||
                h < 10)
            {
                MessageBox.Show(
                    "ابعاد یا موقعیت واردشده معتبر نیست.");

                return;
            }

            SaveUndo();

            // مشخصات اصلی سیستم فیزیکی از API می‌آیند.
            // تغییر نام و نوع سیستم فیزیکی از این پنل
            // نباید هویت یا اطلاعات واقعی سیستم را عوض کند.
            if (!_selected.IsPhysicalSystem)
            {
                _selected.Name = NameBox.Text.Trim();

                if (ItemType.SelectedItem is ComboBoxItem type)
                {
                    _selected.Type =
                        Convert.ToString(type.Tag) ??
                        _selected.Type;
                }

                if (ItemStatus.SelectedItem is ComboBoxItem status)
                {
                    _selected.Status =
                        Convert.ToString(status.Tag) ??
                        _selected.Status;
                }
            }

            _selected.W = Math.Min(w, CanvasWidth);
            _selected.H = Math.Min(h, CanvasHeight);

            _selected.X = Math.Clamp(
                x,
                0,
                CanvasWidth - _selected.W);

            _selected.Y = Math.Clamp(
                y,
                0,
                CanvasHeight - _selected.H);

            if (ItemFloor.SelectedItem is string floor)
                _selected.Floor = floor;

            MarkDirty();

            PopulateFloors();
            RenderCurrentView();
            UpdateDetails();
            UpdateSummary();
        }

        private static bool TryNumber(
            string text,
            out double result)
        {
            return double.TryParse(
                       text,
                       NumberStyles.Any,
                       CultureInfo.InvariantCulture,
                       out result) ||
                   double.TryParse(
                       text,
                       NumberStyles.Any,
                       CultureInfo.CurrentCulture,
                       out result);
        }

        private void Remove_Click(
     object sender,
     RoutedEventArgs e)
        {
            var removableItems = _items
                .Where(x =>
                    _selectedIds.Contains(x.Id) &&
                    !x.IsPhysicalSystem)
                .ToList();

            if (removableItems.Count == 0 &&
                _selected != null &&
                !_selected.IsPhysicalSystem)
            {
                removableItems.Add(_selected);
            }

            if (removableItems.Count == 0)
                return;

            SaveUndo();

            foreach (PlanItem item in removableItems)
                _items.Remove(item);

            _selected = null;
            _selectedIds.Clear();

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateSummary();
            UpdateUnplacedSystems();
        }

        private void Lock_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_selectedIds.Count == 0 &&
                _selected == null)
            {
                return;
            }

            SaveUndo();

            foreach (PlanItem item in _items.Where(
                x => _selectedIds.Contains(x.Id)))
            {
                item.Locked = !item.Locked;
            }

            if (_selected != null &&
                _selectedIds.Count == 0)
            {
                _selected.Locked = !_selected.Locked;
            }

            MarkDirty();

            RenderCurrentView();
        }

        private void Duplicate_Click(
     object sender,
     RoutedEventArgs e)
        {
            if (_selected == null ||
                _selected.IsPhysicalSystem ||
                _selectedIds.Count != 1)
            {
                return;
            }

            SaveUndo();

            PlanItem copy = _selected.Clone();

            copy.Id = Guid.NewGuid().ToString("N");

            copy.Name += " - کپی";

            copy.X = Math.Clamp(
                copy.X + 30,
                0,
                CanvasWidth - copy.W);

            copy.Y = Math.Clamp(
                copy.Y + 30,
                0,
                CanvasHeight - copy.H);

            // این آیتم از ابتدا دستی بوده؛
            // هویت سیستم فیزیکی نباید ساخته یا کپی شود.
            _items.Add(copy);

            _selected = copy;

            _selectedIds.Clear();
            _selectedIds.Add(copy.Id);

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateSummary();
        }

        // =====================================================
        // ALIGN / GROUP / AUTO ARRANGE
        // =====================================================


        // =====================================================
        // ALIGN / GROUP / AUTO ARRANGE
        // =====================================================

        private List<PlanItem> SelectedItems()
        {
            return _items
                .Where(x => _selectedIds.Contains(x.Id))
                .ToList();
        }

        // =====================================================
        // ALIGN HORIZONTAL
        // =====================================================

        private void AlignH_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<PlanItem> selected = SelectedItems();

            if (selected.Count < 2)
                return;

            SaveUndo();

            double center =
                selected.Average(x => x.Y + x.H / 2);

            foreach (PlanItem item in selected)
            {
                if (item.Locked)
                    continue;

                item.Y = Math.Clamp(
                    center - item.H / 2,
                    0,
                    CanvasHeight - item.H);
            }

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateUnplacedSystems();
        }

        // =====================================================
        // ALIGN VERTICAL
        // =====================================================

        private void AlignV_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<PlanItem> selected = SelectedItems();

            if (selected.Count < 2)
                return;

            SaveUndo();

            double center =
                selected.Average(x => x.X + x.W / 2);

            foreach (PlanItem item in selected)
            {
                if (item.Locked)
                    continue;

                item.X = Math.Clamp(
                    center - item.W / 2,
                    0,
                    CanvasWidth - item.W);
            }

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateUnplacedSystems();
        }

        // =====================================================
        // GROUP
        // =====================================================

        private void Group_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<PlanItem> selected = SelectedItems();

            if (selected.Count < 2)
                return;

            SaveUndo();

            string groupId =
                Guid.NewGuid().ToString("N");

            foreach (PlanItem item in selected)
                item.GroupId = groupId;

            MarkDirty();

            Footer.Text =
                $"{selected.Count} آیتم گروه‌بندی شد";
        }

        // =====================================================
        // UNGROUP
        // =====================================================

        private void Ungroup_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<PlanItem> selected = SelectedItems();

            if (selected.Count == 0)
                return;

            SaveUndo();

            foreach (PlanItem item in selected)
                item.GroupId = "";

            MarkDirty();

            Footer.Text = "گروه‌بندی لغو شد";
        }

        // =====================================================
        // SHARED PHYSICAL SYSTEM ARRANGEMENT
        // =====================================================

        private void ArrangePhysicalSystems(
            bool preserveLockedItems)
        {
            List<PlanItem> systems = _items
                .Where(x =>
                    x.IsPhysicalSystem &&
                    x.Floor == _currentFloor)
                .OrderBy(x => x.VariationId)
                .ThenBy(
                    x => x.SystemNumber,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    x => x.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (systems.Count == 0)
                return;

            // آیتم‌های دستی و سیستم‌های قفل‌شده
            // به عنوان مانع در نظر گرفته می‌شوند.

            List<PlanItem> obstacles = _items
                .Where(x =>
                    x.Floor == _currentFloor &&
                    (!x.IsPhysicalSystem ||
                     (preserveLockedItems && x.Locked)))
                .ToList();

            // سیستم‌های فیزیکی قابل جابه‌جایی
            // از موانع حذف می‌شوند تا بتوانیم
            // دوباره آن‌ها را از ابتدا مرتب کنیم.

            foreach (PlanItem item in systems)
            {
                if (!preserveLockedItems || !item.Locked)
                    item.X = -1;
            }

            // جانمایی سیستم‌ها با توجه به فضای آزاد

            foreach (PlanItem item in systems)
            {
                // سیستم قفل‌شده در موقعیت فعلی باقی می‌ماند.

                if (preserveLockedItems && item.Locked)
                {
                    if (IsInsideCanvas(item))
                        obstacles.Add(item);

                    continue;
                }

                // پیدا کردن اولین فضای خالی

                if (TryFindFreePosition(
                    item.W,
                    item.H,
                    item.Floor,
                    out double x,
                    out double y,
                    obstacles,
                    item.Id))
                {
                    item.X = x;
                    item.Y = y;

                    // سیستم جانمایی‌شده نیز مانع سیستم بعدی می‌شود.

                    obstacles.Add(item);
                }
                else
                {
                    // فضای کافی وجود ندارد.

                    item.X = -1;
                    item.Y = -1;
                }
            }

            UpdateUnplacedSystems();

            MarkDirty();

            RenderCurrentView();
            UpdateDetails();
            UpdateSummary();
        }

        // =====================================================
        // AUTO ARRANGE
        // =====================================================

        private void AutoArrange_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_items.Any(x =>
                x.IsPhysicalSystem &&
                x.Floor == _currentFloor))
            {
                return;
            }

            SaveUndo();

            ArrangePhysicalSystems(
                preserveLockedItems: true);
        }

        // =====================================================
        // SORT SYSTEMS
        // =====================================================

        private void SortSystems_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_items.Any(x =>
                x.IsPhysicalSystem &&
                x.Floor == _currentFloor))
            {
                return;
            }

            SaveUndo();

            ArrangePhysicalSystems(
                preserveLockedItems: true);
        }

        // =====================================================
        // ZOOM
        // =====================================================

        private void SetZoom(double value)
        {
            _zoom = Math.Clamp(value, 0.25, 2.5);

            MapCanvas.LayoutTransform =
                new ScaleTransform(_zoom, _zoom);

            ZoomText.Text =
                $"{_zoom * 100:0}%";
        }

        private void ZoomIn_Click(
            object sender,
            RoutedEventArgs e)
            => SetZoom(_zoom + 0.1);

        private void ZoomOut_Click(
            object sender,
            RoutedEventArgs e)
            => SetZoom(_zoom - 0.1);

        private void ZoomFit_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (MapScroll.ViewportWidth <= 0 ||
                MapScroll.ViewportHeight <= 0)
            {
                SetZoom(1);
                return;
            }

            double zx =
                MapScroll.ViewportWidth / CanvasWidth;

            double zy =
                MapScroll.ViewportHeight / CanvasHeight;

            SetZoom(Math.Min(zx, zy) * 0.95);

            MapScroll.ScrollToHorizontalOffset(0);
            MapScroll.ScrollToVerticalOffset(0);
        }

        private void MapWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
            {
                SetZoom(
                    _zoom + (e.Delta > 0 ? 0.1 : -0.1));

                e.Handled = true;
            }
        }

        // =====================================================
        // TEMPLATE / IMPORT / EXPORT
        // =====================================================

        private void Template_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_items.Any(x => x.Floor == _currentFloor))
            {
                MessageBoxResult result = MessageBox.Show(
                    "قالب پیش‌فرض روی آیتم‌های این طبقه اعمال شود؟",
                    "قالب نقشه",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            SaveUndo();

            var physical = _items
                .Where(x =>
                    x.IsPhysicalSystem &&
                    x.Floor == _currentFloor)
                .ToList();

            for (int i = 0; i < physical.Count; i++)
            {
                physical[i].X =
                    60 + (i % 8) * 145;

                physical[i].Y =
                    70 + (i / 8) * 115;
            }

            MarkDirty();

            RenderCurrentView();

            UpdateUnplacedSystems();
        }

        private void Export_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = "lobix-floorplan.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var document = new LayoutDocument
                {
                    GamenetId = _gamenetId,

                    GamenetName = _gamenetName,

                    SavedAt = DateTime.Now,

                    Items = _items
                        .Select(x => x.Clone())
                        .ToList()
                };

                File.WriteAllText(
                    dialog.FileName,
                    JsonSerializer.Serialize(
                        document,
                        _jsonOptions),
                    Encoding.UTF8);

                Footer.Text = "خروجی JSON ایجاد شد";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "خطا در خروجی: " + ex.Message);
            }
        }


        private void Import_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string json = File.ReadAllText(
                    dialog.FileName,
                    Encoding.UTF8);

                LayoutDocument document =
                    ParseLayoutDocument(json);

                if (document.Items == null)
                {
                    throw new InvalidDataException(
                        "فهرست آیتم‌های فایل معتبر نیست.");
                }

                if (_hasApiContext &&
                    document.GamenetId != 0 &&
                    document.GamenetId != _gamenetId)
                {
                    MessageBox.Show(
                        "این فایل متعلق به گیمنت دیگری است و امکان ورود آن وجود ندارد.",
                        "عدم تطابق گیمنت",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                List<PlanItem> importedManualItems =
                    document.Items
                        .Where(x =>
                            !x.IsPhysicalSystem &&
                            IsValidItem(x))
                        .Select(x => x.Clone())
                        .ToList();

                // ابتدا تغییرات را آماده می‌کنیم؛
                // تا این نقطه نقشه اصلی دست‌نخورده باقی مانده است.
                List<PlanItem> newItems = _items
                    .Where(x => x.IsPhysicalSystem)
                    .Select(x => x)
                    .ToList();

                HashSet<string> usedIds =
                    newItems
                        .Select(x => x.Id)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (PlanItem item in importedManualItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Id) ||
                        !usedIds.Add(item.Id))
                    {
                        item.Id = Guid.NewGuid().ToString("N");

                        usedIds.Add(item.Id);
                    }

                    item.W = Math.Clamp(
                        item.W,
                        10,
                        CanvasWidth - LayoutMargin * 2);

                    item.H = Math.Clamp(
                        item.H,
                        10,
                        CanvasHeight - LayoutMargin * 2);

                    item.X = Math.Clamp(
                        item.X,
                        LayoutMargin,
                        CanvasWidth - LayoutMargin - item.W);

                    item.Y = Math.Clamp(
                        item.Y,
                        LayoutMargin,
                        CanvasHeight - LayoutMargin - item.H);

                    newItems.Add(item);
                }

                SaveUndo();

                _items.Clear();
                _items.AddRange(newItems);

                // اصلاح موقعیت آیتم‌های دستی واردشده در صورت برخورد.
                foreach (PlanItem item in importedManualItems)
                {
                    List<PlanItem> obstacles = _items
                        .Where(x => x.Id != item.Id)
                        .ToList();

                    if (!IsPositionFree(
                        item.X,
                        item.Y,
                        item.W,
                        item.H,
                        item.Floor,
                        obstacles,
                        item.Id))
                    {
                        if (!TryFindFreePosition(
                            item.W,
                            item.H,
                            item.Floor,
                            out double x,
                            out double y,
                            obstacles,
                            item.Id))
                        {
                            item.X = -1;
                            item.Y = -1;
                        }
                        else
                        {
                            item.X = x;
                            item.Y = y;
                        }
                    }
                }

                MarkDirty();

                PopulateFloors();
                UpdateUnplacedSystems();
                RenderCurrentView();
                UpdateSummary();

                Footer.Text =
                    $"{importedManualItems.Count} آیتم دستی وارد شد.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "خطا در ورود فایل:\n" + ex.Message,
                    "خطای Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void ExportPng_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png",
                FileName = "lobix-floorplan.png"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                RenderTargetBitmap bitmap = new(
                    (int)CanvasWidth,
                    (int)CanvasHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32);

                bitmap.Render(MapCanvas);

                var encoder = new PngBitmapEncoder();

                encoder.Frames.Add(
                    BitmapFrame.Create(bitmap));

                using FileStream stream =
                    File.Create(dialog.FileName);

                encoder.Save(stream);

                Footer.Text =
                    "تصویر نقشه خروجی گرفته شد";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "خطا در خروجی تصویر: " + ex.Message);
            }
        }

        // =====================================================
        // KEYBOARD
        // =====================================================

        private void SelectAll_Click(
            object sender,
            RoutedEventArgs e)
        {
            _selectedIds.Clear();

            foreach (PlanItem item in _items.Where(IsItemVisible))
                _selectedIds.Add(item.Id);

            _selected = _items.FirstOrDefault(x =>
                _selectedIds.Contains(x.Id));

            UpdateDetails();

            RenderCurrentView();
        }

        private void Root_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            bool ctrl =
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            bool shift =
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (ctrl && e.Key == Key.S)
            {
                Save_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (ctrl && e.Key == Key.Z)
            {
                Undo_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (ctrl && e.Key == Key.Y)
            {
                Redo_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (ctrl && e.Key == Key.A)
            {
                SelectAll_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (ctrl && e.Key == Key.G)
            {
                if (shift)
                    Ungroup_Click(this, new RoutedEventArgs());
                else
                    Group_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (ctrl && e.Key == Key.D)
            {
                Duplicate_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (e.Key == Key.Delete)
            {
                Remove_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (e.Key == Key.Escape)
            {
                _selected = null;

                _selectedIds.Clear();

                UpdateDetails();

                RenderCurrentView();

                e.Handled = true;

                return;
            }

            if (e.Key == Key.Add ||
                e.Key == Key.OemPlus)
            {
                ZoomIn_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (e.Key == Key.Subtract ||
                e.Key == Key.OemMinus)
            {
                ZoomOut_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (e.Key == Key.F)
            {
                ZoomFit_Click(this, new RoutedEventArgs());

                e.Handled = true;

                return;
            }

            if (_selectedIds.Count > 0 &&
                (e.Key == Key.Left ||
                 e.Key == Key.Right ||
                 e.Key == Key.Up ||
                 e.Key == Key.Down))
            {
                SaveUndo();

                double step = shift ? 10 : 1;

                foreach (PlanItem item in SelectedItems())
                {
                    if (item.Locked)
                        continue;

                    switch (e.Key)
                    {
                        case Key.Left:
                            item.X -= step;
                            break;

                        case Key.Right:
                            item.X += step;
                            break;

                        case Key.Up:
                            item.Y -= step;
                            break;

                        case Key.Down:
                            item.Y += step;
                            break;
                    }

                    item.X = Math.Clamp(
                        item.X,
                        0,
                        CanvasWidth - item.W);

                    item.Y = Math.Clamp(
                        item.Y,
                        0,
                        CanvasHeight - item.H);
                }

                MarkDirty();

                RenderCurrentView();

                UpdateUnplacedSystems();

                e.Handled = true;
            }
        }

        // =====================================================
        // INITIALIZATION
        // =====================================================

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            _isInitialized = false;
        }

        // =====================================================
        // API DIAGNOSTICS
        // =====================================================


        private async void DebugFloorPlanApi_Click(
            object sender,
            RoutedEventArgs e)
        {
            var result = new StringBuilder();

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };

            try
            {
                result.AppendLine("========== GAMENET INFO ==========");
                result.AppendLine("GAMENET ID: " + _gamenetId);
                result.AppendLine("GAMENET NAME: " + _gamenetName);
                result.AppendLine("API CONTEXT: " + _hasApiContext);
                result.AppendLine("SYSTEMS LOADED: " + _systemsLoaded);
                result.AppendLine("RESERVATIONS IN MEMORY: " + _reservations.Count);
                result.AppendLine();

                if (!_hasApiContext ||
                    _gamenetId <= 0 ||
                    string.IsNullOrWhiteSpace(_apiService.Token))
                {
                    result.AppendLine(
                        "ERROR: اطلاعات اتصال یا شناسه گیمنت معتبر نیست.");
                }
                else
                {
                    using var extendedApi =
                        new LobixExtendedApiService(_apiService.Token);

                    // ==========================================
                    // SYSTEMS API
                    // ==========================================

                    result.AppendLine("========== SYSTEMS API ==========");

                    try
                    {
                        using var systems =
                            await extendedApi.GetSystemsAsync(_gamenetId);

                        result.AppendLine(
                            JsonSerializer.Serialize(
                                systems.RootElement,
                                jsonOptions));
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine("SYSTEMS ERROR:");
                        result.AppendLine(ex.ToString());
                    }

                    result.AppendLine();

                    // ==========================================
                    // LAYOUT API
                    // ==========================================

                    result.AppendLine("========== LAYOUT API ==========");

                    try
                    {
                        using var layout =
                            await extendedApi.GetLayoutAsync(_gamenetId);

                        result.AppendLine(
                            JsonSerializer.Serialize(
                                layout.RootElement,
                                jsonOptions));
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine("LAYOUT ERROR:");
                        result.AppendLine(ex.ToString());
                    }

                    result.AppendLine();

                    // ==========================================
                    // SYNC API - RAW RESPONSE
                    // ==========================================

                    result.AppendLine("========== SYNC API ==========");

                    try
                    {
                        string from = DateTime.Today.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture);

                        string to = DateTime.Today.AddDays(30).ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture);

                        result.AppendLine("FROM: " + from);
                        result.AppendLine("TO: " + to);
                        result.AppendLine();

                        GamenetSyncResponse sync =
                            await _apiService.SyncAsync(
                                _gamenetId,
                                from,
                                to);

                        if (sync == null)
                        {
                            result.AppendLine("SYNC RESPONSE IS NULL");
                        }
                        else
                        {
                            result.AppendLine(
                                "SYNC PRODUCT ID: " +
                                (sync.Data?.ProductId.ToString() ?? "NULL"));

                            result.AppendLine();

                            result.AppendLine(
                                JsonSerializer.Serialize(
                                    sync,
                                    jsonOptions));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine("SYNC ERROR:");
                        result.AppendLine(ex.ToString());
                    }
                }

                // ==========================================
                // MAPPED RESERVATIONS
                // ==========================================

                result.AppendLine();
                result.AppendLine("========== MAPPED RESERVATIONS ==========");
                result.AppendLine(
                    "TOTAL: " + _reservations.Count);

                result.AppendLine();

                result.AppendLine(
                    JsonSerializer.Serialize(
                        _reservations.Select(r => new
                        {
                            r.Id,
                            r.Source,
                            r.Title,
                            r.Date,
                            r.StartTime,
                            r.Customer,
                            r.Phone,
                            r.GameName,
                            r.Status,
                            r.SystemNumber,
                            r.VariationId,
                            Hours = r.Hours
                        }).ToList(),
                        jsonOptions));

                result.AppendLine();

                // ==========================================
                // PHYSICAL SYSTEMS IN MEMORY + VISIBILITY
                // ==========================================

                var physicalSystems = _items
                    .Where(i => i.IsPhysicalSystem)
                    .ToList();

                result.AppendLine("========== MAP VISIBILITY DIAGNOSTICS ==========");
                result.AppendLine("GAMENET ID: " + _gamenetId);
                result.AppendLine("CURRENT FLOOR: " + _currentFloor);
                result.AppendLine("TOTAL ITEMS: " + _items.Count);
                result.AppendLine("PHYSICAL SYSTEMS: " + physicalSystems.Count);

                result.AppendLine(
                    "VISIBLE SYSTEMS: " +
                    physicalSystems.Count(IsItemVisible));

                result.AppendLine(
                    "UNPLACED SYSTEMS: " +
                    physicalSystems.Count(i => i.X < 0 || i.Y < 0));

                result.AppendLine();

                result.AppendLine("========== FLOOR DISTRIBUTION ==========");

                foreach (var group in physicalSystems
                    .GroupBy(x => x.Floor)
                    .OrderBy(x => x.Key))
                {
                    result.AppendLine(
                        $"{group.Key}: {group.Count()} systems");
                }

                result.AppendLine();

                result.AppendLine("========== PHYSICAL SYSTEMS IN MEMORY ==========");

                result.AppendLine(
                    JsonSerializer.Serialize(
                        physicalSystems.Select(i => new
                        {
                            i.Id,
                            i.Name,
                            i.SystemId,
                            i.SystemNumber,
                            i.VariationId,
                            i.Status,
                            i.Type,
                            i.Floor,
                            i.X,
                            i.Y,
                            i.W,
                            i.H,
                            IsVisible = IsItemVisible(i),
                            i.IsPhysicalSystem
                        }).ToList(),
                        jsonOptions));
            }
            catch (Exception ex)
            {
                result.AppendLine();
                result.AppendLine("========== GENERAL ERROR ==========");
                result.AppendLine(ex.ToString());
            }

            var textBox = new TextBox
            {
                Text = result.ToString(),
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Margin = new Thickness(10)
            };

            var copyButton = new Button
            {
                Content = "کپی کل گزارش",
                Height = 40,
                Margin = new Thickness(10),
                Padding = new Thickness(15, 5, 15, 5)
            };

            copyButton.Click += (_, _) =>
            {
                Clipboard.SetText(textBox.Text);

                MessageBox.Show(
                    "گزارش با موفقیت کپی شد.",
                    "Lobix Diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            };

            var panel = new DockPanel();

            DockPanel.SetDock(copyButton, Dock.Bottom);

            panel.Children.Add(copyButton);
            panel.Children.Add(textBox);

            var window = new Window
            {
                Title = "Lobix API Diagnostics",
                Width = 1000,
                Height = 800,
                MinWidth = 600,
                MinHeight = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                FlowDirection = FlowDirection.LeftToRight,
                Background = Brushes.White,
                Content = panel
            };

            window.ShowDialog();
        }

    }
}
