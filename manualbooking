using LobixApp.Models;
using LobixApp.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using LobixApp;
using LobixApp.Utilities;
using LobixApp.Services.Sync;

namespace LobixApp.Views
{
    public partial class ManualBookingView : UserControl
    {

        // =====================================================
        // API STATUS — SIDEBAR COMMUNICATION
        // =====================================================

        public event Action<string, Brush, string>? ApiStatusChanged;

        private void PublishApiStatus(
            string status,
            Brush color,
            string lastSync)
        {
            ApiStatusChanged?.Invoke(status, color, lastSync);
        }

        // =====================================================
        // SERVICES
        // =====================================================

        private readonly LobixApiService _apiService;
        private readonly Dictionary<int, TextBlock> _systemCardLiveStatusTextBlocks = new();
        // =====================================================
        // SESSION
        // =====================================================

        private readonly GamenetInfo _gamenet;
        private readonly OperatorSession _session;

        // =====================================================
        // VARIATION IDS
        // =====================================================

        // =====================================================
        // GAMENET DATA
        // =====================================================

        private string _gamenetLogoUrl = string.Empty;

        private readonly Dictionary<int, string> _variationNames =
            new();

        private readonly Dictionary<int, decimal> _variationPrices =
            new();

        private readonly Dictionary<int, int> _variationCapacities =
            new();

        private readonly Dictionary<int, string> _variationImageUrls =
            new();

        private readonly Dictionary<int, List<string>> _variationSystemNumbers = new();
        // Maps physical display numbers to stable server-side system IDs (API v2.3).
        private readonly Dictionary<string, string> _physicalSystemIdsByNumber = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _physicalSystemVariationByNumber = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<(int Id, string Name)>> _variationGames = new();
        private readonly Dictionary<int, HashSet<string>> _selectedSystemNumbers = new();
        private readonly Dictionary<int, WrapPanel> _systemTilePanels = new();
        private readonly Dictionary<int, Dictionary<string, Button>> _systemTileButtons = new();
        private readonly Dictionary<int, ComboBox> _systemGameCombos = new();
        private List<ManualBooking> _selectedDateManualBookings = new();
        private List<OnlineBooking> _selectedDateOnlineBookings = new();

        // =====================================================
        // AVAILABILITY
        // =====================================================

        private readonly Dictionary<int, AvailabilityResponse> _availability =
            new();

        // =====================================================
        // BOOKING SELECTION
        // =====================================================

        private readonly Dictionary<int, int> _selectedQuantities =
            new();

        // Controls generated dynamically for each API variation.
        private readonly Dictionary<int, Border> _systemCardBorders = new();
        private readonly Dictionary<int, Image> _systemCardImages = new();
        private readonly Dictionary<int, TextBlock> _systemCardCapacityTextBlocks = new();
        private readonly Dictionary<int, TextBlock> _systemCardPriceTextBlocks = new();
        private readonly Dictionary<int, TextBlock> _systemCardQuantityTextBlocks = new();
        private readonly Dictionary<int, Button> _systemCardMinusButtons = new();
        private readonly Dictionary<int, Button> _systemCardPlusButtons = new();

        private string? _selectedStartHour;

        private int _selectedBookingHours = 1;

        private bool _durationExplicitlySelected;

        private int _wizardStep;

        private int _workStart = 8;

        private int _workEnd = 24;

        // =====================================================
        // SELECTED BOOKING DATE
        // =====================================================

        private DateTime _selectedBookingDate =
            DateTime.Today;

        // =====================================================
        // PERSIAN CALENDAR
        // =====================================================

        private readonly PersianCalendar _persianCalendar =
            new PersianCalendar();

        private int _calendarPersianYear;

        private int _calendarPersianMonth;

        // =====================================================
        // LAST CREATED BOOKING
        // =====================================================

        private string? _lastCreatedBookingId;

        private DateTime? _lastSuccessfulSyncAt;

        private bool _availabilityLoadSucceeded;

        private bool _todayBookingsLoadSucceeded;
        private GamenetSyncResponse? _lastSelectedDateSync;
        private DateTime? _lastSelectedDateSyncDate;

        private bool _isRetryingSync;
        private bool _initialBookingDataLoaded;
        private int _manualLoadingDepth;


        // =====================================================
        // LOAD GAMENET LOGO
        // =====================================================

        private async Task LoadGamenetLogoSafelyAsync()
        {
            try { await LoadGamenetLogoAsync(); }
            catch { /* Logo is decorative; a network/image failure must not block booking. */ }
        }

        private async Task LoadGamenetLogoAsync()
        {
            if (string.IsNullOrWhiteSpace(_gamenetLogoUrl))
                return;

            using HttpClient client =
                new HttpClient();

            byte[] imageBytes =
                await client.GetByteArrayAsync(
                    _gamenetLogoUrl
                );

            using MemoryStream stream =
                new MemoryStream(imageBytes);

            BitmapImage bitmap =
                new BitmapImage();

            bitmap.BeginInit();

            bitmap.StreamSource =
                stream;

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.EndInit();

            bitmap.Freeze();

            GamenetLogoImage.Source =
                bitmap;
        }


        // =====================================================
        // LOAD VARIATION IMAGE
        // =====================================================

        private async Task LoadVariationImageAsync(
            int variationId,
            Image imageControl)
        {
            if (!_variationImageUrls.TryGetValue(
                    variationId,
                    out string? imageUrl))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return;
            }

            using HttpClient client =
                new HttpClient();

            byte[] imageBytes =
                await client.GetByteArrayAsync(
                    imageUrl
                );

            using MemoryStream stream =
                new MemoryStream(imageBytes);

            BitmapImage bitmap =
                new BitmapImage();

            bitmap.BeginInit();

            bitmap.StreamSource =
                stream;

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.EndInit();

            bitmap.Freeze();

            imageControl.Source =
                bitmap;
        }


        // =====================================================
        // CONSTRUCTOR
        // =====================================================

        public ManualBookingView(
            GamenetInfo gamenet,
            OperatorSession session)
        {
            InitializeComponent();

            _apiService =
                new LobixApiService();

            _gamenet =
                gamenet;

            _session =
                session;

            _apiService.SetAuthentication(
                _session.Token
            );

            LoadGamenetInformation();

            InitializeBookingForm();
            Loaded += async (_, _) => await EnsureInlineMapReadyAsync();
        }


        // =====================================================
        // GAMENET INFORMATION
        // =====================================================

        private FloorPlanBookingMapControl? _inlineFloorMap;

        private async Task EnsureInlineMapReadyAsync()
        {
            if (_inlineFloorMap == null)
            {
                _inlineFloorMap = new FloorPlanBookingMapControl(
                    _gamenet,
                    _session.Token,

                    // انتخاب یا لغو انتخاب سیستم
                    (variationId, systemNumber) =>
                    {
                        TogglePhysicalSystem(variationId, systemNumber);
                        UpdateTotal();
                        BuildBookingReviewSummary();
                    },

                    // بررسی انتخاب جدید
                    (variationId, systemNumber) =>
                        _selectedSystemNumbers.TryGetValue(variationId, out var selected)
                        && selected.Contains(systemNumber),

                    // بررسی رزروهای قبلی
                    (variationId, systemNumber) =>
    !string.IsNullOrWhiteSpace(_selectedStartHour)
    && TryGetHourNumber(_selectedStartHour, out int startHour)
    && IsPhysicalSystemBooked(
        variationId,
        systemNumber,
        startHour,
        _selectedBookingHours)
                );

                InlineMapHost.Child = _inlineFloorMap;
            }

          InlineMapHost.Visibility = Visibility.Visible;
InlineMapHost.Height = 480;

await _inlineFloorMap.EnsureLoadedAsync();

_inlineFloorMap.RefreshBookedStates();
        }

        private async void OpenFloorPlanMapButton_Click(object sender, RoutedEventArgs e)
        {
            // The floor map is permanently visible in the booking workspace.
            // System selection is performed exclusively by clicking map objects.
            await EnsureInlineMapReadyAsync();
        }

        private void LoadGamenetInformation()
        {
            GamenetNameTextBlock.Text =
                _gamenet.Name;

            OperatorNameTextBlock.Text =
                string.IsNullOrWhiteSpace(
                    _session.DisplayName)
                    ? _session.Username
                    : _session.DisplayName;

            GamenetStatusTextBlock.Text =
                _gamenet.Status == "publish"
                    ? "فعال"
                    : _gamenet.Status;

            if (_gamenet.Status == "publish")
            {
                GamenetStatusBorder.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            232,
                            247,
                            234
                        )
                    );

                GamenetStatusTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            46,
                            139,
                            60
                        )
                    );
            }
            else
            {
                GamenetStatusBorder.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            244,
                            229
                        )
                    );

                GamenetStatusTextBlock.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            180,
                            100,
                            20
                        )
                    );
            }
        }


        // =====================================================
        // INITIALIZE BOOKING FORM
        // =====================================================

        private void InitializeBookingForm()
        {
            // -------------------------------------------------
            // DATE
            // -------------------------------------------------

            _selectedBookingDate =
                DateTime.Today;

            _calendarPersianYear =
                _persianCalendar.GetYear(
                    _selectedBookingDate
                );

            _calendarPersianMonth =
                _persianCalendar.GetMonth(
                    _selectedBookingDate
                );

            UpdatePersianDateText();

            PersianDateTodayButton.Click +=
                PersianDateTodayButton_Click;

            PersianDateSelectorButton.Click +=
                PersianDateSelectorButton_Click;

            PersianCalendarPreviousButton.Click +=
                PersianCalendarPreviousButton_Click;

            PersianCalendarNextButton.Click +=
                PersianCalendarNextButton_Click;


            // -------------------------------------------------
            // SYSTEM CARDS
            // Cards are generated dynamically after API variations load.
            // -------------------------------------------------


            // -------------------------------------------------
            // PHONE INPUT FILTER
            // -------------------------------------------------

            BookingPhoneTextBox.MaxLength = 11;

            BookingPhoneTextBox.PreviewTextInput +=
                BookingPhoneTextBox_PreviewTextInput;

            BookingPhoneTextBox.PreviewKeyDown +=
                BookingPhoneTextBox_PreviewKeyDown;

            BookingCustomerTextBox.TextChanged += (_, _) => BuildBookingReviewSummary();
            BookingCustomerIdTextBox.TextChanged += (_, _) => BuildBookingReviewSummary();
            BookingPhoneTextBox.TextChanged += (_, _) => BuildBookingReviewSummary();
            BookingNoteTextBox.TextChanged += (_, _) => BuildBookingReviewSummary();
            BookingGameTextBox.TextChanged += (_, _) => BuildBookingReviewSummary();

            // -------------------------------------------------
            // CREATE BOOKING
            // -------------------------------------------------

            RetrySyncButton.Click +=
                RetrySyncButton_Click;

            _wizardStep = 0;
            ShowBookingWizardStep(0);


            // -------------------------------------------------
            // RESET
            // -------------------------------------------------

            ResetBookingForm();


            // -------------------------------------------------
            // LOAD DATA
            // -------------------------------------------------

            _ = InitializeBookingDataAsync();
        }


        // =====================================================
        // MANUAL BOOKING WIZARD
        // =====================================================

        private void ShowBookingWizardStep(int step)
        {
            _wizardStep = Math.Max(0, Math.Min(5, step));
            // Single-screen operator workflow: every section remains visible.
            // _wizardStep is retained only for backward-compatible event handlers.
            BookingDateStepPanel.Visibility = Visibility.Visible;
            BookingHourStepPanel.Visibility = Visibility.Visible;
            BookingDurationStepPanel.Visibility = Visibility.Visible;
            BookingSystemsStepPanel.Visibility = Visibility.Visible;
            BookingCustomerStepPanel.Visibility = Visibility.Visible;
            BookingWizardNavigationPanel.Visibility = Visibility.Collapsed;
            BookingReviewPanel.Visibility = Visibility.Collapsed;
            BookingBackButton.IsEnabled = _wizardStep > 0;
            BookingNextButton.Content = _wizardStep == 4 ? "مرور و تأیید ←" : "ادامه ←";
            BookingWizardMessageTextBlock.Text = "همه اطلاعات رزرو را همین‌جا وارد کنید؛ ظرفیت هنگام ثبت دوباره بررسی می‌شود.";
            BuildBookingReviewSummary();
        }

        private async void BookingNextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BookingNextButton.IsEnabled = false;

                switch (_wizardStep)
                {
                    case 0:
                        if (_selectedBookingDate.Date < DateTime.Today)
                            throw new Exception("امکان رزرو برای تاریخ گذشته وجود ندارد.");

                        if (!_availabilityLoadSucceeded)
                        {
                            SetBookingInfo("در حال بارگذاری ظرفیت تاریخ انتخاب‌شده...");
                            await LoadAvailabilityAsync(_selectedBookingDate);
                        }

                        if (!_availabilityLoadSucceeded)
                            throw new Exception("ظرفیت این تاریخ بارگذاری نشده است. اتصال را بررسی و دوباره تلاش کنید.");

                        ShowBookingWizardStep(1);
                        break;

                    case 1:
                        if (string.IsNullOrWhiteSpace(_selectedStartHour))
                            throw new Exception("برای ادامه، ساعت ورود مشتری را انتخاب کنید.");

                        ShowBookingWizardStep(2);
                        break;

                    case 2:
                        if (!_durationExplicitlySelected)
                            throw new Exception("مدت بازی را انتخاب کنید.");

                        string? durationError = GetDurationValidationError(_selectedBookingHours);
                        if (!string.IsNullOrWhiteSpace(durationError))
                            throw new Exception(durationError);

                        ShowBookingWizardStep(3);
                        break;

                    case 3:
                        if (GetSelectedSystemCount() < 1)
                            throw new Exception("حداقل یک سیستم آزاد را انتخاب کنید.");

                        if (!TryGetHourNumber(_selectedStartHour!, out int selectedStartHour))
                            throw new Exception("ساعت ورود انتخاب‌شده معتبر نیست.");

                        string? capacityError = GetSelectedSystemsCapacityError(
                            selectedStartHour,
                            _selectedBookingHours);
                        if (!string.IsNullOrWhiteSpace(capacityError))
                            throw new Exception(capacityError);

                        ShowBookingWizardStep(4);
                        break;

                    case 4:
                        ValidateCustomerFieldsForReview();
                        UpdateTotal();
                        BuildBookingReviewSummary();
                        ShowBookingWizardStep(5);
                        break;
                }
            }
            catch (Exception ex)
            {
                SetBookingError(UserFacingErrors.GetMessage(ex, "عملیات انجام نشد. اطلاعات را بررسی کنید و دوباره تلاش کنید."));
            }
            finally
            {
                BookingNextButton.IsEnabled = true;
            }
        }

        private void BookingBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_wizardStep > 0 && _wizardStep < 5)
                ShowBookingWizardStep(_wizardStep - 1);
        }

        private void BackToCustomerButton_Click(object sender, RoutedEventArgs e)
        {
            ShowBookingWizardStep(4);
        }

        private void ValidateCustomerFieldsForReview()
        {
            string customer = BookingCustomerTextBox.Text.Trim();
            string customerId = BookingCustomerIdTextBox.Text.Trim();
            string phone = ConvertPersianDigitsToEnglish(BookingPhoneTextBox.Text.Trim());

            if (string.IsNullOrWhiteSpace(customer) &&
                string.IsNullOrWhiteSpace(customerId) &&
                string.IsNullOrWhiteSpace(phone))
                throw new Exception("حداقل یکی از نام مشتری، شماره موبایل یا شناسه مشتری را وارد کنید.");

            if (!string.IsNullOrWhiteSpace(phone) &&
                (phone.Length != 11 || !phone.StartsWith("09", StringComparison.Ordinal) || !phone.All(char.IsDigit)))
                throw new Exception("شماره موبایل باید ۱۱ رقم و با ۰۹ شروع شود؛ مثال: ۰۹۱۲۳۴۵۶۷۸۹");

            if (GetSelectedSystemCount() < 1)
                throw new Exception("هیچ سیستمی برای رزرو انتخاب نشده است.");

            if (string.IsNullOrWhiteSpace(_selectedStartHour))
                throw new Exception("ساعت ورود انتخاب نشده است.");

            if (!_durationExplicitlySelected)
                throw new Exception("مدت بازی انتخاب نشده است.");
        }

        private void BuildBookingReviewSummary()
        {
            if (BookingReviewSummaryTextBlock == null)
                return;

            string systems = string.Join("، ", _selectedQuantities
                .Where(item => item.Value > 0)
                .Select(item => $"{GetVariationName(item.Key)} × {ToPersianNumber(item.Value)}"));

            string game = BookingGameTextBox.Text.Trim();
            string customer = BookingCustomerTextBox.Text.Trim();
            string phone = ConvertPersianDigitsToEnglish(BookingPhoneTextBox.Text.Trim());
            string customerId = BookingCustomerIdTextBox.Text.Trim();
            decimal total = _selectedQuantities.Sum(item =>
                item.Value > 0 && _variationPrices.TryGetValue(item.Key, out decimal price)
                    ? price * item.Value * _selectedBookingHours : 0m);

            BookingReviewSummaryTextBlock.Text =
                $"تاریخ: {PersianDateTextBlock.Text}\n" +
                $"ساعت ورود: {ToPersianNumber(_selectedStartHour ?? "—")}\n" +
                $"مدت بازی: {ToPersianNumber(_selectedBookingHours)} ساعت\n" +
                $"سیستم‌ها: {(string.IsNullOrWhiteSpace(systems) ? "انتخاب نشده" : systems)}\n" +
                $"بازی: {(string.IsNullOrWhiteSpace(game) ? "مشخص نشده" : game)}\n" +
                $"مشتری: {(string.IsNullOrWhiteSpace(customer) ? "—" : customer)}\n" +
                $"موبایل: {(string.IsNullOrWhiteSpace(phone) ? "—" : ToPersianNumber(phone))}\n" +
                $"شناسه مشتری: {(string.IsNullOrWhiteSpace(customerId) ? "—" : customerId)}\n" +
                $"مبلغ نهایی: {ToPersianNumber(total.ToString("N0", CultureInfo.InvariantCulture))} تومان";
        }

        // =====================================================
        // PHONE INPUT FILTER
        // =====================================================

        private void BookingPhoneTextBox_PreviewTextInput(
            object sender,
            System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled =
                !char.IsDigit(e.Text, 0);
        }


        // =====================================================
        // PHONE KEYBOARD FILTER
        // =====================================================

        private void BookingPhoneTextBox_PreviewKeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space)
            {
                e.Handled = true;
            }
        }
        // =====================================================
        // UPDATE PERSIAN DATE TEXT
        // =====================================================

        private void UpdatePersianDateText()
        {
            int year =
                _persianCalendar.GetYear(
                    _selectedBookingDate
                );

            int month =
                _persianCalendar.GetMonth(
                    _selectedBookingDate
                );

            int day =
                _persianCalendar.GetDayOfMonth(
                    _selectedBookingDate
                );

            string monthText =
                month.ToString(
                    "00",
                    CultureInfo.InvariantCulture
                );

            string dayText =
                day.ToString(
                    "00",
                    CultureInfo.InvariantCulture
                );

            PersianDateTextBlock.Text =
                $"{ToPersianNumber(year)}/" +
                $"{ToPersianNumber(monthText)}/" +
                $"{ToPersianNumber(dayText)}";

            // دکمه امروز فقط وقتی تاریخ امروز انتخاب شده، حالت فعال دارد.
            if (PersianDateTodayButton != null)
            {
                bool isToday = _selectedBookingDate.Date == DateTime.Today;
                PersianDateTodayButton.Background = new SolidColorBrush(
                    isToday ? Color.FromRgb(219, 60, 69) : Color.FromRgb(32, 34, 44));
                PersianDateTodayButton.BorderBrush = new SolidColorBrush(
                    isToday ? Color.FromRgb(219, 60, 69) : Color.FromRgb(52, 55, 68));
                PersianDateTodayButton.Foreground = Brushes.White;
            }
        }


        // =====================================================
        // TODAY BUTTON
        // =====================================================

        private async void PersianDateTodayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                PersianDateTodayButton.IsEnabled =
                    false;

                _selectedBookingDate =
                    DateTime.Today;

                _calendarPersianYear =
                    _persianCalendar.GetYear(
                        _selectedBookingDate
                    );

                _calendarPersianMonth =
                    _persianCalendar.GetMonth(
                        _selectedBookingDate
                    );

                UpdatePersianDateText();
                _selectedStartHour = null;
                _selectedBookingHours = 1;
                _durationExplicitlySelected = false;

                await LoadAvailabilityAsync(
                    _selectedBookingDate
                );
            }
            catch (Exception ex)
            {
                SetBookingError(UserFacingErrors.GetMessage(ex,
                    "دریافت اطلاعات تاریخ امروز ناموفق بود. اتصال را بررسی و دوباره تلاش کنید."));
            }
            finally
            {
                PersianDateTodayButton.IsEnabled =
                    true;
            }
        }


        // =====================================================
        // OPEN PERSIAN CALENDAR
        // =====================================================

        private void PersianDateSelectorButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _calendarPersianYear =
                _persianCalendar.GetYear(
                    _selectedBookingDate
                );

            _calendarPersianMonth =
                _persianCalendar.GetMonth(
                    _selectedBookingDate
                );

            RenderPersianCalendar();

            PersianCalendarPopup.IsOpen =
                true;
        }


        // =====================================================
        // PREVIOUS MONTH
        // =====================================================

        private void PersianCalendarPreviousButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _calendarPersianMonth--;

            if (_calendarPersianMonth < 1)
            {
                _calendarPersianMonth = 12;
                _calendarPersianYear--;
            }

            RenderPersianCalendar();
        }


        // =====================================================
        // NEXT MONTH
        // =====================================================

        private void PersianCalendarNextButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _calendarPersianMonth++;

            if (_calendarPersianMonth > 12)
            {
                _calendarPersianMonth = 1;
                _calendarPersianYear++;
            }

            RenderPersianCalendar();
        }


        // =====================================================
        // RENDER PERSIAN CALENDAR
        // =====================================================

        private void RenderPersianCalendar()
        {
            PersianCalendarDaysGrid.Children.Clear();

            string monthName =
                _calendarPersianMonth switch
                {
                    1 => "فروردین",
                    2 => "اردیبهشت",
                    3 => "خرداد",
                    4 => "تیر",
                    5 => "مرداد",
                    6 => "شهریور",
                    7 => "مهر",
                    8 => "آبان",
                    9 => "آذر",
                    10 => "دی",
                    11 => "بهمن",
                    12 => "اسفند",
                    _ => ""
                };

            PersianCalendarMonthTextBlock.Text =
                $"{monthName} {ToPersianNumber(_calendarPersianYear)}";

            DateTime firstDay =
                _persianCalendar.ToDateTime(
                    _calendarPersianYear,
                    _calendarPersianMonth,
                    1,
                    0,
                    0,
                    0,
                    0
                );

            int firstDayOffset =
                ((int)firstDay.DayOfWeek + 1) % 7;

            for (
                int i = 0;
                i < firstDayOffset;
                i++)
            {
                PersianCalendarDaysGrid.Children.Add(
                    new Border()
                );
            }

            int daysInMonth =
                _persianCalendar.GetDaysInMonth(
                    _calendarPersianYear,
                    _calendarPersianMonth
                );

            int selectedYear =
                _persianCalendar.GetYear(
                    _selectedBookingDate
                );

            int selectedMonth =
                _persianCalendar.GetMonth(
                    _selectedBookingDate
                );

            int selectedDay =
                _persianCalendar.GetDayOfMonth(
                    _selectedBookingDate
                );

            for (
                int day = 1;
                day <= daysInMonth;
                day++)
            {
                DateTime currentDate =
                    _persianCalendar.ToDateTime(
                        _calendarPersianYear,
                        _calendarPersianMonth,
                        day,
                        0,
                        0,
                        0,
                        0
                    );

                bool isSelected =
                    _calendarPersianYear == selectedYear &&
                    _calendarPersianMonth == selectedMonth &&
                    day == selectedDay;

                bool isPast =
                    currentDate.Date <
                    DateTime.Today;

                Button dayButton =
                    new Button
                    {
                        Content =
                            ToPersianNumber(day),

                        Width =
                            38,

                        Height =
                            38,

                        Margin =
                            new Thickness(2),

                        FontSize =
                            13,

                        FontFamily =
                            new FontFamily("Peyda"),

                        // کمی ضخیم‌تر از حالت قبلی
                        FontWeight =
                            FontWeights.SemiBold,

                        BorderThickness =
                            new Thickness(0),

                        Cursor =
                            System.Windows.Input.Cursors.Hand,

                        IsEnabled =
                            !isPast,

                        Tag =
                            currentDate
                    };

                if (isSelected)
                {
                    dayButton.Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                219,
                                60,
                                69
                            )
                        );

                    dayButton.Foreground =
                        Brushes.White;
                }
                else if (isPast)
                {
                    dayButton.Background =
                        Brushes.Transparent;

                    dayButton.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                190,
                                190,
                                195
                            )
                        );
                }
                else
                {
                    dayButton.Background =
                        Brushes.Transparent;

                    dayButton.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                28,
                                30,
                                42
                            )
                        );
                }

                dayButton.Click +=
                    PersianCalendarDayButton_Click;

                PersianCalendarDaysGrid.Children.Add(
                    dayButton
                );
            }
        }


        // =====================================================
        // SELECT PERSIAN CALENDAR DAY
        // =====================================================

        private async void PersianCalendarDayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not DateTime selectedDate)
                return;

            if (selectedDate.Date < DateTime.Today)
            {
                SetBookingError(
                    "امکان انتخاب تاریخ گذشته وجود ندارد."
                );

                return;
            }

            try
            {
                _selectedBookingDate =
                    selectedDate.Date;

                _calendarPersianYear =
                    _persianCalendar.GetYear(
                        selectedDate
                    );

                _calendarPersianMonth =
                    _persianCalendar.GetMonth(
                        selectedDate
                    );

                UpdatePersianDateText();
                _selectedStartHour = null;
                _selectedBookingHours = 1;
                _durationExplicitlySelected = false;

                PersianCalendarPopup.IsOpen =
                    false;

                await LoadAvailabilityAsync(
                    _selectedBookingDate
                );
            }
            catch (Exception ex)
            {
                SetBookingError(UserFacingErrors.GetMessage(ex,
                    "دریافت اطلاعات تاریخ انتخاب‌شده ناموفق بود. اتصال را بررسی و دوباره تلاش کنید."));
            }
        }


        // =====================================================
        // RESET FORM
        // =====================================================

        private void ResetBookingForm()
        {
            foreach (TextBlock quantityTextBlock in _systemCardQuantityTextBlocks.Values)
            {
                quantityTextBlock.Text = "۰";
            }

            BookingHoursPanel.Children.Clear();

            if (BookingDurationPanel != null)
            {
                BookingDurationPanel.Children.Clear();
            }

            BookingTotalTextBlock.Text =
                "۰ تومان";

            BookingFormStatusTextBlock.Foreground =
                Brushes.Gray;

            BookingFormStatusTextBlock.Text =
                "در حال دریافت اطلاعات گیمنت...";

            _selectedQuantities.Clear();

            _selectedStartHour = null;

            _selectedBookingHours = 1;
            _durationExplicitlySelected = false;

            _lastCreatedBookingId = null;

            _selectedBookingDate =
                DateTime.Today;

            _calendarPersianYear =
                _persianCalendar.GetYear(
                    _selectedBookingDate
                );

            _calendarPersianMonth =
                _persianCalendar.GetMonth(
                    _selectedBookingDate
                );

            UpdatePersianDateText();

            UpdateSystemSelectionVisuals();

            UpdateTotal();
        }


        // =====================================================
        // LOADING FEEDBACK
        // =====================================================

        private void SetManualLoading(bool isLoading, string message = "در حال دریافت اطلاعات از سرور لابیکس...")
        {
            if (ManualLoadingOverlay == null) return;

            // After the initial setup, background refreshes do not cover the whole screen.
            if (isLoading && _initialBookingDataLoaded) return;

            if (isLoading)
            {
                _manualLoadingDepth++;
                ManualLoadingDescriptionText.Text = message;
                if (ManualLoadingOverlay.Visibility != Visibility.Visible)
                {
                    ManualLoadingOverlay.Opacity = 0;
                    ManualLoadingOverlay.Visibility = Visibility.Visible;
                    ManualLoadingOverlay.BeginAnimation(UIElement.OpacityProperty,
                        new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
                }
                return;
            }

            _manualLoadingDepth = Math.Max(0, _manualLoadingDepth - 1);
            if (_manualLoadingDepth == 0)
            {
                ManualLoadingOverlay.BeginAnimation(UIElement.OpacityProperty, null);
                ManualLoadingOverlay.Visibility = Visibility.Collapsed;
                ManualLoadingOverlay.Opacity = 1;
            }
        }

        // =====================================================
        // INITIAL LOAD
        // =====================================================

        private async Task InitializeBookingDataAsync()
        {
            SetManualLoading(true, "در حال دریافت اطلاعات گیمنت، دستگاه‌ها و ظرفیت رزرو...");
            try
            {
                await LoadGamenetDetailsAsync();

                await LoadAvailabilityAsync(_selectedBookingDate);
                ManualLoadingDescriptionText.Text = "در حال دریافت رزروهای امروز...";

                // -------------------------------------------------
                // LOAD TODAY BOOKINGS COUNT
                // -------------------------------------------------

                await LoadTodayBookingsAsync();
            }
            catch (Exception ex)
            {
                SetBookingError(UserFacingErrors.GetMessage(ex,
                    "دریافت اطلاعات گیمنت ناموفق بود. اتصال را بررسی و دوباره تلاش کنید."));
            }
            finally
            {
                _initialBookingDataLoaded = true;
                SetManualLoading(false);
            }
        }


        // =====================================================
        // API SYNC / RETRY
        // تلاش مجدد برای اتصال و همگام‌سازی اطلاعات
        // =====================================================

        private async void RetrySyncButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDataAsync();
        }

        public async Task RefreshDataAsync()
        {
            if (_isRetryingSync) return;

            _isRetryingSync = true;
            SetManualLoading(true, "در حال اتصال مجدد و همگام‌سازی اطلاعات...");
            if (RetrySyncButton != null)
            {
                RetrySyncButton.IsEnabled = false;
                RetrySyncButton.Content = "در حال بررسی...";
            }

            ApiConnectionStatusTextBlock.Text = "در حال بررسی اتصال به سرویس...";
            ApiConnectionStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(232, 185, 106));
            _availabilityLoadSucceeded = false;
            _todayBookingsLoadSucceeded = false;

            try
            {
                await LoadGamenetDetailsAsync();
                await LoadAvailabilityAsync(_selectedBookingDate);
                ManualLoadingDescriptionText.Text = "در حال دریافت رزروهای امروز...";
                await LoadTodayBookingsAsync();

                if (_availabilityLoadSucceeded && _todayBookingsLoadSucceeded)
                {
                    ApiConnectionStatusTextBlock.Text = "ارتباط با API برقرار است";
                    ApiConnectionStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(85, 214, 160));
                }
                else
                {
                    ApiConnectionStatusTextBlock.Text = "همگام‌سازی کامل نشد؛ اتصال را بررسی کنید";
                    ApiConnectionStatusTextBlock.Foreground = Brushes.IndianRed;
                }
            }
            catch (Exception ex)
            {
                ApiConnectionStatusTextBlock.Text = "ارتباط با API ناموفق است";
                ApiConnectionStatusTextBlock.Foreground = Brushes.IndianRed;
                SetBookingError(UserFacingErrors.GetMessage(ex,
                    "همگام‌سازی ناموفق بود. اتصال را بررسی و دوباره تلاش کنید."));
            }
            finally
            {
                _isRetryingSync = false;
                SetManualLoading(false);
                if (RetrySyncButton != null)
                {
                    RetrySyncButton.IsEnabled = true;
                    RetrySyncButton.Content = "تلاش مجدد";
                }
            }
        }


        // =====================================================
        // LOAD TODAY BOOKINGS
        // =====================================================


        private async Task LoadTodayBookingsAsync()
        {
            _todayBookingsLoadSucceeded = false;

            try
            {
                string today = DateTime.Today.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                );

                GamenetSyncResponse sync;
                if (_lastSelectedDateSync != null &&
                    _lastSelectedDateSyncDate?.Date == DateTime.Today)
                {
                    sync = _lastSelectedDateSync;
                }
                else
                {
                    sync = await _apiService.SyncAsync(_gamenet.Id, today, today);
                }

                // شمارش تمام رزروهای امروز
                int manualCount = 0;

                foreach (ManualBooking booking in sync.Data.ManualBookings)
                {
                    if (string.Equals(
                            booking.Date,
                            today,
                            StringComparison.Ordinal))
                    {
                        manualCount++;
                    }
                }

                int onlineCount = 0;
                int pendingCount = 0;

                foreach (OnlineBooking booking in sync.Data.OnlineBookings)
                {
                    bool isToday =
                        !string.IsNullOrWhiteSpace(booking.DateIso) &&
                        booking.DateIso.StartsWith(
                            today,
                            StringComparison.Ordinal
                        );

                    if (!isToday)
                        continue;

                    onlineCount++;

                    // فقط رزروهایی که وضعیت واقعی آن‌ها pending است.
                    if (string.Equals(
                            booking.Status?.Trim(),
                            "pending",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pendingCount++;
                    }
                }

                // مجموع رزروهای امروز، شامل رزروهای دستی و آنلاین
                int totalBookings = manualCount + onlineCount;

                TodayBookingsTextBlock.Text =
                    ToPersianNumber(totalBookings);

                // شمارنده رزروهای در انتظار
                if (PendingBookingsTextBlock != null)
                {
                    PendingBookingsTextBlock.Text =
                        ToPersianNumber(pendingCount);
                }

                _todayBookingsLoadSucceeded = true;
                _lastSuccessfulSyncAt = DateTime.Now;

                if (LastSuccessfulSyncTextBlock != null)
                {
                    LastSuccessfulSyncTextBlock.Text =
                        "آخرین همگام‌سازی موفق: " +
                        ToPersianNumber(
                            _lastSuccessfulSyncAt.Value.ToString(
                                "yyyy/MM/dd HH:mm",
                                CultureInfo.InvariantCulture
                            )
                        );
                }


                string apiStatus = _availabilityLoadSucceeded
                    ? "ارتباط با API برقرار است"
                    : "دریافت ظرفیت هنوز تأیید نشده است";

                Brush apiStatusColor = _availabilityLoadSucceeded
                    ? new SolidColorBrush(Color.FromRgb(85, 214, 160))
                    : new SolidColorBrush(Color.FromRgb(232, 185, 106));

                if (ApiConnectionStatusTextBlock != null)
                {
                    ApiConnectionStatusTextBlock.Text = apiStatus;
                    ApiConnectionStatusTextBlock.Foreground = apiStatusColor;
                }

                string lastSyncText =
                    "آخرین همگام‌سازی: " +
                    ToPersianNumber(
                        _lastSuccessfulSyncAt.Value.ToString(
                            "yyyy/MM/dd HH:mm",
                            CultureInfo.InvariantCulture
                        )
                    );

                PublishApiStatus(apiStatus, apiStatusColor, lastSyncText);

            }
            catch
            {
                _todayBookingsLoadSucceeded = false;

                // در صورت خطا، عدد صفر نمایش داده نشود.
                TodayBookingsTextBlock.Text = "—";

                if (PendingBookingsTextBlock != null)
                {
                    PendingBookingsTextBlock.Text = "—";
                }

                if (ApiConnectionStatusTextBlock != null)
                {
                    ApiConnectionStatusTextBlock.Text =
                        "دریافت رزروها از API ناموفق بود";

                    ApiConnectionStatusTextBlock.Foreground =
                        Brushes.IndianRed;
                    PublishApiStatus(
    "دریافت رزروها از API ناموفق بود",
    Brushes.IndianRed,
    "آخرین همگام‌سازی: ناموفق"
);
                }

            }
        }



        // =====================================================
        // GAMENET DETAILS
        // =====================================================


        private async Task LoadGamenetDetailsAsync()
        {
            using JsonDocument document =
                await _apiService.GetGamenetDetailsAsync(_gamenet.Id);

            JsonElement root = document.RootElement;

            // اطلاعات اصلی گیمنت
            if (root.TryGetProperty("gamenet", out JsonElement gamenetElement))
            {
                if (gamenetElement.TryGetProperty("work_start", out JsonElement workStartElement))
                    _workStart = ReadInt(workStartElement, 0);

                if (gamenetElement.TryGetProperty("work_end", out JsonElement workEndElement))
                    _workEnd = ReadInt(workEndElement, 24);

                if (gamenetElement.TryGetProperty("image_url", out JsonElement logoElement))
                {
                    _gamenetLogoUrl = ReadString(logoElement);
                }
                else if (gamenetElement.TryGetProperty("logo_url", out logoElement))
                {
                    _gamenetLogoUrl = ReadString(logoElement);
                }

                // API v2.3 returns stable physical system IDs alongside variations.
                _physicalSystemIdsByNumber.Clear();
                _physicalSystemVariationByNumber.Clear();
                if (gamenetElement.TryGetProperty("physical_systems", out JsonElement physicalSystemsElement) &&
                    physicalSystemsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement physicalSystem in physicalSystemsElement.EnumerateArray())
                    {
                        string systemId = physicalSystem.TryGetProperty("system_id", out JsonElement systemIdElement) ? ReadString(systemIdElement) : string.Empty;
                        string number = physicalSystem.TryGetProperty("number", out JsonElement numberElement) ? ReadString(numberElement) : string.Empty;
                        bool active = !physicalSystem.TryGetProperty("active", out JsonElement activeElement) || activeElement.ValueKind != JsonValueKind.False;
                        if (active && !string.IsNullOrWhiteSpace(systemId) && !string.IsNullOrWhiteSpace(number))
                        {
                            _physicalSystemIdsByNumber[number.Trim()] = systemId.Trim();
                            int variationId = physicalSystem.TryGetProperty("variation_id", out JsonElement physicalVariationElement) ? ReadInt(physicalVariationElement, 0) : 0;
                            if (variationId > 0) _physicalSystemVariationByNumber[number.Trim()] = variationId;
                        }
                    }
                }
            }

            // اعتبارسنجی ساعات کاری
            if (_workStart < 0) _workStart = 0;
            if (_workStart > 23) _workStart = 23;
            if (_workEnd < 1) _workEnd = 1;
            if (_workEnd > 24) _workEnd = 24;

            if (_workEnd <= _workStart)
                throw new Exception("ساعت کاری گیمنت نامعتبر است.");

            _ = LoadGamenetLogoSafelyAsync();

            // پاک‌کردن اطلاعات قبلی سیستم‌ها
            _variationNames.Clear();
            _variationPrices.Clear();
            _variationCapacities.Clear();
            _variationImageUrls.Clear();
            _variationSystemNumbers.Clear();
            _variationGames.Clear();
            _selectedSystemNumbers.Clear();

            if (!root.TryGetProperty("variations", out JsonElement variationsElement) ||
                variationsElement.ValueKind != JsonValueKind.Array)
            {
                throw new Exception(
                    "اطلاعات سیستم‌های گیمنت در پاسخ سرور وجود ندارد یا معتبر نیست.");
            }

            foreach (JsonElement variation in variationsElement.EnumerateArray())
            {
                if (!variation.TryGetProperty("id", out JsonElement idElement))
                    continue;

                int variationId = ReadInt(idElement, 0);

                if (variationId <= 0)
                    continue;

                string variationName = string.Empty;

                if (variation.TryGetProperty("name", out JsonElement nameElement))
                    variationName = ReadString(nameElement);

                if (string.IsNullOrWhiteSpace(variationName) &&
                    variation.TryGetProperty("title", out JsonElement titleElement))
                {
                    variationName = ReadString(titleElement);
                }

                if (string.IsNullOrWhiteSpace(variationName))
                    variationName = $"سیستم {variationId}";

                decimal price = 0;

                if (variation.TryGetProperty("price", out JsonElement priceElement))
                    price = ReadDecimal(priceElement);

                int capacity = 0;

                if (variation.TryGetProperty("capacity", out JsonElement capacityElement))
                    capacity = ReadInt(capacityElement, 0);

                string imageUrl = string.Empty;

                if (variation.TryGetProperty("image_url", out JsonElement imageElement))
                    imageUrl = ReadString(imageElement);

                _variationNames[variationId] = variationName;
                _variationPrices[variationId] = price;
                _variationCapacities[variationId] = capacity;
                _variationImageUrls[variationId] = imageUrl;

                var numbers = new List<string>();
                if (variation.TryGetProperty("system_numbers", out JsonElement numbersElement) &&
                    numbersElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement numberElement in numbersElement.EnumerateArray())
                    {
                        string number = ReadString(numberElement).Trim();
                        if (!string.IsNullOrWhiteSpace(number) && !numbers.Contains(number))
                            numbers.Add(number);
                    }
                }
                _variationSystemNumbers[variationId] = numbers;
                _selectedSystemNumbers[variationId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var games = new List<(int Id, string Name)>();
                if (variation.TryGetProperty("games", out JsonElement gamesElement) &&
                    gamesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement gameElement in gamesElement.EnumerateArray())
                    {
                        int gameId = gameElement.TryGetProperty("id", out JsonElement gameIdElement)
                            ? ReadInt(gameIdElement, 0) : 0;
                        string gameName = gameElement.TryGetProperty("name", out JsonElement gameNameElement)
                            ? ReadString(gameNameElement).Trim() : string.Empty;
                        if (gameId > 0 && !string.IsNullOrWhiteSpace(gameName))
                            games.Add((gameId, gameName));
                    }
                }
                _variationGames[variationId] = games;
            }

            // Devices created in the v2.3 floor-plan manager become selectable in this form
            // when their registry record is assigned to a WooCommerce variation.
            foreach (var physicalEntry in _physicalSystemVariationByNumber)
            {
                if (_variationSystemNumbers.TryGetValue(physicalEntry.Value, out List<string>? registeredNumbers) &&
                    !registeredNumbers.Contains(physicalEntry.Key, StringComparer.OrdinalIgnoreCase))
                    registeredNumbers.Add(physicalEntry.Key);
            }

            BuildSystemCards();

            int totalSystems = _variationCapacities.Values.Sum();
            ActiveSystemsTextBlock.Text = ToPersianNumber(totalSystems);

            foreach (int variationId in _variationNames.Keys.ToList())
            {
                if (_systemCardImages.TryGetValue(variationId, out Image? imageControl))
                {
                    try
                    {
                        await LoadVariationImageAsync(variationId, imageControl);
                    }
                    catch
                    {
                        // خطای تصویر نباید مانع بارگذاری فرم رزرو شود.
                    }
                }

                if (_systemCardCapacityTextBlocks.TryGetValue(variationId, out TextBlock? capacityText) &&
                    _systemCardPriceTextBlocks.TryGetValue(variationId, out TextBlock? priceText))
                {
                    UpdateSystemInformation(variationId, capacityText, priceText);
                }
            }
        }

        // تبدیل امن مقدار JSON به عدد صحیح؛ هم Number و هم String را می‌پذیرد.
        private static int ReadInt(JsonElement element, int fallback = 0)
        {
            if (element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt32(out int number))
            {
                return number;
            }

            string text = element.ToString().Trim();

            if (int.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        // تبدیل امن مقدار JSON به رشته
        private static string ReadString(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null ||
                element.ValueKind == JsonValueKind.Undefined)
            {
                return string.Empty;
            }

            return element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.ToString();
        }



        // =====================================================
        // SYSTEM INFORMATION
        // =====================================================

        private void UpdateSystemInformation(
            int variationId,
            TextBlock capacityTextBlock,
            TextBlock priceTextBlock)
        {
            if (_variationCapacities.TryGetValue(
                    variationId,
                    out int capacity))
            {
                capacityTextBlock.Text =
                    $"ظرفیت: {ToPersianNumber(capacity)} سیستم";
            }
            else
            {
                capacityTextBlock.Text =
                    "ظرفیت: —";
            }

            if (_variationPrices.TryGetValue(
                    variationId,
                    out decimal price))
            {
                priceTextBlock.Text =
                    $"قیمت: {ToPersianNumber(price.ToString("N0", CultureInfo.InvariantCulture))} تومان / ساعت";
            }
            else
            {
                priceTextBlock.Text =
                    "قیمت: —";
            }
        }


        // =====================================================
        // LOAD AVAILABILITY
        // =====================================================




        private async Task LoadAvailabilityAsync(DateTime date)
        {
            _availabilityLoadSucceeded = false;

            SetManualLoading(
                true,
                "در حال دریافت ظرفیت و وضعیت دستگاه‌ها..."
            );

            try
            {
                // اعتبارسنجی تاریخ
                if (date.Date < DateTime.Today)
                {
                    throw new Exception(
                        "امکان دریافت ظرفیت برای تاریخ گذشته وجود ندارد."
                    );
                }

                // اعتبارسنجی سیستم‌ها
                if (_variationNames.Count == 0)
                {
                    throw new Exception(
                        "هیچ سیستمی برای این گیمنت از سرور دریافت نشده است."
                    );
                }

                BookingFormStatusTextBlock.Foreground = Brushes.Gray;
                BookingFormStatusTextBlock.Text =
                    "در حال دریافت ظرفیت سیستم‌ها...";

                string apiDate = date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                );

                // شناسه تمام انواع سیستم
                int[] variationIds = _variationNames.Keys
                    .OrderBy(id => id)
                    .ToArray();

                // دریافت هم‌زمان ظرفیت انواع سیستم‌ها
                Task<KeyValuePair<int, AvailabilityResponse>>[] availabilityTasks =
                    variationIds
                        .Select(async variationId =>
                            new KeyValuePair<int, AvailabilityResponse>(
                                variationId,
                                await _apiService.GetAvailabilityAsync(
                                    _gamenet.Id,
                                    variationId,
                                    apiDate
                                )
                            )
                        )
                        .ToArray();

                // دریافت رزروهای دستی و آنلاین همان روز
                Task<GamenetSyncResponse> selectedDateSyncTask =
                    _apiService.SyncAsync(
                        _gamenet.Id,
                        apiDate,
                        apiDate
                    );

                // انتظار برای تکمیل تمام درخواست‌ها
                await Task.WhenAll(
                    availabilityTasks.Cast<Task>()
                        .Append(selectedDateSyncTask)
                );

                // آماده‌سازی پاسخ‌ها
                Dictionary<int, AvailabilityResponse> freshAvailability =
                    (await Task.WhenAll(availabilityTasks))
                        .ToDictionary(
                            item => item.Key,
                            item => item.Value
                        );

                GamenetSyncResponse selectedDateSync =
                    await selectedDateSyncTask;

                // ثبت اطلاعات آخرین Sync
                _lastSelectedDateSync = selectedDateSync;
                _lastSelectedDateSyncDate = date.Date;

                // دریافت رزروهای دستی و آنلاین
                List<ManualBooking> freshManualBookings =
                    selectedDateSync.Data?.ManualBookings
                    ?? new List<ManualBooking>();

                List<OnlineBooking> freshOnlineBookings =
                    selectedDateSync.Data?.OnlineBookings
                    ?? new List<OnlineBooking>();

                // جایگزینی اطلاعات رزروهای روز
                _selectedDateManualBookings = freshManualBookings;
                _selectedDateOnlineBookings = freshOnlineBookings;

                System.Diagnostics.Debug.WriteLine("========== MANUAL BOOKINGS ==========");

                foreach (var booking in _selectedDateManualBookings)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Date: {booking.Date}"
                    );

                    foreach (var system in booking.Systems ?? new List<ManualBookingSystem>())
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"VariationId: {system.VariationId} | Number: [{system.SystemNumber}] | Hours: [{string.Join(", ", system.Hours ?? new List<string>())}]"
                        );
                    }
                }

                System.Diagnostics.Debug.WriteLine("========== ONLINE BOOKINGS ==========");

                foreach (var booking in _selectedDateOnlineBookings)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Date: {booking.DateIso} | VariationId: {booking.VariationId} | VarId: {booking.VarId} | Number: [{booking.SystemNumber}] | Start: {booking.StartTime} | Hours: {booking.Hours} | Status: {booking.Status}"
                    );
                }
                // جایگزینی ظرفیت‌ها
                _availability.Clear();

                foreach (KeyValuePair<int, AvailabilityResponse> item
                         in freshAvailability)
                {
                    _availability[item.Key] = item.Value;
                }

                // پاک‌سازی انتخاب‌های قبلی
                _selectedStartHour = null;
                _selectedBookingHours = 1;
                _durationExplicitlySelected = false;

                _selectedQuantities.Clear();

                foreach (HashSet<string> selectedNumbers
                         in _selectedSystemNumbers.Values)
                {
                    selectedNumbers.Clear();
                }

                // به‌روزرسانی اطلاعات صفحه
                UpdateSystemSelectionVisuals();
                UpdateTotal();

                BuildBookingHours();
                BuildBookingDurationOptions();

                UpdateLiveCapacitySummary();

                // اعلام موفقیت دریافت ظرفیت
                _availabilityLoadSucceeded = true;

                BookingFormStatusTextBlock.Foreground =
                    Brushes.DarkGreen;

                BookingFormStatusTextBlock.Text =
                    "اطلاعات ظرفیت و ساعت‌ها با موفقیت دریافت شد.";

                // نمایش وضعیت اتصال API
                if (_todayBookingsLoadSucceeded)
                {
                    ApiConnectionStatusTextBlock.Text =
                        "ارتباط با API برقرار است";

                    ApiConnectionStatusTextBlock.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(85, 214, 160)
                        );
                }
                else
                {
                    ApiConnectionStatusTextBlock.Text =
                        "ظرفیت دریافت شد؛ دریافت رزروها تأیید نشده است";

                    ApiConnectionStatusTextBlock.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(232, 185, 106)
                        );
                }

                // تازه‌سازی رنگ سیستم‌های نقشه
                if (_inlineFloorMap != null)
                {
                    _inlineFloorMap.RefreshBookedStates();
                }
            }
            catch (Exception ex)
            {
                _availabilityLoadSucceeded = false;

                // اعلام خطای دریافت اطلاعات
                ApiConnectionStatusTextBlock.Text =
                    "دریافت ظرفیت از API ناموفق بود";

                ApiConnectionStatusTextBlock.Foreground =
                    Brushes.IndianRed;

                // پاک‌سازی ظرفیت نامعتبر
                _availability.Clear();

                _selectedStartHour = null;
                _selectedBookingHours = 1;
                _durationExplicitlySelected = false;

                _selectedQuantities.Clear();

                foreach (HashSet<string> selectedNumbers
                         in _selectedSystemNumbers.Values)
                {
                    selectedNumbers.Clear();
                }

                // پاک‌سازی ساعت‌ها
                BookingHoursPanel.Children.Clear();

                // پاک‌سازی مدت رزرو
                if (BookingDurationPanel != null)
                {
                    BookingDurationPanel.Children.Clear();
                }

                // پاک‌سازی خلاصه ظرفیت
                if (LiveCapacitySummaryPanel != null)
                {
                    LiveCapacitySummaryPanel.Children.Clear();
                }

                if (LiveCapacityTotalsTextBlock != null)
                {
                    LiveCapacityTotalsTextBlock.Text =
                        "ظرفیت دریافت نشد. ارتباط با سرور را بررسی کن و دوباره تلاش کن.";
                }

                // تازه‌سازی وضعیت ظاهری انتخاب‌ها
                UpdateSystemSelectionVisuals();
                UpdateTotal();

                // تازه‌سازی رنگ نقشه
                if (_inlineFloorMap != null)
                {
                    _inlineFloorMap.RefreshBookedStates();
                }

                SetBookingError(
                    UserFacingErrors.GetMessage(
                        ex,
                        "دریافت ظرفیت ناموفق بود؛ ظرفیت صفر نیست و اطلاعات از سرور دریافت نشده است. لطفاً دوباره تلاش کنید."
                    )
                );
            }
            finally
            {
                SetManualLoading(false);
            }
        }


        // =====================================================
        // REFRESH AVAILABILITY BEFORE BOOKING
        // =====================================================

        private async Task RefreshAvailabilityForBookingAsync(DateTime date)
        {
            if (_variationNames.Count == 0)
            {
                throw new Exception(
                    "فهرست سیستم‌های گیمنت خالی است."
                );
            }

            string apiDate = date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );

            Dictionary<int, AvailabilityResponse> freshAvailability = new();

            foreach (int variationId in new List<int>(_variationNames.Keys))
            {
                AvailabilityResponse response =
                    await _apiService.GetAvailabilityAsync(
                        _gamenet.Id,
                        variationId,
                        apiDate
                    );

                freshAvailability[variationId] = response;
            }

            // فقط پس از دریافت موفق تمام پاسخ‌ها، اطلاعات قبلی جایگزین شود.
            _availability.Clear();

            foreach (KeyValuePair<int, AvailabilityResponse> item
                     in freshAvailability)
            {
                _availability[item.Key] = item.Value;
            }
        }

        // =====================================================
        // BUILD START HOURS
        // =====================================================


        private void BuildBookingHours()
        {
            BookingHoursPanel.Children.Clear();

            HashSet<string> uniqueHours =
                new(StringComparer.Ordinal);

            List<string> hours =
                new();

            // استخراج ساعت‌های معتبر از ظرفیت تمام انواع سیستم
            foreach (int variationId in _availability.Keys.ToList())
            {
                if (!_availability.TryGetValue(
                        variationId,
                        out AvailabilityResponse? response))
                {
                    continue;
                }

                if (response.Availability?.Hours == null)
                    continue;

                foreach (AvailabilityHour hour in response.Availability.Hours)
                {
                    int hourNumber = hour.Hour;

                    // محدودیت ساعت کاری گیمنت
                    if (hourNumber < _workStart ||
                        hourNumber >= _workEnd)
                    {
                        continue;
                    }

                    string normalizedHour =
                        NormalizeHourForDisplay(
                            hourNumber.ToString()
                        );

                    if (uniqueHours.Add(normalizedHour))
                    {
                        hours.Add(normalizedHour);
                    }
                }
            }

            // مرتب‌سازی ساعت‌ها
            hours.Sort(CompareHours);

            // ساخت دکمه‌های ساعت
            foreach (string hour in hours)
            {
                Button button = CreateHourButton(hour);

                BookingHoursPanel.Children.Add(button);
            }

            // به‌روزرسانی وضعیت نقشه
            if (_inlineFloorMap != null)
            {
                _inlineFloorMap.RefreshBookedStates();
            }
        }

        // =====================================================
        // CREATE START HOUR BUTTON
        // =====================================================

        private Button CreateHourButton(
            string hour)
        {
            bool workingHour =
                IsWithinWorkingHours(
                    hour
                );

            bool past =
                IsPastBookingHour(
                    _selectedBookingDate,
                    hour
                );

            bool bookable =
                workingHour &&
                !past &&
                IsHourBookableForAnySystem(
                    hour
                );

            Button button =
                new Button
                {
                    Tag =
                        hour,

                    Width =
                        76,

                    Height =
                        36,

                    Margin =
                        new Thickness(4),

                    FontSize =
                        14,

                    FontFamily =
                        new FontFamily(
                            "Peyda"
                        ),

                    FontWeight =
                        FontWeights.SemiBold,

                    Padding =
                        new Thickness(
                            8,
                            0,
                            8,
                            0
                        ),

                    Cursor =
                        bookable
                            ? System.Windows.Input.Cursors.Hand
                            : System.Windows.Input.Cursors.Arrow,

                    IsEnabled =
                        bookable,

                    BorderThickness =
                        new Thickness(1)
                };


            // =================================================
            // AVAILABLE HOUR
            // =================================================

            if (bookable)
            {
                button.Content =
                    ToPersianNumber(hour);

                ApplyNormalHourButtonStyle(
                    button
                );
            }


            // =================================================
            // RESERVED / UNAVAILABLE HOUR
            // =================================================

            else
            {
                bool hasCapacity =
                    IsHourBookableForAnySystem(
                        hour
                    );

                // ---------------------------------------------
                // COMPLETELY RESERVED
                // ---------------------------------------------

                if (!hasCapacity &&
                    workingHour &&
                    !past)
                {
                    StackPanel reservedPanel =
                        new StackPanel
                        {
                            Orientation =
                                Orientation.Vertical,

                            HorizontalAlignment =
                                HorizontalAlignment.Center,

                            VerticalAlignment =
                                VerticalAlignment.Center
                        };

                    TextBlock hourText =
                        new TextBlock
                        {
                            Text =
                                ToPersianNumber(hour),

                            FontFamily =
                                new FontFamily(
                                    "Peyda"
                                ),

                            FontSize =
                                13,

                            FontWeight =
                                FontWeights.SemiBold,

                            HorizontalAlignment =
                                HorizontalAlignment.Center,

                            TextAlignment =
                                TextAlignment.Center,

                            Foreground =
                                new SolidColorBrush(
                                    Color.FromRgb(
                                        145,
                                        146,
                                        152
                                    )
                                )
                        };

                    TextBlock reservedText =
                        new TextBlock
                        {
                            Text =
                                "رزرو شده",

                            FontFamily =
                                new FontFamily(
                                    "Peyda"
                                ),

                            FontSize =
                                10,

                            FontWeight =
                                FontWeights.Medium,

                            HorizontalAlignment =
                                HorizontalAlignment.Center,

                            TextAlignment =
                                TextAlignment.Center,

                            Foreground =
                                new SolidColorBrush(
                                    Color.FromRgb(
                                        170,
                                        171,
                                        176
                                    )
                                )
                        };

                    reservedPanel.Children.Add(
                        hourText
                    );

                    reservedPanel.Children.Add(
                        reservedText
                    );

                    button.Content =
                        reservedPanel;

                    button.Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                242,
                                242,
                                244
                            )
                        );

                    button.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                165,
                                166,
                                172
                            )
                        );

                    button.BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(
                                225,
                                226,
                                230
                            )
                        );

                    button.Opacity =
                        0.78;
                }


                // ---------------------------------------------
                // PAST / OTHER UNAVAILABLE
                // ---------------------------------------------

                else
                {
                    button.Content =
                        ToPersianNumber(hour);

                    button.Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                242,
                                242,
                                244
                            )
                        );

                    button.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                165,
                                166,
                                172
                            )
                        );

                    button.BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(
                                225,
                                226,
                                230
                            )
                        );

                    button.Opacity =
                        0.65;
                }
            }


            // =================================================
            // CLICK
            // =================================================

            button.Click +=
                BookingHourButton_Click;

            return button;
        }
        // =====================================================
        // HOUR CLICK
        // =====================================================
        // =====================================================
        // HOUR CLICK
        // =====================================================

        private void BookingHourButton_Click(
     object sender,
     RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not string hour)
                return;

            // VALIDATE HOUR
            string? validationError =
                ValidateStartHour(
                    _selectedBookingDate,
                    hour
                );

            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SetBookingError(validationError);
                return;
            }

            // SAVE SELECTED HOUR
            _selectedStartHour = hour;

            _selectedBookingHours = 1;
            _durationExplicitlySelected = false;

            UpdateLiveCapacitySummary();

            // UPDATE HOUR BUTTONS
            foreach (object child in BookingHoursPanel.Children)
            {
                if (child is not Button hourButton)
                    continue;

                bool selected =
                    string.Equals(
                        hourButton.Tag?.ToString(),
                        hour,
                        StringComparison.Ordinal
                    );

                if (selected)
                {
                    ApplySelectedHourButtonStyle(hourButton);
                }
                else if (hourButton.IsEnabled)
                {
                    ApplyNormalHourButtonStyle(hourButton);
                }
            }

            // UPDATE SYSTEM CARDS
            UpdateSystemSelectionVisuals();

            // BUILD DURATION OPTIONS
            BuildBookingDurationOptions();

            // REFRESH FLOOR MAP
            _inlineFloorMap?.RefreshBookedStates();

            // INFO
            SetBookingInfo(
                $"ساعت شروع {ToPersianNumber(hour)} انتخاب شد."
            );

            // TOTAL
            UpdateTotal();
        }
        // =====================================================
        // BUILD DYNAMIC DURATION OPTIONS
        // =====================================================


        private void BuildBookingDurationOptions()
        {
            if (BookingDurationPanel == null)
                return;

            BookingDurationPanel.Children.Clear();

            int previouslySelectedDuration =
                Math.Max(1, _selectedBookingHours);

            // تا وقتی ساعت شروع انتخاب نشده، مدت رزرو نمایش داده نمی‌شود
            if (string.IsNullOrWhiteSpace(_selectedStartHour))
            {
                UpdateDurationVisuals();

                if (_inlineFloorMap != null)
                    _inlineFloorMap.RefreshBookedStates();

                return;
            }

            // اعتبارسنجی ساعت شروع
            if (!TryGetHourNumber(
                    _selectedStartHour,
                    out int startHour))
            {
                UpdateDurationVisuals();

                if (_inlineFloorMap != null)
                    _inlineFloorMap.RefreshBookedStates();

                return;
            }

            // محدودیت ساعت کاری
            int maxByWorkTime =
                _workEnd - startHour;

            if (maxByWorkTime < 1)
            {
                UpdateDurationVisuals();

                if (_inlineFloorMap != null)
                    _inlineFloorMap.RefreshBookedStates();

                return;
            }

            // حداکثر مدت مجاز بر اساس ظرفیت سیستم‌ها
            int maxByAvailability =
                GetMaximumAvailableDuration(startHour);

            int maxDuration =
                Math.Min(
                    maxByWorkTime,
                    maxByAvailability
                );

            // اگر هیچ مدت معتبری وجود نداشت
            if (maxDuration < 1)
            {
                _selectedBookingHours = 1;
                _durationExplicitlySelected = false;

                UpdateDurationVisuals();

                if (_inlineFloorMap != null)
                    _inlineFloorMap.RefreshBookedStates();

                return;
            }

            // حفظ مدت قبلی در صورت معتبر بودن
            _selectedBookingHours =
                Math.Min(
                    previouslySelectedDuration,
                    maxDuration
                );

            if (_selectedBookingHours != previouslySelectedDuration)
            {
                _durationExplicitlySelected = false;
            }

            // ساخت دکمه‌های مدت رزرو
            for (int hours = 1; hours <= maxDuration; hours++)
            {
                Button button =
                    CreateDurationButton(hours);

                BookingDurationPanel.Children.Add(button);
            }

            UpdateDurationVisuals();

            // تازه‌سازی رنگ سیستم‌ها با مدت جدید
            if (_inlineFloorMap != null)
            {
                _inlineFloorMap.RefreshBookedStates();
            }
        }



        // =====================================================
        // CREATE DURATION BUTTON
        // =====================================================

        private Button CreateDurationButton(
            int hours)
        {
            int startHour = 0;
            if (!string.IsNullOrWhiteSpace(_selectedStartHour))
                TryGetHourNumber(_selectedStartHour, out startHour);
            int endHour = (startHour + hours) % 24;
            string endTime = ToPersianNumber(endHour.ToString("00", CultureInfo.InvariantCulture)) + ":۰۰";

            Button button =
                new Button
                {
                    Content = endTime,

                    Tag =
                        hours,

                    Width =
                        74,

                    Height =
                        38,

                    Margin =
                        new Thickness(4),

                    FontSize =
                        14,

                    FontFamily =
                        new FontFamily(
                            "Peyda"
                        ),

                    // کمی ضخیم‌تر
                    FontWeight =
                        FontWeights.SemiBold,

                    Cursor =
                        System.Windows.Input.Cursors.Hand,

                    BorderThickness =
                        new Thickness(1)
                };

            ApplyNormalDurationButtonStyle(
                button
            );

            button.Click +=
                BookingDurationButton_Click;

            return button;
        }


        // =====================================================
        // DURATION CLICK
        // =====================================================

        private void BookingDurationButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not int hours)
                return;

            string? validationError =
                GetDurationValidationError(hours);

            if (!string.IsNullOrWhiteSpace(validationError))
            {
                SetBookingError(validationError);
                return;
            }

            _selectedBookingHours = hours;
            _durationExplicitlySelected = true;

            UpdateDurationVisuals();

            int startHour = 0;

            if (!string.IsNullOrWhiteSpace(_selectedStartHour))
                TryGetHourNumber(_selectedStartHour, out startHour);

            int endHour = (startHour + hours) % 24;

            SetBookingInfo(
                $"ساعت پایان {ToPersianNumber(endHour.ToString("00", CultureInfo.InvariantCulture))}:۰۰ انتخاب شد."
            );

            UpdateTotal();

            // REFRESH FLOOR MAP
            _inlineFloorMap?.RefreshBookedStates();
        }

        // =====================================================
        // UPDATE DURATION VISUALS
        // =====================================================

        private void UpdateDurationVisuals()
        {
            if (BookingDurationPanel != null)
            {
                foreach (
                    object child
                    in BookingDurationPanel.Children)
                {
                    if (child is not Button button)
                        continue;

                    if (button.Tag is not int hours)
                        continue;

                    if (_durationExplicitlySelected && hours == _selectedBookingHours)
                    {
                        ApplySelectedDurationButtonStyle(
                            button
                        );
                    }
                    else
                    {
                        ApplyNormalDurationButtonStyle(
                            button
                        );
                    }
                }
            }

            if (BookingDurationStatusTextBlock != null)
            {
                if (string.IsNullOrWhiteSpace(
                        _selectedStartHour))
                {
                    BookingDurationStatusTextBlock.Text =
                        "ابتدا ساعت شروع را انتخاب کنید.";
                }
                else if (!_durationExplicitlySelected)
                {
                    BookingDurationStatusTextBlock.Text =
                        "ساعت پایان را انتخاب کنید.";
                }
                else
                {
                    int startHour = 0;
                    if (!string.IsNullOrWhiteSpace(_selectedStartHour))
                        TryGetHourNumber(_selectedStartHour, out startHour);
                    int endHour = (startHour + _selectedBookingHours) % 24;
                    BookingDurationStatusTextBlock.Text =
                        $"ساعت پایان انتخابی: {ToPersianNumber(endHour.ToString("00", CultureInfo.InvariantCulture))}:۰۰";
                }
            }
            UpdateSystemTileVisuals();
        }


        // =====================================================
        // MAXIMUM AVAILABLE DURATION
        // =====================================================

        private int GetMaximumAvailableDuration(
            int startHour)
        {
            int maximum =
                _workEnd - startHour;

            if (maximum < 1)
                return 0;

            for (
                int duration = 1;
                duration <= maximum;
                duration++)
            {
                int currentHour =
                    startHour + duration - 1;

                string hourText =
                    $"{currentHour:00}:00";

                bool available;

                if (GetSelectedSystemCount() == 0)
                {
                    available =
                        IsHourBookableForAnySystem(
                            hourText
                        );
                }
                else
                {
                    available =
                        AreSelectedSystemsBookableAtHour(
                            hourText
                        );
                }

                if (!available)
                {
                    return duration - 1;
                }
            }

            return maximum;
        }


        // =====================================================
        // CAN USE DURATION
        // =====================================================

        private bool CanUseDuration(
            int duration)
        {
            return string.IsNullOrWhiteSpace(
                       GetDurationValidationError(
                           duration
                       )
                   );
        }


        // =====================================================
        // DURATION VALIDATION MESSAGE
        // =====================================================

        private string? GetDurationValidationError(
            int duration)
        {
            if (duration < 1)
            {
                return "مدت رزرو باید حداقل یک ساعت باشد.";
            }

            if (string.IsNullOrWhiteSpace(
                    _selectedStartHour))
            {
                return "ابتدا ساعت شروع رزرو را انتخاب کنید.";
            }

            string? startError =
                ValidateStartHour(
                    _selectedBookingDate,
                    _selectedStartHour
                );

            if (!string.IsNullOrWhiteSpace(
                    startError))
            {
                return startError;
            }

            if (!TryGetHourNumber(
                    _selectedStartHour,
                    out int startHour))
            {
                return "ساعت شروع رزرو نامعتبر است.";
            }

            int endHour =
                startHour + duration;

            if (startHour < _workStart ||
                endHour > _workEnd)
            {
                return
                    $"این رزرو خارج از ساعت کاری گیمنت است. ساعت کاری گیمنت از {ToPersianNumber($"{_workStart:00}:00")} تا {ToPersianNumber($"{_workEnd:00}:00")} است.";
            }

            if (GetSelectedSystemCount() == 0)
            {
                for (
                    int offset = 0;
                    offset < duration;
                    offset++)
                {
                    int currentHour =
                        startHour + offset;

                    string hour =
                        $"{currentHour:00}:00";

                    if (!IsHourBookableForAnySystem(
                            hour))
                    {
                        return
                            $"در ساعت {ToPersianNumber(hour)} ظرفیت هیچ‌یک از سیستم‌ها برای رزرو موجود نیست.";
                    }
                }

                return null;
            }

            string? capacityError =
                GetSelectedSystemsCapacityError(
                    startHour,
                    duration
                );

            return capacityError;
        }


        // =====================================================
        // SELECTED SYSTEMS AVAILABLE FOR ENTIRE DURATION
        // =====================================================

        private bool AreSelectedSystemsBookableForDuration()
        {
            if (GetSelectedSystemCount() == 0)
                return false;

            if (string.IsNullOrWhiteSpace(
                    _selectedStartHour))
            {
                return false;
            }

            if (!TryGetHourNumber(
                    _selectedStartHour,
                    out int startHour))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(
                GetSelectedSystemsCapacityError(
                    startHour,
                    _selectedBookingHours
                )
            );
        }


        // =====================================================
        // SELECTED SYSTEMS BOOKABLE AT HOUR
        // =====================================================

        private bool AreSelectedSystemsBookableAtHour(
            string hour)
        {
            bool hasSelection = false;

            foreach (
                KeyValuePair<int, int> selection
                in _selectedQuantities)
            {
                int variationId =
                    selection.Key;

                int quantity =
                    selection.Value;

                if (quantity <= 0)
                    continue;

                hasSelection = true;

                if (!IsVariationBookableAtHour(
                        variationId,
                        hour,
                        quantity))
                {
                    return false;
                }
            }

            return hasSelection;
        }


        // =====================================================
        // INCREASE SYSTEM QUANTITY
        // =====================================================

        private void IncreaseSystemQuantity(
            int variationId)
        {
            if (!_availability.ContainsKey(
                    variationId))
            {
                SetBookingError(
                    "اطلاعات ظرفیت این سیستم هنوز دریافت نشده است."
                );

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    _selectedStartHour))
            {
                SetBookingError(
                    "ابتدا یک ساعت شروع انتخاب کنید."
                );

                return;
            }

            string? startError =
                ValidateStartHour(
                    _selectedBookingDate,
                    _selectedStartHour
                );

            if (!string.IsNullOrWhiteSpace(
                    startError))
            {
                SetBookingError(
                    startError
                );

                return;
            }

            int currentQuantity =
                GetSelectedQuantity(
                    variationId
                );

            int availableAtStart =
                GetAvailableQuantityAtHour(
                    variationId,
                    _selectedStartHour
                );

            if (availableAtStart <= currentQuantity)
            {
                SetBookingError(
                    BuildCapacityMessage(
                        variationId,
                        _selectedStartHour,
                        currentQuantity + 1,
                        availableAtStart
                    )
                );

                return;
            }

            int previousDuration =
                _selectedBookingHours;

            _selectedQuantities[
                variationId
            ] =
                currentQuantity + 1;

            UpdateSystemSelectionVisuals();

            BuildBookingDurationOptions();

            if (previousDuration > 1 &&
                CanUseDuration(previousDuration))
            {
                _selectedBookingHours =
                    previousDuration;

                UpdateDurationVisuals();
            }

            UpdateTotal();

            SetBookingInfo(
                $"{GetVariationName(variationId)}: {ToPersianNumber(GetSelectedQuantity(variationId))} سیستم انتخاب شد."
            );
        }


        // =====================================================
        // DECREASE SYSTEM QUANTITY
        // =====================================================

        private void DecreaseSystemQuantity(
            int variationId)
        {
            int currentQuantity =
                GetSelectedQuantity(
                    variationId
                );

            if (currentQuantity <= 0)
            {
                return;
            }

            int previousDuration =
                _selectedBookingHours;

            int newQuantity =
                currentQuantity - 1;

            if (newQuantity <= 0)
            {
                _selectedQuantities.Remove(
                    variationId
                );
            }
            else
            {
                _selectedQuantities[
                    variationId
                ] =
                    newQuantity;
            }

            UpdateSystemSelectionVisuals();

            BuildBookingDurationOptions();

            if (previousDuration > 1 &&
                CanUseDuration(previousDuration))
            {
                _selectedBookingHours =
                    previousDuration;

                UpdateDurationVisuals();
            }

            UpdateTotal();

            if (newQuantity <= 0)
            {
                SetBookingInfo(
                    $"{GetVariationName(variationId)} از انتخاب خارج شد."
                );
            }
            else
            {
                SetBookingInfo(
                    $"{GetVariationName(variationId)}: {ToPersianNumber(newQuantity)} سیستم انتخاب شد."
                );
            }
        }


        // =====================================================
        // GET SELECTED QUANTITY
        // =====================================================

        private int GetSelectedQuantity(
            int variationId)
        {
            if (_selectedQuantities.TryGetValue(
                    variationId,
                    out int quantity))
            {
                return quantity;
            }

            return 0;
        }


        // =====================================================
        // GET TOTAL SELECTED SYSTEM COUNT
        // =====================================================

        private async Task ValidatePhysicalSystemsWithServerAsync(
            DateTime bookingDate, string? startTime, int hours, List<BookingSystemRequest> systems)
        {
            var selectedIds = systems
                .Where(s => !string.IsNullOrWhiteSpace(s.SystemId))
                .Select(s => s.SystemId!)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            if (selectedIds.Count == 0) return; // Backward compatibility for legacy systems not yet registered in v2.3.
            if (string.IsNullOrWhiteSpace(startTime)) throw new Exception("ساعت شروع رزرو مشخص نیست.");

            using var extendedApi = new LobixExtendedApiService(_session.Token);
            using var response = await extendedApi.GetDeviceAvailabilityAsync(
                _gamenet.Id,
                bookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                startTime,
                Math.Clamp(hours, 1, 24));

            if (!response.RootElement.TryGetProperty("devices", out JsonElement devices) || devices.ValueKind != JsonValueKind.Array)
                throw new Exception("وضعیت دستگاه‌ها از سرور قابل تأیید نیست؛ برای جلوگیری از رزرو تداخلی، رزرو ارسال نشد.");

            foreach (JsonElement device in devices.EnumerateArray())
            {
                string id = device.TryGetProperty("system_id", out JsonElement idElement) ? ReadString(idElement) : string.Empty;
                if (!selectedIds.Contains(id)) continue;
                string status = device.TryGetProperty("status", out JsonElement statusElement) ? ReadString(statusElement) : "unknown";
                if (!string.Equals(status, "free", StringComparison.OrdinalIgnoreCase))
                {
                    string number = device.TryGetProperty("number", out JsonElement numberElement) ? ReadString(numberElement) : "";
                    string label = status switch { "reserved" => "رزرو شده", "in_use" => "در حال استفاده", "off" => "خاموش/غیرفعال", _ => "وضعیت نامشخص" };
                    throw new Exception($"دستگاه {number} در سرور «{label}» است. لطفاً ظرفیت را تازه‌سازی و دستگاه دیگری انتخاب کنید.");
                }
            }
        }

        private int GetSelectedSystemCount()
        {
            int total = 0;

            foreach (
                int quantity
                in _selectedQuantities.Values)
            {
                if (quantity > 0)
                {
                    total += quantity;
                }
            }

            return total;
        }

        private void TogglePhysicalSystem(int variationId, string systemNumber)
        {
            int startHour = 0;
            bool hasValidatedSlot = !string.IsNullOrWhiteSpace(_selectedStartHour) &&
                _availabilityLoadSucceeded && TryGetHourNumber(_selectedStartHour, out startHour);
            if (hasValidatedSlot && IsPhysicalSystemBooked(variationId, systemNumber, startHour, _selectedBookingHours))
            {
                SetBookingError($"دستگاه {systemNumber} در بازه انتخاب‌شده رزرو است.");
                UpdateSystemTileVisuals();
                return;
            }

            if (!_selectedSystemNumbers.TryGetValue(variationId, out var selected))
            {
                selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _selectedSystemNumbers[variationId] = selected;
            }

            if (!selected.Add(systemNumber))
                selected.Remove(systemNumber);

            if (selected.Count > 0)
                _selectedQuantities[variationId] = selected.Count;
            else
                _selectedQuantities.Remove(variationId);

            UpdateSystemSelectionVisuals();
            UpdateTotal();
            BuildBookingDurationOptions();
        }


        private static string NormalizePhysicalSystemNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = ConvertPersianDigitsToEnglish(value.Trim());

            // استخراج شماره انتهایی:
            // Casual-7 => 7
            // Cas-1    => 1
            // VIP-16   => 16
            // [PC #01] => 01
            int end = normalized.Length - 1;

            while (end >= 0 && !char.IsDigit(normalized[end]))
                end--;

            if (end < 0)
                return normalized.Trim().ToUpperInvariant();

            int start = end;

            while (start >= 0 && char.IsDigit(normalized[start]))
                start--;

            string digits = normalized.Substring(start + 1, end - start);

            if (int.TryParse(
                digits,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }

            return digits;
        }

        private bool IsPhysicalSystemBooked(
            int variationId,
            string systemNumber,
            int startHour,
            int duration)
        {
            if (string.IsNullOrWhiteSpace(systemNumber))
                return false;

            int endHour = startHour + Math.Max(1, duration);

            string requestedNumber =
                NormalizePhysicalSystemNumber(systemNumber);

            string selectedDate =
                _selectedBookingDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            // بررسی رزروهای دستی
            foreach (ManualBooking booking in _selectedDateManualBookings)
            {
                if (!string.Equals(
                    booking.Date?.Trim(),
                    selectedDate,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (ManualBookingSystem system in
                         booking.Systems ?? new List<ManualBookingSystem>())
                {
                    if (system.VariationId != variationId)
                        continue;

                    // رزرو بدون شماره دستگاه، قابل انتساب به یک کاشی مشخص نیست.
                    if (string.IsNullOrWhiteSpace(system.SystemNumber))
                        continue;

                    string bookedNumber =
                        NormalizePhysicalSystemNumber(system.SystemNumber);

                    if (!string.Equals(
                        bookedNumber,
                        requestedNumber,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (string hourText in
                             system.Hours ?? new List<string>())
                    {
                        if (!TryGetHourNumber(hourText, out int bookedHour))
                            continue;

                        if (bookedHour >= startHour && bookedHour < endHour)
                            return true;
                    }
                }
            }

            // بررسی رزروهای آنلاین
            foreach (OnlineBooking booking in _selectedDateOnlineBookings)
            {
                if (!string.Equals(
                    booking.DateIso?.Trim(),
                    selectedDate,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                int bookedVariationId =
                    booking.VariationId > 0
                        ? booking.VariationId
                        : booking.VarId;

                if (bookedVariationId != variationId)
                    continue;

                if (string.IsNullOrWhiteSpace(booking.SystemNumber))
                    continue;

                string bookedNumber =
                    NormalizePhysicalSystemNumber(booking.SystemNumber);

                if (!string.Equals(
                    bookedNumber,
                    requestedNumber,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetHourNumber(
                    booking.StartTime,
                    out int bookedStart))
                {
                    continue;
                }

                int bookedEnd =
                    bookedStart + Math.Max(1, booking.Hours);

                // بررسی تداخل واقعی دو بازه زمانی
                if (startHour < bookedEnd && bookedStart < endHour)
                    return true;
            }

            return false;
        }


        private void UpdateSystemTileVisuals()
        {
            bool selectionChanged = false;

            foreach (var variationEntry in _systemTileButtons)
            {
                int variationId = variationEntry.Key;

                foreach (var tileEntry in variationEntry.Value)
                {
                    string number = tileEntry.Key;
                    Button tile = tileEntry.Value;

                    bool selected =
                        _selectedSystemNumbers.TryGetValue(
                            variationId,
                            out var selectedSet) &&
                        selectedSet.Contains(number);

                    int startHour = 0;

                    bool hasStart =
                        !string.IsNullOrWhiteSpace(_selectedStartHour) &&
                        TryGetHourNumber(_selectedStartHour, out startHour);

                    bool booked =
                        hasStart &&
                        IsPhysicalSystemBooked(
                            variationId,
                            number,
                            startHour,
                            _selectedBookingHours
                        );

                    // اگر دستگاه قبلاً رزرو شده باشد،
                    // از انتخاب جدید خارج می‌شود.
                    if (booked && selected && selectedSet != null)
                    {
                        selectedSet.Remove(number);
                        selected = false;
                        selectionChanged = true;
                    }

                    tile.IsEnabled =
                        hasStart &&
                        _availabilityLoadSucceeded &&
                        !booked;

                    tile.Opacity = booked ? 0.75 : 1.0;

                    tile.Content =
                        booked
                            ? "رزرو شده · " + number
                            : selected
                                ? "✓ " + number
                                : "دستگاه " + number;

                    // رنگ پس‌زمینه:
                    // رزرو قبلی = قرمز
                    // انتخاب جدید = آبی
                    // آزاد = سبز
                    tile.Background = new SolidColorBrush(
                        booked
                            ? Color.FromRgb(105, 35, 42)
                            : selected
                                ? Color.FromRgb(39, 68, 112)
                                : Color.FromRgb(28, 48, 41)
                    );

                    tile.Foreground = new SolidColorBrush(
                        booked
                            ? Color.FromRgb(255, 190, 190)
                            : selected
                                ? Color.FromRgb(220, 235, 255)
                                : Color.FromRgb(105, 226, 165)
                    );

                    tile.BorderBrush = new SolidColorBrush(
                        booked
                            ? Color.FromRgb(219, 60, 69)
                            : selected
                                ? Color.FromRgb(115, 174, 255)
                                : Color.FromRgb(57, 124, 91)
                    );

                    tile.ToolTip =
                        booked
                            ? "این دستگاه قبلاً برای این بازه رزرو شده است"
                            : selected
                                ? "برای لغو انتخاب کلیک کنید"
                                : "انتخاب این دستگاه";
                }

                if (_selectedSystemNumbers.TryGetValue(
                        variationId,
                        out var numbers))
                {
                    if (numbers.Count > 0)
                        _selectedQuantities[variationId] = numbers.Count;
                    else
                        _selectedQuantities.Remove(variationId);
                }
            }

            if (selectionChanged)
            {
                foreach (var entry in _systemCardQuantityTextBlocks)
                {
                    entry.Value.Text =
                        ToPersianNumber(GetSelectedQuantity(entry.Key));
                }

                UpdateTotal();
            }
        }


        // =====================================================
        // SYSTEM VISUALS
        // =====================================================



        private void UpdateSystemSelectionVisuals()
        {
            foreach (KeyValuePair<int, Border> item in _systemCardBorders)
            {
                int variationId = item.Key;
                int selectedQuantity = GetSelectedQuantity(variationId);

                // به‌روزرسانی ظاهر کارت
                SetSystemBorderVisual(item.Value, selectedQuantity > 0);

                // به‌روزرسانی تعداد انتخاب‌شده
                if (_systemCardQuantityTextBlocks.TryGetValue(
                    variationId, out TextBlock? quantityText))
                {
                    quantityText.Text = ToPersianNumber(selectedQuantity);
                }

                // به‌روزرسانی ظرفیت نمایش‌داده‌شده روی کارت
                if (_systemCardCapacityTextBlocks.TryGetValue(
                    variationId, out TextBlock? capacityText))
                {
                    UpdateSystemQuantityText(variationId, capacityText);
                }

                // به‌روزرسانی دکمه‌های افزایش و کاهش
                if (_systemCardMinusButtons.TryGetValue(
                        variationId, out Button? minusButton) &&
                    _systemCardPlusButtons.TryGetValue(
                        variationId, out Button? plusButton))
                {
                    UpdateSystemQuantityButtons(
                        variationId,
                        minusButton,
                        plusButton);
                }
            }
            UpdateLiveSystemCardStatuses();
            UpdateSystemTileVisuals();

        }


        // =====================================================
        // BUILD DYNAMIC SYSTEM CARDS — DARK THEME
        // =====================================================


        private void BuildSystemCards()
        {
            // پاک‌سازی کارت‌های قبلی
            

            _systemCardBorders.Clear();
            _systemCardImages.Clear();
            _systemCardCapacityTextBlocks.Clear();
            _systemCardPriceTextBlocks.Clear();
            _systemCardQuantityTextBlocks.Clear();
            _systemCardMinusButtons.Clear();
            _systemCardPlusButtons.Clear();
            _systemCardLiveStatusTextBlocks.Clear();
            _systemTilePanels.Clear();
            _systemTileButtons.Clear();
            _systemGameCombos.Clear();

            foreach (KeyValuePair<int, string> variation in _variationNames)
            {
                int variationId = variation.Key;
                string variationName = variation.Value;

                // =================================================
                // تصویر سیستم
                // =================================================

                var image = new Image
                {
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(8),
                    SnapsToDevicePixels = true
                };

                var imageContainer = new Border
                {
                    Height = 82,
                    Background = new SolidColorBrush(Color.FromRgb(37, 37, 48)),
                    CornerRadius = new CornerRadius(13),
                    ClipToBounds = true,
                    Child = image
                };

                // =================================================
                // نام سیستم
                // =================================================

                var title = new TextBlock
                {
                    Text = variationName,
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 247)),
                    Margin = new Thickness(0, 13, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };

                // =================================================
                // ظرفیت اسمی سیستم
                // =================================================

                var capacityLabel = new TextBlock
                {
                    Text = "ظرفیت کل: —",
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 194)),
                    Margin = new Thickness(0, 0, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                };

                // =================================================
                // وضعیت زنده ظرفیت در ساعت انتخاب‌شده
                // =================================================

                var liveStatusLabel = new TextBlock
                {
                    Text = "برای مشاهده ظرفیت، ابتدا یک ساعت انتخاب کن.",
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(184, 184, 199)),
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 46)),
                    Padding = new Thickness(9, 8, 9, 8),
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };

                // =================================================
                // قیمت
                // =================================================

                var priceLabel = new TextBlock
                {
                    Text = "قیمت: —",
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 238)),
                    Margin = new Thickness(0, 0, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                };

                // =================================================
                // دکمه کاهش تعداد
                // =================================================

                var minusButton = new Button
                {
                    Content = "−",
                    Width = 36,
                    Height = 36,
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromRgb(48, 48, 61)),
                    Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 247)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(78, 78, 94)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    IsEnabled = false,
                    Opacity = 1,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = "کاهش تعداد سیستم"
                };

                // =================================================
                // تعداد انتخاب‌شده
                // =================================================

                var quantityText = new TextBlock
                {
                    Text = "۰",
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 247)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };

                // =================================================
                // دکمه افزایش تعداد
                // =================================================

                var plusButton = new Button
                {
                    Content = "+",
                    Width = 36,
                    Height = 36,
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromRgb(219, 60, 69)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(219, 60, 69)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    IsEnabled = false,
                    Opacity = 1,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ToolTip = "افزایش تعداد سیستم"
                };

                // =================================================
                // چیدمان دکمه‌ها و تعداد
                // =================================================

                var quantityGrid = new Grid
                {
                    FlowDirection = FlowDirection.LeftToRight
                };

                quantityGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(42) });

                quantityGrid.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });

                quantityGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(42) });

                Grid.SetColumn(minusButton, 0);
                Grid.SetColumn(quantityText, 1);
                Grid.SetColumn(plusButton, 2);

                quantityGrid.Children.Add(minusButton);
                quantityGrid.Children.Add(quantityText);
                quantityGrid.Children.Add(plusButton);

                var quantityContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 56, 69)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Margin = new Thickness(0, 12, 0, 0),
                    Padding = new Thickness(5),
                    Child = quantityGrid
                };

                // =================================================
                // انتخاب دستگاه فیزیکی با کاشی‌های شماره‌دار
                // =================================================

                var tilePanel = new WrapPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                _systemTilePanels[variationId] = tilePanel;
                _systemTileButtons[variationId] = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

                List<string> physicalNumbers = _variationSystemNumbers.TryGetValue(variationId, out var knownNumbers)
                    ? knownNumbers : new List<string>();
                foreach (string systemNumber in physicalNumbers)
                {
                    var tile = new Button
                    {
                        Content = "دستگاه " + systemNumber,
                        Tag = systemNumber,
                        MinWidth = 76,
                        Height = 48,
                        Margin = new Thickness(3),
                        Padding = new Thickness(7, 4, 7, 4),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Background = new SolidColorBrush(Color.FromRgb(28, 48, 41)),
                        Foreground = new SolidColorBrush(Color.FromRgb(105, 226, 165)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(57, 124, 91)),
                        BorderThickness = new Thickness(1),
                        IsEnabled = false,
                        ToolTip = "برای انتخاب دستگاه، تاریخ و ساعت را مشخص کنید"
                    };
                    tile.Click += (_, e) =>
                    {
                        TogglePhysicalSystem(variationId, systemNumber);
                        e.Handled = true;
                    };
                    _systemTileButtons[variationId][systemNumber] = tile;
                    tilePanel.Children.Add(tile);
                }

                var gameCombo = new ComboBox
                {
                    Height = 40,
                    MinWidth = 190,
                    Margin = new Thickness(0, 9, 0, 0),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(247, 247, 252)),
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 47)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(82, 67, 105)),
                    BorderThickness = new Thickness(1),
                    IsEnabled = false,
                    ToolTip = "بازی اختیاری برای این نوع سیستم"
                };
                gameCombo.Resources[SystemColors.WindowBrushKey] = new SolidColorBrush(Color.FromRgb(35, 35, 47));
                gameCombo.Resources[SystemColors.ControlBrushKey] = new SolidColorBrush(Color.FromRgb(35, 35, 47));
                gameCombo.ItemContainerStyle = new Style(typeof(ComboBoxItem))
                {
                    Setters =
                    {
                        new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(247, 247, 252))),
                        new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(35, 35, 47))),
                        new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)),
                        new Setter(Control.FontFamilyProperty, new FontFamily("Peyda"))
                    }
                };
                gameCombo.ItemContainerStyle.Triggers.Add(new Trigger
                {
                    Property = ComboBoxItem.IsHighlightedProperty,
                    Value = true,
                    Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(76, 52, 102))) }
                });
                gameCombo.Items.Add(new KeyValuePair<int, string>(0, "بدون انتخاب بازی"));
                if (_variationGames.TryGetValue(variationId, out var variationGames))
                {
                    foreach (var game in variationGames)
                        gameCombo.Items.Add(new KeyValuePair<int, string>(game.Id, game.Name));
                }
                gameCombo.DisplayMemberPath = "Value";
                gameCombo.SelectedValuePath = "Key";
                gameCombo.SelectedIndex = 0;
                gameCombo.IsEnabled = variationGames != null && variationGames.Count > 0;
                _systemGameCombos[variationId] = gameCombo;

                // =================================================
                // محتوای کارت
                // =================================================

                var content = new StackPanel
                {
                    Orientation = Orientation.Vertical
                };

                content.Children.Add(imageContainer);
                content.Children.Add(title);
                content.Children.Add(capacityLabel);
                content.Children.Add(liveStatusLabel);
                content.Children.Add(priceLabel);

                // Read-only selection count. Operators must select/deselect
                // individual physical systems on the floor map, never by quantity.
                var mapSelectionSummary = new Grid
                {
                    Margin = new Thickness(0, 12, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 42))
                };
                mapSelectionSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                mapSelectionSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var mapSelectionLabel = new TextBlock
                {
                    Text = "انتخاب‌شده از نقشه سالن",
                    FontFamily = new FontFamily("Peyda"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(190, 192, 207)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 8, 4, 8)
                };
                quantityText.Margin = new Thickness(8, 6, 10, 6);
                quantityText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(quantityText, 1);
                mapSelectionSummary.Children.Add(mapSelectionLabel);
                mapSelectionSummary.Children.Add(quantityText);
                content.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 42)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(56, 56, 69)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Child = mapSelectionSummary
                });
                if (physicalNumbers.Count > 0)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "برای انتخاب یا لغو انتخاب، روی دستگاه موردنظر در نقشه سالن کلیک کنید.",
                        FontFamily = new FontFamily("Peyda"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(160, 163, 179)),
                        Margin = new Thickness(0, 7, 0, 2),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                if (variationGames != null && variationGames.Count > 0)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "بازی (اختیاری)",
                        FontFamily = new FontFamily("Peyda"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(190, 192, 207)),
                        Margin = new Thickness(0, 7, 0, 2)
                    });
                    content.Children.Add(gameCombo);
                }

                // =================================================
                // کارت سیستم
                // =================================================

                var card = new Border
                {
                    Width = 218,
                    Margin = new Thickness(7),
                    Padding = new Thickness(16),
                    CornerRadius = new CornerRadius(15),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(57, 57, 70)),
                    Background = new SolidColorBrush(Color.FromRgb(28, 28, 37)),
                    SnapsToDevicePixels = true,
                    Child = content
                };

                // =================================================
                // رویدادهای دکمه‌ها
                // =================================================

                // Quantity stepper is intentionally not rendered or wired.
                // Only the map callback can change _selectedSystemNumbers.
                plusButton.IsEnabled = false;
                minusButton.IsEnabled = false;
                plusButton.Visibility = Visibility.Collapsed;
                minusButton.Visibility = Visibility.Collapsed;
                quantityText.Text = "۰";

                // =================================================
                // ذخیره ارجاع کنترل‌ها برای به‌روزرسانی بعدی
                // =================================================

                _systemCardBorders[variationId] = card;
                _systemCardImages[variationId] = image;
                _systemCardCapacityTextBlocks[variationId] = capacityLabel;
                _systemCardPriceTextBlocks[variationId] = priceLabel;
                _systemCardQuantityTextBlocks[variationId] = quantityText;
                _systemCardMinusButtons[variationId] = minusButton;
                _systemCardPlusButtons[variationId] = plusButton;
                _systemCardLiveStatusTextBlocks[variationId] = liveStatusLabel;

                
            }

            // به‌روزرسانی تعداد انتخاب‌شده و دکمه‌ها
            UpdateSystemSelectionVisuals();

            // نمایش ظرفیت موجود در داده‌های فعلی API
            UpdateLiveSystemCardStatuses();
        }


        // =====================================================
        // SYSTEM VISUALS
        // =====================================================



        // =====================================================
        // SYSTEM QUANTITY TEXT
        // =====================================================

        private void UpdateSystemQuantityText(
            int variationId,
            TextBlock textBlock)
        {
            int selected =
                GetSelectedQuantity(
                    variationId
                );


            if (!_variationCapacities.TryGetValue(
                    variationId,
                    out int capacity))
            {
                textBlock.Text =
                    "ظرفیت: —";

                return;
            }


            if (selected > 0)
            {
                textBlock.Text =
                    $"ظرفیت: {ToPersianNumber(capacity)} سیستم | " +
                    $"انتخاب: {ToPersianNumber(selected)}";
            }
            else
            {
                textBlock.Text =
                    $"ظرفیت: {ToPersianNumber(capacity)} سیستم";
            }
        }



        // =====================================================
        // SYSTEM QUANTITY BUTTONS — DARK THEME
        // =====================================================

        private void UpdateSystemQuantityButtons(
            int variationId,
            Button minusButton,
            Button plusButton)
        {
            int selected = GetSelectedQuantity(variationId);

            // When the server exposes physical device numbers, selection must be
            // made from those numbered tiles rather than from an anonymous quantity.
            if (_variationSystemNumbers.TryGetValue(variationId, out List<string>? physicalNumbers) &&
                physicalNumbers.Count > 0)
            {
                minusButton.IsEnabled = false;
                plusButton.IsEnabled = false;
                minusButton.Opacity = 0.45;
                plusButton.Opacity = 0.45;
                return;
            }

            // MINUS BUTTON
            bool canDecrease = selected > 0;

            minusButton.IsEnabled = canDecrease;
            minusButton.Opacity = 1;

            minusButton.Background = new SolidColorBrush(
                canDecrease
                    ? Color.FromRgb(65, 65, 80)
                    : Color.FromRgb(42, 42, 53));

            minusButton.Foreground = new SolidColorBrush(
                canDecrease
                    ? Color.FromRgb(255, 255, 255)
                    : Color.FromRgb(155, 155, 170));

            minusButton.BorderBrush = new SolidColorBrush(
                canDecrease
                    ? Color.FromRgb(95, 95, 112)
                    : Color.FromRgb(62, 62, 76));

            // PLUS BUTTON
            bool canIncrease = false;

            if (!string.IsNullOrWhiteSpace(_selectedStartHour))
            {
                int available = GetAvailableQuantityAtHour(
                    variationId,
                    _selectedStartHour);

                canIncrease = available > selected;
            }

            plusButton.IsEnabled = canIncrease;
            plusButton.Opacity = 1;

            plusButton.Background = new SolidColorBrush(
                canIncrease
                    ? Color.FromRgb(219, 60, 69)
                    : Color.FromRgb(85, 43, 49));

            plusButton.Foreground = new SolidColorBrush(
                canIncrease
                    ? Color.FromRgb(255, 255, 255)
                    : Color.FromRgb(205, 185, 188));

            plusButton.BorderBrush = new SolidColorBrush(
                canIncrease
                    ? Color.FromRgb(235, 82, 91)
                    : Color.FromRgb(105, 58, 64));
        }

        // =====================================================
        // SYSTEM BORDER VISUAL
        // =====================================================

        private static void SetSystemBorderVisual(
            Border border,
            bool selected)
        {
            if (selected)
            {
                border.Background = new SolidColorBrush(
                    Color.FromRgb(39, 68, 112));

                border.BorderBrush = new SolidColorBrush(
                    Color.FromRgb(115, 174, 255));

                border.BorderThickness = new Thickness(2);
                border.Opacity = 1.0;
            }
            else
            {
                border.Background = new SolidColorBrush(
                    Color.FromRgb(28, 28, 37));

                border.BorderBrush = new SolidColorBrush(
                    Color.FromRgb(57, 57, 70));

                border.BorderThickness = new Thickness(1);
                border.Opacity = 1.0;
            }
        }


        // =====================================================
        // CHECK WHETHER ANY SYSTEM CAN BE BOOKED AT THIS HOUR
        // =====================================================



        private bool IsHourBookableForAnySystem(string hour)
        {
            if (!IsWithinWorkingHours(hour))
            {
                return false;
            }

            foreach (int variationId in _availability.Keys)
            {
                if (IsVariationBookableAtHour(variationId, hour, 1))
                {
                    return true;
                }
            }

            return false;
        }



        // =====================================================
        // VARIATION BOOKABLE - ONE SYSTEM
        // =====================================================

        private bool IsVariationBookableAtHour(
            int variationId,
            string hour)
        {
            return IsVariationBookableAtHour(
                variationId,
                hour,
                1
            );
        }


        // =====================================================
        // VARIATION BOOKABLE - QUANTITY
        // =====================================================



        private bool IsVariationBookableAtHour(
            int variationId,
            string hour,
            int requiredQuantity)
        {
            if (requiredQuantity < 1)
                return false;

            if (string.IsNullOrWhiteSpace(hour))
                return false;

            if (!_availability.TryGetValue(
                    variationId,
                    out AvailabilityResponse? response))
            {
                return false;
            }

            if (response?.Availability == null)
                return false;

            List<AvailabilityHour>? hours = null;

            if (response.Availability.Hours != null &&
                response.Availability.Hours.Count > 0)
            {
                hours = response.Availability.Hours;
            }
            else if (response.Availability.Slots != null &&
                     response.Availability.Slots.Count > 0)
            {
                hours = response.Availability.Slots;
            }

            if (hours == null || hours.Count == 0)
                return false;

            string requestedHour = NormalizeHour(hour);

            foreach (AvailabilityHour item in hours)
            {
                if (item == null)
                    continue;


                string availableHour =
                    NormalizeHour(item.Hour.ToString());

                if (!string.Equals(
                        availableHour,
                        requestedHour,
                        StringComparison.Ordinal))
                {
                    continue;
                }




                return item.Bookable &&
                       item.Available >= requiredQuantity;

            }

            return false;
        }



        // =====================================================
        // GET AVAILABLE QUANTITY AT HOUR
        // =====================================================

        private int GetAvailableQuantityAtHour(
            int variationId,
            string hour)
        {
            if (!_availability.TryGetValue(
                    variationId,
                    out AvailabilityResponse? response))
            {
                return 0;
            }


            foreach (
                AvailabilityHour availabilityHour
                in response.Availability.Hours)
            {
                if (!string.Equals(
                        NormalizeHour(
                            availabilityHour.Hour.ToString()),
                        NormalizeHour(hour),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!availabilityHour.Bookable)
                    return 0;

                return Math.Max(
                    0,
                    availabilityHour.Available
                );
            }


            return 0;
        }


        // =====================================================
        // SELECTED SYSTEMS CAPACITY ERROR
        // =====================================================

        private string? GetSelectedSystemsCapacityError(
            int startHour,
            int duration)
        {
            if (duration < 1)
                return "مدت رزرو نامعتبر است.";

            foreach (
                KeyValuePair<int, int> selection
                in _selectedQuantities)
            {
                int variationId =
                    selection.Key;

                int quantity =
                    selection.Value;

                if (quantity <= 0)
                    continue;

                for (
                    int offset = 0;
                    offset < duration;
                    offset++)
                {
                    int currentHour =
                        startHour + offset;

                    if (currentHour >= _workEnd)
                    {
                        return
                            $"رزرو سیستم {GetVariationName(variationId)} تا این مدت خارج از ساعت کاری گیمنت است.";
                    }

                    string hour =
                        $"{currentHour:00}:00";

                    int available =
                        GetAvailableQuantityAtHour(
                            variationId,
                            hour
                        );

                    if (available < quantity)
                    {
                        return BuildCapacityMessage(
                            variationId,
                            hour,
                            quantity,
                            available
                        );
                    }
                }
            }

            return null;
        }


        // =====================================================
        // CAPACITY MESSAGE
        // =====================================================

        private string BuildCapacityMessage(
            int variationId,
            string hour,
            int requiredQuantity,
            int availableQuantity)
        {
            string systemName =
                GetVariationName(
                    variationId
                );

            if (availableQuantity <= 0)
            {
                return
                    $"در ساعت {ToPersianNumber(hour)} هیچ سیستم {systemName} آزادی وجود ندارد؛ شما {ToPersianNumber(requiredQuantity)} سیستم انتخاب کرده‌اید.";
            }

            return
                $"در ساعت {ToPersianNumber(hour)} فقط {ToPersianNumber(availableQuantity)} سیستم {systemName} آزاد است؛ شما {ToPersianNumber(requiredQuantity)} سیستم انتخاب کرده‌اید.";
        }


        // =====================================================
        // START HOUR VALIDATION
        // =====================================================

        private string? ValidateStartHour(
            DateTime date,
            string hour)
        {
            if (date.Date < DateTime.Today)
            {
                return
                    "امکان رزرو برای تاریخ گذشته وجود ندارد.";
            }

            if (!TryGetHourNumber(
                    hour,
                    out int hourNumber))
            {
                return
                    "ساعت شروع رزرو نامعتبر است.";
            }

            if (hourNumber < _workStart ||
                hourNumber >= _workEnd)
            {
                return
                    $"ساعت انتخاب‌شده خارج از ساعت کاری گیمنت است. ساعت کاری گیمنت از {ToPersianNumber($"{_workStart:00}:00")} تا {ToPersianNumber($"{_workEnd:00}:00")} است.";
            }

            if (date.Date == DateTime.Today)
            {
                DateTime now =
                    DateTime.Now;

                if (hourNumber <= now.Hour)
                {
                    return
                        $"ساعت {ToPersianNumber(hour)} گذشته است و برای امروز قابل رزرو نیست.";
                }
            }

            if (!IsHourBookableForAnySystem(hour))
            {
                return
                    $"در ساعت {ToPersianNumber(hour)} ظرفیت هیچ‌یک از سیستم‌ها برای رزرو موجود نیست.";
            }

            return null;
        }


        // =====================================================
        // WORKING HOURS
        // =====================================================

        private bool IsWithinWorkingHours(
            string hour)
        {
            if (!TryGetHourNumber(
                    hour,
                    out int hourNumber))
            {
                return false;
            }

            return hourNumber >= _workStart &&
                   hourNumber < _workEnd;
        }


        // =====================================================
        // PAST BOOKING HOUR
        // =====================================================

        private static bool IsPastBookingHour(
            DateTime date,
            string hour)
        {
            if (date.Date != DateTime.Today)
                return false;

            if (!TryGetHourNumber(
                    hour,
                    out int hourNumber))
            {
                return true;
            }

            return hourNumber <= DateTime.Now.Hour;
        }


        // =====================================================
        // PARSE HOUR
        // =====================================================

        private static bool TryGetHourNumber(
            string hour,
            out int hourNumber)
        {
            hourNumber = 0;

            if (string.IsNullOrWhiteSpace(hour))
                return false;

            string normalized =
                NormalizeHour(hour);

            normalized =
                ConvertPersianDigitsToEnglish(
                    normalized
                );

            string[] parts =
                normalized.Split(':');

            if (parts.Length == 0)
                return false;

            return int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out hourNumber
            );
        }


        // =====================================================
        // NORMALIZE HOUR
        // =====================================================


        private static string NormalizeHour(string hour)
        {
            if (string.IsNullOrWhiteSpace(hour))
                return string.Empty;

            string normalized = ConvertPersianDigitsToEnglish(
                hour.Trim()
            );

            // اگر ساعت به شکل 08:00 یا 8:00 باشد،
            // فقط بخش ساعت را نگه می‌داریم.
            if (normalized.Contains(':'))
            {
                string[] parts = normalized.Split(':');

                if (parts.Length > 0)
                    normalized = parts[0];
            }

            // تبدیل 08 به 8
            if (int.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int hourNumber))
            {
                return hourNumber.ToString(
                    CultureInfo.InvariantCulture
                );
            }

            return normalized;
        }



        // =====================================================
        // NORMALIZE HOUR FOR DISPLAY
        // =====================================================

        private static string NormalizeHourForDisplay(
            string hour)
        {
            if (!TryGetHourNumber(
                    hour,
                    out int hourNumber))
            {
                return hour.Trim();
            }

            return $"{hourNumber:00}:00";
        }


        // =====================================================
        // COMPARE HOURS
        // =====================================================

        private static int CompareHours(
            string left,
            string right)
        {
            bool leftOk =
                TryGetHourNumber(
                    left,
                    out int leftHour
                );

            bool rightOk =
                TryGetHourNumber(
                    right,
                    out int rightHour
                );

            if (!leftOk && !rightOk)
                return string.Compare(
                    left,
                    right,
                    StringComparison.Ordinal
                );

            if (!leftOk)
                return 1;

            if (!rightOk)
                return -1;

            return leftHour.CompareTo(
                rightHour
            );
        }


        // =====================================================
        // TOTAL
        // =====================================================

        private void UpdateTotal()
        {
            if (GetSelectedSystemCount() == 0)
            {
                BookingTotalTextBlock.Text =
                    "۰ تومان";

                return;
            }

            decimal total =
                0;

            foreach (
                KeyValuePair<int, int> selection
                in _selectedQuantities)
            {
                int variationId =
                    selection.Key;

                int quantity =
                    selection.Value;

                if (quantity <= 0)
                    continue;

                if (_variationPrices.TryGetValue(
                        variationId,
                        out decimal price))
                {
                    total +=
                        price *
                        quantity *
                        _selectedBookingHours;
                }
            }

            BookingTotalTextBlock.Text =
                $"{ToPersianNumber(total.ToString("N0", CultureInfo.InvariantCulture))} تومان";

            if (BookingReviewSummaryTextBlock != null)
                BuildBookingReviewSummary();
        }


        // =====================================================
        // CREATE REAL BOOKING
        // =====================================================

        private async void CreateRealBookingButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var submitButton = sender as Button;
            try
            {
                if (submitButton != null) { submitButton.IsEnabled = false; submitButton.Content = "در حال بررسی و ثبت..."; }
                CreateRealBookingButton.IsEnabled = false;
                CreateRealBookingButton.Content = "در حال بررسی و ثبت...";
                SetManualLoading(true, "در حال بررسی ظرفیت و ثبت رزرو با سرور لابیکس...");

                SetBookingInfo(
                    "در حال بررسی اطلاعات رزرو..."
                );

                bool isOnline = await _apiService.CheckConnectionAsync();

                ConnectionStateService.Instance.SetState(
                    isOnline
                        ? LobixConnectionState.Online
                        : LobixConnectionState.Offline
                );

                DateTime bookingDate =
                    _selectedBookingDate.Date;


                // -------------------------------------------------
                // DATE
                // -------------------------------------------------

                if (bookingDate < DateTime.Today)
                {
                    throw new Exception(
                        "امکان رزرو برای تاریخ گذشته وجود ندارد."
                    );
                }


                // -------------------------------------------------
                // START HOUR
                // -------------------------------------------------

                if (string.IsNullOrWhiteSpace(
                        _selectedStartHour))
                {
                    throw new Exception(
                        "ساعت شروع رزرو را انتخاب کنید."
                    );
                }

                string? startHourError =
                    ValidateStartHour(
                        bookingDate,
                        _selectedStartHour
                    );

                if (!string.IsNullOrWhiteSpace(
                        startHourError))
                {
                    throw new Exception(
                        startHourError
                    );
                }


                // -------------------------------------------------
                // SYSTEM
                // -------------------------------------------------

                if (GetSelectedSystemCount() == 0)
                {
                    throw new Exception(
                        "حداقل یک سیستم را انتخاب کنید."
                    );
                }


                // -------------------------------------------------
                // FRESH CAPACITY CHECK
                // -------------------------------------------------

                SetBookingInfo(
                    "در حال بررسی ظرفیت لحظه‌ای سیستم‌ها..."
                );

                if (isOnline)
                {
                    await RefreshAvailabilityForBookingAsync(
                        bookingDate
                    );
                }


                // -------------------------------------------------
                // RE-CHECK SELECTED SYSTEMS
                // -------------------------------------------------

                if (!TryGetHourNumber(
                        _selectedStartHour,
                        out int freshStartHour))
                {
                    throw new Exception(
                        "ساعت شروع رزرو نامعتبر است."
                    );
                }

                string? freshCapacityError =
                    GetSelectedSystemsCapacityError(
                        freshStartHour,
                        _selectedBookingHours
                    );

                if (!string.IsNullOrWhiteSpace(
                        freshCapacityError))
                {
                    UpdateSystemSelectionVisuals();

                    BuildBookingDurationOptions();

                    UpdateTotal();

                    throw new Exception(
                        freshCapacityError
                    );
                }


                // -------------------------------------------------
                // DURATION
                // -------------------------------------------------

                if (_selectedBookingHours < 1)
                {
                    throw new Exception(
                        "مدت رزرو نامعتبر است."
                    );
                }

                string? durationError =
                    GetDurationValidationError(
                        _selectedBookingHours
                    );

                if (!string.IsNullOrWhiteSpace(
                        durationError))
                {
                    throw new Exception(
                        durationError
                    );
                }


                // -------------------------------------------------
                // CUSTOMER
                // -------------------------------------------------

                string customer =
                    BookingCustomerTextBox.Text.Trim();


                // -------------------------------------------------
                // CUSTOMER ID
                // -------------------------------------------------

                string customerId =
                    BookingCustomerIdTextBox.Text.Trim();

                // اگر فقط ID وارد شده باشد،
                // همان ID به عنوان نام/نمایش مشتری نیز ذخیره می‌شود.
                if (string.IsNullOrWhiteSpace(customer))
                {
                    if (!string.IsNullOrWhiteSpace(customerId))
                        customer = customerId;
                    else if (!string.IsNullOrWhiteSpace(BookingPhoneTextBox.Text.Trim()))
                        customer = ConvertPersianDigitsToEnglish(BookingPhoneTextBox.Text.Trim());
                }


                // -------------------------------------------------
                // PHONE
                // -------------------------------------------------

                string phone =
                    BookingPhoneTextBox.Text.Trim();


                // -------------------------------------------------
                // PHONE VALIDATION
                // -------------------------------------------------

                if (!string.IsNullOrWhiteSpace(phone))
                {
                    string normalizedPhone =
                        ConvertPersianDigitsToEnglish(
                            phone
                        );

                    if (normalizedPhone.Length != 11 ||
                        !normalizedPhone.StartsWith(
                            "09",
                            StringComparison.Ordinal
                        ))
                    {
                        throw new Exception(
                            "شماره موبایل باید دقیقاً ۱۱ رقم و با ۰۹ شروع شود؛ مثال: ۰۹۱۲۳۴۵۶۷۸۹"
                        );
                    }

                    phone =
                        normalizedPhone;
                }

                // -------------------------------------------------
                // CUSTOMER VALIDATION
                // -------------------------------------------------

                if (string.IsNullOrWhiteSpace(customer) &&
                    string.IsNullOrWhiteSpace(phone) &&
                    string.IsNullOrWhiteSpace(customerId))
                {
                    throw new Exception(
                        "حداقل یکی از نام، شماره موبایل یا ID مشتری را وارد کنید."
                    );
                }


                // -------------------------------------------------
                // NOTE
                // -------------------------------------------------

                string note =
                    BookingNoteTextBox.Text.Trim();

                string selectedGame = BookingGameTextBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(selectedGame))
                {
                    string gameNote = "بازی انتخاب‌شده: " + selectedGame;
                    note = string.IsNullOrWhiteSpace(note) ? gameNote : note + Environment.NewLine + gameNote;
                    if (note.Length > 1000)
                        throw new Exception("مجموع توضیحات و نام بازی نباید بیشتر از ۱۰۰۰ نویسه باشد.");
                }


                // -------------------------------------------------
                // SYSTEMS
                // -------------------------------------------------

                List<BookingSystemRequest> systems =
                    new();

                decimal totalAmount =
                    0;

                foreach (
                    KeyValuePair<int, int> selection
                    in _selectedQuantities)
                {
                    int variationId =
                        selection.Key;

                    int quantity =
                        selection.Value;

                    if (quantity <= 0)
                        continue;

                    string? systemError =
                        ValidateVariationForBooking(
                            variationId,
                            quantity
                        );

                    if (!string.IsNullOrWhiteSpace(
                            systemError))
                    {
                        throw new Exception(
                            systemError
                        );
                    }

                    if (!_variationPrices.TryGetValue(
                            variationId,
                            out decimal price))
                    {
                        throw new Exception(
                            $"قیمت سیستم {GetVariationName(variationId)} مشخص نیست."
                        );
                    }

                    if (_selectedSystemNumbers.TryGetValue(variationId, out HashSet<string>? selectedNumbers) && selectedNumbers.Count > 0)
                    {
                        int? selectedGameId = null;
                        if (_systemGameCombos.TryGetValue(variationId, out ComboBox? gameCombo) &&
                            gameCombo.SelectedValue is int gameId && gameId > 0)
                            selectedGameId = gameId;

                        foreach (string systemNumber in selectedNumbers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                        {
                            systems.Add(new BookingSystemRequest
                            {
                                VariationId = variationId,
                                Quantity = 1,
                                SystemNumber = systemNumber,
                                SystemId = _physicalSystemIdsByNumber.TryGetValue(systemNumber, out string? physicalSystemId) ? physicalSystemId : null,
                                GameId = selectedGameId
                            });
                        }
                        quantity = selectedNumbers.Count;
                    }
                    else
                    {
                        // Variations without physical-number metadata retain the legacy quantity flow.
                        int? selectedGameId = null;
                        if (_systemGameCombos.TryGetValue(variationId, out ComboBox? gameCombo) &&
                            gameCombo.SelectedValue is int gameId && gameId > 0)
                            selectedGameId = gameId;

                        systems.Add(new BookingSystemRequest
                        {
                            VariationId = variationId,
                            Quantity = quantity,
                            GameId = selectedGameId
                        });
                    }

                    totalAmount += price * quantity * _selectedBookingHours;
                }

                if (systems.Count == 0)
                {
                    throw new Exception(
                        "هیچ سیستمی برای رزرو انتخاب نشده است."
                    );
                }


                // -------------------------------------------------
                // CREATE REQUEST
                // -------------------------------------------------

                BookingCreateRequest request =
                    new BookingCreateRequest
                    {
                        ProductId =
                            _gamenet.Id,

                        Date =
                            bookingDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture
                            ),

                        StartTime =
                            _selectedStartHour,

                        Hours =
                            _selectedBookingHours,

                        Customer =
                            customer,

                        CustomerId =
                            customerId,

                        Phone =
                            phone,

                        Amount =
                            totalAmount,

                        Note =
                            note,

                        Systems =
                            systems
                    };


                if (!isOnline)
                {
                    // Save locally and enqueue the operation atomically.
                    string localBookingId =
                        "offline-" + Guid.NewGuid().ToString("N");

                    var localReservation = OfflineBookingMapper.ToReservation(
                        request, localBookingId, "Pending");

                    string operationId =
                        await LocalDatabase.Instance.SaveLocalChangeWithPayloadAsync(
                            request.ProductId,
                            "ManualBooking",
                            localBookingId,
                            localReservation,
                            "Create",
                            request,
                            0
                        );

                    _lastCreatedBookingId = localBookingId;

                    ConnectionStateService.Instance.SetState(
                        LobixConnectionState.Offline
                    );

                    SetBookingSuccess(
                        "رزرو روی این کامپیوتر ذخیره شد و در انتظار همگام‌سازی است.\n" +
                        "شناسه محلی: " + localBookingId
                    );

                    SetBookingInfo(
                        "این رزرو هنوز توسط سرور تأیید نشده است."
                    );

                    return;
                }

                // Online path: validate physical devices with the server first.
                await ValidatePhysicalSystemsWithServerAsync(
                    bookingDate,
                    _selectedStartHour,
                    _selectedBookingHours,
                    systems
                );

                string idempotencyKey =
                    Guid.NewGuid().ToString("N");

                SetBookingInfo("در حال ثبت رزرو...");

                BookingCreateResponse response =
                    await _apiService.CreateBookingAsync(
                        request,
                        idempotencyKey
                    );

                if (!response.Success ||
                    response.Booking == null ||
                    string.IsNullOrWhiteSpace(response.Booking.Id))
                {
                    throw new Exception(
                        string.IsNullOrWhiteSpace(response.Message)
                            ? "ثبت رزرو ناموفق بود."
                            : response.Message
                    );
                }

                _lastCreatedBookingId = response.Booking.Id;

                SetBookingSuccess(
                    $"رزرو با موفقیت ثبت شد. شناسه: {_lastCreatedBookingId}"
                );


                // -------------------------------------------------
                // REFRESH AVAILABILITY
                // -------------------------------------------------

                await LoadAvailabilityAsync(
                    bookingDate
                );


                // -------------------------------------------------
                // REFRESH TODAY BOOKINGS COUNT
                // -------------------------------------------------

                await LoadTodayBookingsAsync();


                // -------------------------------------------------
                // CLEAR CUSTOMER DATA
                // -------------------------------------------------

                BookingCustomerTextBox.Clear();

                BookingCustomerIdTextBox.Clear();

                BookingPhoneTextBox.Clear();

                BookingNoteTextBox.Clear();
                BookingGameTextBox.Clear();

                _selectedQuantities.Clear();
                foreach (HashSet<string> selectedNumbers in _selectedSystemNumbers.Values)
                    selectedNumbers.Clear();

                _selectedStartHour = null;

                _selectedBookingHours = 1;
                _durationExplicitlySelected = false;

                UpdateSystemSelectionVisuals();

                UpdateTotal();

                if (BookingDurationPanel != null)
                {
                    BookingDurationPanel.Children.Clear();
                }

                UpdateDurationVisuals();
                ShowBookingWizardStep(0);
            }
            catch (Exception ex)
            {
                SetBookingError(UserFacingErrors.GetMessage(ex,
                    "ثبت رزرو ناموفق بود. پیش از تلاش مجدد، جدول رزروها را تازه‌سازی کنید تا رزرو تکراری ثبت نشود."));
            }
            finally
            {
                SetManualLoading(false);
                CreateRealBookingButton.Content = "تأیید و ثبت رزرو";
                CreateRealBookingButton.IsEnabled = true;
                if (submitButton != null) { submitButton.Content = "تأیید و ثبت رزرو"; submitButton.IsEnabled = true; }
            }
        }


        // =====================================================
        // VALIDATE VARIATION FOR BOOKING
        // =====================================================

        private string? ValidateVariationForBooking(
            int variationId,
            int quantity)
        {
            if (quantity < 1)
            {
                return
                    $"تعداد انتخاب‌شده برای سیستم {GetVariationName(variationId)} نامعتبر است.";
            }

            if (string.IsNullOrWhiteSpace(
                    _selectedStartHour))
            {
                return
                    "ساعت شروع رزرو انتخاب نشده است.";
            }

            if (!TryGetHourNumber(
                    _selectedStartHour,
                    out int startHour))
            {
                return
                    "ساعت شروع رزرو نامعتبر است.";
            }

            for (
                int offset = 0;
                offset < _selectedBookingHours;
                offset++)
            {
                int currentHour =
                    startHour + offset;

                if (currentHour >= _workEnd)
                {
                    return
                        $"رزرو سیستم {GetVariationName(variationId)} تا این مدت خارج از ساعت کاری گیمنت است.";
                }

                string hour =
                    $"{currentHour:00}:00";

                int available =
                    GetAvailableQuantityAtHour(
                        variationId,
                        hour
                    );

                if (available < quantity)
                {
                    return BuildCapacityMessage(
                        variationId,
                        hour,
                        quantity,
                        available
                    );
                }
            }

            return null;
        }


        // =====================================================
        // HOUR BUTTON NORMAL STYLE
        // =====================================================


        private static void ApplyNormalHourButtonStyle(Button button)
        {
            button.Background = new SolidColorBrush(
                Color.FromRgb(35, 35, 46));

            button.Foreground = new SolidColorBrush(
                Color.FromRgb(235, 235, 242));

            button.BorderBrush = new SolidColorBrush(
                Color.FromRgb(65, 65, 80));

            button.BorderThickness = new Thickness(1);
        }



        // =====================================================
        // HOUR BUTTON SELECTED STYLE
        // =====================================================


        private static void ApplySelectedHourButtonStyle(Button button)
        {
            button.Background = new SolidColorBrush(
                Color.FromRgb(219, 60, 69));

            button.Foreground = Brushes.White;

            button.BorderBrush = new SolidColorBrush(
                Color.FromRgb(235, 82, 91));

            button.BorderThickness = new Thickness(1);
        }



        // =====================================================
        // DURATION BUTTON NORMAL STYLE
        // =====================================================


        private static void ApplyNormalDurationButtonStyle(Button button)
        {
            button.Background = new SolidColorBrush(
                Color.FromRgb(35, 35, 46));

            button.Foreground = new SolidColorBrush(
                Color.FromRgb(235, 235, 242));

            button.BorderBrush = new SolidColorBrush(
                Color.FromRgb(65, 65, 80));

            button.BorderThickness = new Thickness(1);
        }


        // =====================================================
        // DURATION BUTTON SELECTED STYLE
        // =====================================================


        private static void ApplySelectedDurationButtonStyle(Button button)
        {
            button.Background = new SolidColorBrush(
                Color.FromRgb(219, 60, 69));

            button.Foreground = Brushes.White;

            button.BorderBrush = new SolidColorBrush(
                Color.FromRgb(219, 60, 69));

            button.BorderThickness = new Thickness(1);
        }



        // =====================================================
        // VARIATION NAME
        // =====================================================

        private string GetVariationName(int variationId)
        {
            if (_variationNames.TryGetValue(
                    variationId,
                    out string? variationName) &&
                !string.IsNullOrWhiteSpace(variationName))
            {
                return variationName;
            }

            return $"سیستم {variationId}";
        }


        // =====================================================
        // STATUS HELPERS
        // =====================================================

        private void SetBookingError(
            string message)
        {
            BookingFormStatusTextBlock.Foreground =
                Brushes.IndianRed;

            BookingFormStatusTextBlock.Text =
                message;

            if (BookingReviewStatusTextBlock != null)
            {
                BookingReviewStatusTextBlock.Foreground = Brushes.IndianRed;
                BookingReviewStatusTextBlock.Text = message;
            }
        }

        private void SetBookingInfo(
            string message)
        {
            BookingFormStatusTextBlock.Foreground =
                Brushes.Gray;

            BookingFormStatusTextBlock.Text =
                message;

            if (BookingReviewStatusTextBlock != null)
            {
                BookingReviewStatusTextBlock.Foreground = Brushes.LightGray;
                BookingReviewStatusTextBlock.Text = message;
            }
        }

        private void SetBookingSuccess(
            string message)
        {
            BookingFormStatusTextBlock.Foreground =
                Brushes.DarkGreen;

            BookingFormStatusTextBlock.Text =
                message;

            if (BookingReviewStatusTextBlock != null)
            {
                BookingReviewStatusTextBlock.Foreground = Brushes.LightGreen;
                BookingReviewStatusTextBlock.Text = message;
            }
        }


        // =====================================================
        // DECIMAL PARSER
        // =====================================================

        private static decimal ReadDecimal(
            JsonElement element)
        {
            if (element.ValueKind ==
                JsonValueKind.Number)
            {
                if (element.TryGetDecimal(
                        out decimal number))
                {
                    return number;
                }

                if (element.TryGetDouble(
                        out double doubleValue))
                {
                    return (decimal)doubleValue;
                }
            }

            if (element.ValueKind ==
                JsonValueKind.String)
            {
                string value =
                    element.GetString() ??
                    string.Empty;

                value =
                    value.Trim()
                        .Replace(
                            ",",
                            string.Empty
                        )
                        .Replace(
                            "٬",
                            string.Empty
                        )
                        .Replace(
                            "٫",
                            "."
                        );

                if (decimal.TryParse(
                        value,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out decimal parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }


        // =====================================================
        // CONVERT PERSIAN DIGITS
        // =====================================================

        private static string ConvertPersianDigitsToEnglish(
            string value)
        {
            return value
                .Replace("۰", "0")
                .Replace("۱", "1")
                .Replace("۲", "2")
                .Replace("۳", "3")
                .Replace("۴", "4")
                .Replace("۵", "5")
                .Replace("۶", "6")
                .Replace("۷", "7")
                .Replace("۸", "8")
                .Replace("۹", "9");
        }


        // =====================================================
        // PERSIAN NUMBER
        // =====================================================

        private static string ToPersianNumber(
            object value)
        {
            return value
                .ToString()!
                .Replace("0", "۰")
                .Replace("1", "۱")
                .Replace("2", "۲")
                .Replace("3", "۳")
                .Replace("4", "۴")
                .Replace("5", "۵")
                .Replace("6", "۶")
                .Replace("7", "۷")
                .Replace("8", "۸")
                .Replace("9", "۹");
        }


        private void ReservationsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.ShowReservations();
            }
        }

        // =====================================================
        // LIVE SYSTEM CARD CAPACITY STATUS
        // =====================================================

        private void UpdateLiveSystemCardStatuses()
        {
            foreach (KeyValuePair<int, TextBlock> item in _systemCardLiveStatusTextBlocks)
            {
                int variationId = item.Key;
                TextBlock statusText = item.Value;

                if (string.IsNullOrWhiteSpace(_selectedStartHour) ||
                    !TryGetHourNumber(_selectedStartHour, out int selectedHour))
                {
                    statusText.Text = "برای مشاهده ظرفیت، ابتدا یک ساعت انتخاب کن.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(184, 184, 199));
                    continue;
                }

                if (!_availability.TryGetValue(variationId, out AvailabilityResponse? response) ||
                    response?.Availability?.Hours == null)
                {
                    statusText.Text = "اطلاعات ظرفیت دریافت نشده است.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(240, 180, 90));
                    continue;
                }


                AvailabilityHour? hourInfo =
                    response.Availability.Hours.FirstOrDefault(
                        hour => hour.Hour == selectedHour);

                if (hourInfo == null)
                {
                    statusText.Text = "اطلاعات این ساعت موجود نیست.";
                    statusText.Foreground = new SolidColorBrush(Color.FromRgb(240, 180, 90));
                    continue;
                }

                int capacity = Math.Max(0, hourInfo.Capacity);
                int reserved = Math.Max(0, hourInfo.Reserved);
                int available = Math.Max(0, hourInfo.Available);

                string status = !hourInfo.Bookable
                    ? "غیرقابل رزرو"
                    : available <= 0
                        ? "تکمیل ظرفیت"
                        : "ظرفیت موجود";

                statusText.Text =
                    $"{status}\n" +
                    $"ظرفیت: {ToPersianNumber(capacity)} | " +
                    $"رزروشده: {ToPersianNumber(reserved)} | " +
                    $"باقی‌مانده: {ToPersianNumber(available)}";

                statusText.Foreground = new SolidColorBrush(
                    !hourInfo.Bookable || available <= 0
                        ? Color.FromRgb(255, 126, 126)
                        : Color.FromRgb(105, 214, 160));
            }
            UpdateSystemTileVisuals();
        }

        // =====================================================
        // LIVE CAPACITY SUMMARY
        // =====================================================

        private void UpdateLiveCapacitySummary()
        {
            LiveCapacitySummaryPanel.Children.Clear();

            if (string.IsNullOrWhiteSpace(_selectedStartHour) ||
                !TryGetHourNumber(_selectedStartHour, out int selectedHour))
            {
                LiveCapacityTotalsTextBlock.Text =
                    "برای مشاهده ظرفیت، ابتدا یک ساعت انتخاب کن.";
                return;
            }

            int totalCapacity = 0;
            int totalReserved = 0;
            int totalAvailable = 0;
            int loadedSystems = 0;

            foreach (int variationId in _variationNames.Keys.OrderBy(id => id))
            {
                if (!_availability.TryGetValue(
                        variationId,
                        out AvailabilityResponse? response) ||
                    response?.Availability?.Hours == null)
                {
                    continue;
                }


                AvailabilityHour? hourInfo =
                    response.Availability.Hours.FirstOrDefault(
                        hour => hour.Hour == selectedHour);


                if (hourInfo == null)
                    continue;

                int capacity = Math.Max(0, hourInfo.Capacity);
                int reserved = Math.Max(0, hourInfo.Reserved);
                int available = Math.Max(0, hourInfo.Available);

                totalCapacity += capacity;
                totalReserved += reserved;
                totalAvailable += available;
                loadedSystems++;

                bool unavailable = !hourInfo.Bookable || available <= 0;

                Border itemBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 49)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 7)
                };

                TextBlock itemText = new TextBlock
                {
                    Text =
                        $"{GetVariationName(variationId)} | " +
                        $"{(!hourInfo.Bookable ? "غیرقابل رزرو" : available <= 0 ? "تکمیل ظرفیت" : "ظرفیت موجود")}\n" +
                        $"ظرفیت: {ToPersianNumber(capacity)}   " +
                        $"رزروشده: {ToPersianNumber(reserved)}   " +
                        $"باقی‌مانده: {ToPersianNumber(available)}",
                    Foreground = new SolidColorBrush(
                        unavailable
                            ? Color.FromRgb(255, 126, 126)
                            : Color.FromRgb(105, 214, 160)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    FlowDirection = FlowDirection.RightToLeft
                };

                itemBorder.Child = itemText;
                LiveCapacitySummaryPanel.Children.Add(itemBorder);
            }

            if (loadedSystems == 0)
            {
                LiveCapacityTotalsTextBlock.Text =
                    "برای این ساعت اطلاعات ظرفیت دریافت نشده است.";
                return;
            }

            LiveCapacityTotalsTextBlock.Text =
                $"ساعت: {ToPersianNumber(selectedHour)} | " +
                $"سیستم‌های بررسی‌شده: {ToPersianNumber(loadedSystems)}\n" +
                $"مجموع ظرفیت: {ToPersianNumber(totalCapacity)} | " +
                $"رزروشده: {ToPersianNumber(totalReserved)} | " +
                $"باقی‌مانده: {ToPersianNumber(totalAvailable)}";
        }



    }
}
