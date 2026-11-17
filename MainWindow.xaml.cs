using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using LobixApp.Models;
using LobixApp.Views;
using LobixApp.Services;
using LobixApp.Utilities;
using LobixApp.Services.Sync;

namespace LobixApp
{
    public partial class MainWindow : Window
    {
        private GamenetInfo? _gamenet;
        private OperatorSession? _session;

        private DispatcherTimer? _offlineSyncTimer;
        private BookingOutboxSyncService? _bookingSyncService;

        private bool _isOfflineSyncRunning;

        private DashboardView? _dashboardView;
        private ManualBookingView? _manualBookingView;
        private ReservationsView? _reservationsView;
        private FloorPlanView? _floorPlanView;
        private OperationsHubView? _operationsView;

        private OnlineBookingNotificationService? _onlineBookingNotifications;

        // جلوگیری از ارسال چندباره تصمیم برای یک سفارش
        private readonly HashSet<int> _processingOrderDecisions = new();

        private readonly object _decisionLock = new();

        private bool _isSidebarCollapsed;
        private bool _isLoggedIn;

        private enum ShellPage
        {
            Dashboard,
            Reservations,
            ManualBooking,
            FloorPlan,
            Operations
        }

        private ShellPage _currentPage = ShellPage.Dashboard;

        private const double SidebarExpandedWidth = 224;
        private const double SidebarCollapsedWidth = 76;
        private const double SidebarAnimationDurationMs = 260;

        private readonly Brush _activeNavigationBackground =
            new SolidColorBrush(Color.FromRgb(43, 27, 34));

        private readonly Brush _activeNavigationBorder =
            new SolidColorBrush(Color.FromRgb(84, 48, 57));

        private readonly Brush _inactiveNavigationBackground =
            Brushes.Transparent;

        private readonly Brush _inactiveNavigationBorder =
            Brushes.Transparent;

        private readonly Brush _activeNavigationForeground =
            Brushes.White;

        private readonly Brush _inactiveNavigationForeground =
            new SolidColorBrush(Color.FromRgb(165, 167, 181));

        // ---------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------

        public MainWindow()
        {
            InitializeComponent();

            Closed += MainWindow_Closed;

            ShowLogin();
        }

        // ---------------------------------------------------------
        // Offline synchronization
        // ---------------------------------------------------------

        private void StartOfflineSync()
        {
            _offlineSyncTimer?.Stop();

            if (_session == null || _gamenet == null)
                return;

            var api = new LobixApiService();

            api.SetAuthentication(_session.Token);

            _bookingSyncService =
                new BookingOutboxSyncService(
                    api,
                    productId: _gamenet.Id);

            _offlineSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(20)
            };

            _offlineSyncTimer.Tick += OfflineSyncTimer_Tick;

            _offlineSyncTimer.Start();

            // اولین بررسی بدون انتظار
            _ = SyncOfflineBookingsAsync();
        }

        private async void OfflineSyncTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await SyncOfflineBookingsAsync();
        }

        private async Task SyncOfflineBookingsAsync()
        {
            if (_isOfflineSyncRunning ||
                !_isLoggedIn ||
                _bookingSyncService == null)
            {
                return;
            }

            _isOfflineSyncRunning = true;

            try
            {
                ConnectionStateService.Instance.SetState(
                    LobixConnectionState.Syncing);

                int synced =
                    await _bookingSyncService.SyncBookingsAsync();

                var outbox = new OutboxService();

                int pending =
                    await outbox.GetCountAsync("Pending", _gamenet?.Id ?? 0);

                pending +=
                    await outbox.GetCountAsync("Retry", _gamenet?.Id ?? 0);

                int conflicts =
                    await outbox.GetCountAsync("Conflict", _gamenet?.Id ?? 0);

                if (!_isLoggedIn)
                    return;

                if (conflicts > 0)
                {
                    SidebarApiStatusText.Text =
                        $"{conflicts} رزرو دارای تعارض";

                    SidebarApiStatusText.Foreground =
                        Brushes.IndianRed;
                }
                else if (
                    ConnectionStateService.Instance.State ==
                    LobixConnectionState.Offline)
                {
                    SidebarApiStatusText.Text =
                        pending > 0
                            ? $"آفلاین • {pending} رزرو در صف"
                            : "آفلاین";

                    SidebarApiStatusText.Foreground =
                        Brushes.DarkOrange;
                }
                else if (synced > 0)
                {
                    SidebarApiStatusText.Text =
                        $"{synced} رزرو همگام‌سازی شد";

                    SidebarApiStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(85, 214, 160));
                }
                else if (pending > 0)
                {
                    SidebarApiStatusText.Text =
                        $"{pending} رزرو در انتظار ارسال";

                    SidebarApiStatusText.Foreground =
                        Brushes.DarkOrange;
                }
                else
                {
                    SidebarApiStatusText.Text =
                        "متصل • همگام‌سازی‌شده";

                    SidebarApiStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(85, 214, 160));
                }

                SidebarApiStatusIndicator.Fill =
                    SidebarApiStatusText.Foreground;

                SidebarCollapsedStatusIndicator.Fill =
                    SidebarApiStatusText.Foreground;

                SidebarLastSyncText.Text =
                    "آخرین بررسی: " +
                    DateTime.Now.ToString("HH:mm:ss");

                if (synced > 0 &&
                    _currentPage == ShellPage.Reservations &&
                    _reservationsView != null)
                {
                    await _reservationsView.RefreshAsync();
                }

                if (synced > 0)
                {
                    SidebarApiStatusText.Text =
                        $"{synced} رزرو همگام‌سازی شد";

                    SidebarApiStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(85, 214, 160));

                    SidebarLastSyncText.Text =
                        "آخرین همگام‌سازی: " +
                        DateTime.Now.ToString("HH:mm:ss");

                    SidebarApiStatusIndicator.Fill =
                        SidebarApiStatusText.Foreground;

                    SidebarCollapsedStatusIndicator.Fill =
                        SidebarApiStatusText.Foreground;
                }
            }
            catch (Exception ex)
            {
                ConnectionStateService.Instance.SetState(
                    LobixConnectionState.SyncError);

                System.Diagnostics.Debug.WriteLine(
                    "Offline sync error: " + ex);
            }
            finally
            {
                _isOfflineSyncRunning = false;
            }
        }

        // ---------------------------------------------------------
        // Keyboard shortcuts
        // ---------------------------------------------------------

        private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    ShowDashboard();
                    e.Handled = true;
                    break;

                case Key.D2:
                case Key.NumPad2:
                    ShowManualBooking();
                    e.Handled = true;
                    break;

                case Key.D3:
                case Key.NumPad3:
                    ShowReservations();
                    e.Handled = true;
                    break;

                case Key.D4:
                case Key.NumPad4:
                    ShowFloorPlan();
                    e.Handled = true;
                    break;

                case Key.D5:
                case Key.NumPad5:
                    ShowOperations();
                    e.Handled = true;
                    break;
            }
        }

        // ---------------------------------------------------------
        // Button animation
        // ---------------------------------------------------------

        private void Window_PreviewMouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            DependencyObject? current =
                e.OriginalSource as DependencyObject;

            while (current != null &&
                   current is not ButtonBase)
            {
                current = current is Visual
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            if (current is not ButtonBase button ||
                !button.IsEnabled)
            {
                return;
            }

            button.RenderTransformOrigin =
                new Point(0.5, 0.5);

            if (button.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 1);
                button.RenderTransform = scale;
            }

            var animation = new DoubleAnimation(
                1,
                0.965,
                TimeSpan.FromMilliseconds(65))
            {
                AutoReverse = true,

                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                animation);

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                animation);
        }

        // ---------------------------------------------------------
        // Page transition
        // ---------------------------------------------------------

        private void AnimatePageTransition()
        {
            MainContent.BeginAnimation(
                UIElement.OpacityProperty,
                null);

            MainContent.Opacity = 0;

            var fade = new DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            MainContent.BeginAnimation(
                UIElement.OpacityProperty,
                fade);
        }

        // ---------------------------------------------------------
        // Login
        // ---------------------------------------------------------

        private void ShowLogin()
        {
            _isLoggedIn = false;

            SidebarColumn.BeginAnimation(
                ColumnDefinition.WidthProperty,
                null);

            SidebarColumn.Width = new GridLength(0);

            SidebarPanel.Visibility =
                Visibility.Collapsed;

            MainContent.Content = null;

            _reservationsView = null;

            var loginView = new LoginView();

            loginView.LoginSucceeded +=
                LoginView_LoginSucceeded;

            MainContent.Content = loginView;

            AnimatePageTransition();
        }

        // ---------------------------------------------------------
        // Login success
        // ---------------------------------------------------------

        private void LoginView_LoginSucceeded(
            GamenetInfo gamenet,
            OperatorSession session)
        {
            if (gamenet == null || session == null)
                return;

            // پاک‌سازی سرویس اعلان حساب قبلی
            if (_onlineBookingNotifications != null)
            {
                _onlineBookingNotifications.OrderNotificationClicked
                    -= OpenOnlineOrderDetails;

                _onlineBookingNotifications.Dispose();

                _onlineBookingNotifications = null;
            }

            // پاک‌سازی تایمر قبلی
            StopOfflineSync();

            _bookingSyncService = null;

            // ثبت حساب جدید
            _gamenet = gamenet;
            _session = session;

            _isLoggedIn = true;

            lock (_decisionLock)
            {
                _processingOrderDecisions.Clear();
            }

            // شروع همگام‌سازی آفلاین
            StartOfflineSync();

            // ساخت سرویس اعلان مخصوص همین گیمنت
            _onlineBookingNotifications =
                new OnlineBookingNotificationService(
                    gamenet,
                    session);

            _onlineBookingNotifications.OrderNotificationClicked
                += OpenOnlineOrderDetails;

            SidebarPanel.Visibility =
                Visibility.Visible;

            ApplySidebarState(animate: false);

            ShowDashboard();
        }

        // ---------------------------------------------------------
        // Stop offline synchronization
        // ---------------------------------------------------------

        private void StopOfflineSync()
        {
            if (_offlineSyncTimer != null)
            {
                _offlineSyncTimer.Stop();

                _offlineSyncTimer.Tick -=
                    OfflineSyncTimer_Tick;

                _offlineSyncTimer = null;
            }

            _bookingSyncService = null;
        }

        // ---------------------------------------------------------
        // Online order notification
        // ---------------------------------------------------------

        private void OpenOnlineOrderDetails(int orderId)
        {
            if (!_isLoggedIn ||
                _onlineBookingNotifications == null ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            if (!_onlineBookingNotifications.TryGetPendingOrder(
                orderId,
                out List<ReservationItem> items))
            {
                MessageBox.Show(
                    "اطلاعات این سفارش در صف اعلان‌ها پیدا نشد.",
                    "لابیکس",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (items.Count == 0)
                return;

            var window =
                new OnlineOrderDetailsWindow(orderId, items)
                {
                    Owner = this
                };

            window.DecisionRequested +=
                (requestedOrderId, decision) =>
                {
                    ProcessOnlineOrderDecision(
                        window,
                        requestedOrderId,
                        decision);
                };

            window.ShowDialog();
        }

        // ---------------------------------------------------------
        // Process online order decision
        // ---------------------------------------------------------

        private async void ProcessOnlineOrderDecision(
            OnlineOrderDetailsWindow window,
            int orderId,
            string decision)
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                window.SetDecisionResult(
                    false,
                    "نشست کاربری معتبر نیست.");

                return;
            }

            if (orderId <= 0)
            {
                window.SetDecisionResult(
                    false,
                    "شناسه سفارش معتبر نیست.");

                return;
            }

            if (decision != "confirmed" &&
                decision != "rejected")
            {
                window.SetDecisionResult(
                    false,
                    "تصمیم انتخاب‌شده معتبر نیست.");

                return;
            }

            // جلوگیری از ارسال چند درخواست برای یک سفارش
            lock (_decisionLock)
            {
                if (!_processingOrderDecisions.Add(orderId))
                    return;
            }

            // نگهداری اطلاعات حساب فعلی برای جلوگیری از
            // اعمال نتیجه درخواست روی حسابی که بعداً وارد شده است.
            GamenetInfo currentGamenet = _gamenet;
            OperatorSession currentSession = _session;

            OnlineBookingNotificationService? notificationService =
                _onlineBookingNotifications;

            window.SetProcessing(true);

            try
            {
                var apiService = new LobixApiService();

                apiService.SetAuthentication(
                    currentSession.Token);

                await apiService.SetBookingDecisionAsync(
                    currentGamenet.Id,
                    orderId,
                    decision);

                // فقط اگر هنوز همان حساب فعال است،
                // سفارش را از صف اعلان همان حساب حذف می‌کنیم.
                bool sameAccount =
                    _isLoggedIn &&
                    ReferenceEquals(_gamenet, currentGamenet) &&
                    ReferenceEquals(_session, currentSession);

                if (sameAccount)
                {
                    notificationService?.ResolveOrder(orderId);
                }

                string message = decision == "confirmed"
                    ? "سفارش با موفقیت تأیید شد."
                    : "سفارش با موفقیت رد شد.";

                window.SetDecisionResult(
                    true,
                    message);
            }
            catch (Exception ex)
            {
                string friendlyMessage =
                    UserFacingErrors.GetMessage(
                        ex,
                        "ثبت تصمیم سفارش ناموفق بود. دوباره تلاش کنید.");

                window.SetProcessing(false);

                window.SetDecisionResult(
                    false,
                    friendlyMessage);

                System.Diagnostics.Debug.WriteLine(
                    "Online order decision error: " + ex);
            }
            finally
            {
                lock (_decisionLock)
                {
                    _processingOrderDecisions.Remove(orderId);
                }
            }
        }

        // ---------------------------------------------------------
        // Dashboard
        // ---------------------------------------------------------

        public void ShowDashboard()
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            _currentPage = ShellPage.Dashboard;

            _dashboardView ??=
                new DashboardView(_gamenet, _session);

            _dashboardView.NavigationRequested -=
                DashboardView_NavigationRequested;

            _dashboardView.NavigationRequested +=
                DashboardView_NavigationRequested;

            MainContent.Content = _dashboardView;

            AnimatePageTransition();

            UpdateNavigationState();
        }

        private void DashboardView_NavigationRequested(
            string action)
        {
            switch (action)
            {
                case "booking":
                    ShowManualBooking();
                    break;

                case "reservations":
                case "refresh":
                    ShowReservations();
                    break;
            }
        }

        private void DashboardNavigationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowDashboard();
        }

        // ---------------------------------------------------------
        // Reservations
        // ---------------------------------------------------------

        public void ShowReservations()
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            _currentPage = ShellPage.Reservations;

            _reservationsView =
                new ReservationsView(_gamenet, _session);

            MainContent.Content = _reservationsView;

            AnimatePageTransition();

            UpdateNavigationState();
        }

        private void ReservationsNavigationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowReservations();
        }

        // ---------------------------------------------------------
        // Manual booking
        // ---------------------------------------------------------

        public void ShowManualBooking()
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            _currentPage = ShellPage.ManualBooking;

            if (_manualBookingView == null)
            {
                _manualBookingView =
                    new ManualBookingView(_gamenet, _session);

                _manualBookingView.ApiStatusChanged +=
                    ManualBookingView_ApiStatusChanged;
            }

            MainContent.Content = _manualBookingView;

            AnimatePageTransition();

            UpdateNavigationState();
        }

        private void ManualBookingView_ApiStatusChanged(
            string status,
            Brush color,
            string lastSync)
        {
            SidebarApiStatusText.Text = status;
            SidebarApiStatusText.Foreground = color;

            SidebarApiStatusIndicator.Fill = color;
            SidebarCollapsedStatusIndicator.Fill = color;

            SidebarLastSyncText.Text = lastSync;
        }

        private void ManualBookingNavigationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowManualBooking();
        }

        // ---------------------------------------------------------
        // Floor plan
        // ---------------------------------------------------------

        private object? _floorPlanGamenetContext;
        private object? _floorPlanSessionContext;

        public void ShowFloorPlan()
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            _currentPage = ShellPage.FloorPlan;

            try
            {
                bool contextChanged =
                    !ReferenceEquals(
                        _floorPlanGamenetContext,
                        _gamenet) ||
                    !ReferenceEquals(
                        _floorPlanSessionContext,
                        _session);

                if (_floorPlanView == null ||
                    contextChanged)
                {
                    _floorPlanView =
                        new FloorPlanView(_gamenet, _session);

                    _floorPlanGamenetContext = _gamenet;
                    _floorPlanSessionContext = _session;
                }

                MainContent.Content = _floorPlanView;

                AnimatePageTransition();

                UpdateNavigationState();
            }
            catch (Exception ex)
            {
                LobixDialog.Show(
                    "خطا در باز کردن نقشه سالن:\n" + ex.Message,
                    "خطای نقشه سالن",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void FloorPlanView_LayoutSaved(
            object? sender,
            EventArgs e)
        {
            // رویداد ذخیره موفق چیدمان سالن
        }

        private void FloorPlanNavigationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowFloorPlan();
        }

        // ---------------------------------------------------------
        // Operations
        // ---------------------------------------------------------

        public void ShowOperations()
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            _currentPage = ShellPage.Operations;

            _operationsView =
                new OperationsHubView(_gamenet, _session);

            MainContent.Content = _operationsView;

            AnimatePageTransition();

            UpdateNavigationState();
        }

        private void OperationsNavigationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowOperations();
        }

        // ---------------------------------------------------------
        // Logout
        // ---------------------------------------------------------

        private async void LogoutButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isLoggedIn || _session == null)
                return;

            var confirm = LobixDialog.Show(
                "از حساب اپراتور خارج می‌شوید؟",
                "خروج از حساب",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            var session = _session;

            try
            {
                var api = new LobixApiService();

                api.SetAuthentication(session.Token);

                await api.LogoutAsync();
            }
            catch (Exception ex)
            {
                UserFacingErrors.GetMessage(
                    ex,
                    "ارتباط با سرور هنگام خروج برقرار نشد.");
            }
            finally
            {
                StopOfflineSync();

                if (_onlineBookingNotifications != null)
                {
                    _onlineBookingNotifications.OrderNotificationClicked
                        -= OpenOnlineOrderDetails;

                    _onlineBookingNotifications.Dispose();

                    _onlineBookingNotifications = null;
                }

                await LobixApp.Services.Security
                    .LocalSessionService.Instance.ClearAsync();

                _session = null;
                _gamenet = null;

                _dashboardView = null;
                _manualBookingView = null;
                _reservationsView = null;
                _floorPlanView = null;
                _operationsView = null;

                _floorPlanGamenetContext = null;
                _floorPlanSessionContext = null;

                lock (_decisionLock)
                {
                    _processingOrderDecisions.Clear();
                }

                _isLoggedIn = false;

                ShowLogin();
            }
        }

        // ---------------------------------------------------------
        // Dashboard API status
        // ---------------------------------------------------------

        private void DashboardView_ApiStatusChanged(
            string status,
            Brush color,
            string lastSync)
        {
            SidebarApiStatusText.Text = status;
            SidebarApiStatusText.Foreground = color;

            SidebarLastSyncText.Text = lastSync;

            SidebarApiStatusIndicator.Fill = color;
            SidebarCollapsedStatusIndicator.Fill = color;
        }

        // ---------------------------------------------------------
        // Retry synchronization
        // ---------------------------------------------------------

        private async void SidebarRetrySyncButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isLoggedIn ||
                _gamenet == null ||
                _session == null)
            {
                return;
            }

            SidebarRetrySyncButton.IsEnabled = false;

            SidebarRetrySyncButton.Content =
                "در حال همگام‌سازی...";

            var syncingColor = new SolidColorBrush(
                Color.FromRgb(232, 185, 106));

            SidebarApiStatusText.Text =
                "در حال تلاش برای اتصال...";

            SidebarApiStatusText.Foreground =
                syncingColor;

            SidebarApiStatusIndicator.Fill =
                syncingColor;

            SidebarCollapsedStatusIndicator.Fill =
                syncingColor;

            try
            {
                await SyncOfflineBookingsAsync();

                if (_currentPage == ShellPage.Reservations &&
                    _reservationsView != null)
                {
                    await _reservationsView.RefreshAsync();

                    bool failed =
                        _reservationsView.SyncStatusDisplay.Contains(
                            "خطا",
                            StringComparison.OrdinalIgnoreCase);

                    Brush color = failed
                        ? Brushes.IndianRed
                        : new SolidColorBrush(
                            Color.FromRgb(85, 214, 160));

                    SidebarApiStatusText.Text =
                        failed ? "اتصال ناموفق" : "متصل";

                    SidebarApiStatusText.Foreground = color;

                    SidebarApiStatusIndicator.Fill = color;
                    SidebarCollapsedStatusIndicator.Fill = color;

                    SidebarLastSyncText.Text =
                        "آخرین وضعیت: " +
                        _reservationsView.SyncStatusDisplay;
                }
                else if (
                    _currentPage == ShellPage.ManualBooking &&
                    _manualBookingView != null)
                {
                    await _manualBookingView.RefreshDataAsync();
                }
                else if (_dashboardView != null)
                {
                    await _dashboardView.RefreshStatsAsync();

                    bool failed =
                        string.Equals(
                            _dashboardView.SyncStatusDisplay,
                            "عدم اتصال",
                            StringComparison.Ordinal);

                    Brush color = failed
                        ? Brushes.IndianRed
                        : new SolidColorBrush(
                            Color.FromRgb(85, 214, 160));

                    SidebarApiStatusText.Text =
                        failed ? "اتصال ناموفق" : "متصل";

                    SidebarApiStatusText.Foreground = color;

                    SidebarApiStatusIndicator.Fill = color;
                    SidebarCollapsedStatusIndicator.Fill = color;

                    SidebarLastSyncText.Text = failed
                        ? "همگام‌سازی موفقی ثبت نشده است"
                        : "آخرین همگام‌سازی: " +
                          _dashboardView.SyncStatusDisplay;
                }
            }
            catch (Exception ex)
            {
                string friendly =
                    UserFacingErrors.GetMessage(
                        ex,
                        "همگام‌سازی ناموفق بود. اتصال را بررسی کنید.");

                SidebarApiStatusText.Text =
                    "اتصال ناموفق";

                SidebarApiStatusText.Foreground =
                    Brushes.IndianRed;

                SidebarApiStatusIndicator.Fill =
                    Brushes.IndianRed;

                SidebarCollapsedStatusIndicator.Fill =
                    Brushes.IndianRed;

                LobixDialog.Show(
                    friendly,
                    "همگام‌سازی ناموفق بود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SidebarRetrySyncButton.IsEnabled = true;

                SidebarRetrySyncButton.Content =
                    "تلاش مجدد برای اتصال";
            }
        }

        // ---------------------------------------------------------
        // Window closed
        // ---------------------------------------------------------

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            StopOfflineSync();

            if (_onlineBookingNotifications != null)
            {
                _onlineBookingNotifications.OrderNotificationClicked
                    -= OpenOnlineOrderDetails;

                _onlineBookingNotifications.Dispose();

                _onlineBookingNotifications = null;
            }

            lock (_decisionLock)
            {
                _processingOrderDecisions.Clear();
            }
        }

        // ---------------------------------------------------------
        // Sidebar toggle
        // ---------------------------------------------------------

        private void SidebarToggleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!_isLoggedIn)
                return;

            _isSidebarCollapsed = !_isSidebarCollapsed;

            ApplySidebarState(animate: true);
        }

        private void ApplySidebarState(bool animate)
        {
            double targetWidth = _isSidebarCollapsed
                ? SidebarCollapsedWidth
                : SidebarExpandedWidth;

            SidebarColumn.BeginAnimation(
                ColumnDefinition.WidthProperty,
                null);

            if (animate &&
                SidebarPanel.Visibility == Visibility.Visible)
            {
                double currentWidth =
                    SidebarColumn.ActualWidth;

                if (currentWidth <= 0)
                {
                    currentWidth = _isSidebarCollapsed
                        ? SidebarExpandedWidth
                        : SidebarCollapsedWidth;
                }

                SidebarColumn.Width =
                    new GridLength(currentWidth);

                var animation = new GridLengthAnimation
                {
                    From = new GridLength(currentWidth),

                    To = new GridLength(targetWidth),

                    Duration = TimeSpan.FromMilliseconds(
                        SidebarAnimationDurationMs),

                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseInOut
                    },

                    FillBehavior = FillBehavior.Stop
                };

                animation.Completed += (_, _) =>
                {
                    SidebarColumn.BeginAnimation(
                        ColumnDefinition.WidthProperty,
                        null);

                    SidebarColumn.Width =
                        new GridLength(targetWidth);
                };

                SidebarColumn.BeginAnimation(
                    ColumnDefinition.WidthProperty,
                    animation,
                    HandoffBehavior.SnapshotAndReplace);
            }
            else
            {
                SidebarColumn.Width =
                    new GridLength(targetWidth);
            }

            BrandTextPanel.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            WorkspaceLabel.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            SidebarToggleText.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            DashboardNavigationText.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            ReservationsNavigationText.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            ManualBookingNavigationText.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            SidebarToggleIcon.Text =
                _isSidebarCollapsed ? "⇤" : "⇥";

            SidebarToggleButton.ToolTip =
                _isSidebarCollapsed
                    ? "باز کردن منو"
                    : "جمع کردن منو";

            SidebarStatusPanel.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            SidebarCollapsedStatusIndicator.Visibility =
                _isSidebarCollapsed
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SidebarFooter.Padding =
                _isSidebarCollapsed
                    ? new Thickness(0)
                    : new Thickness(10);

            SidebarFooter.Background =
                _isSidebarCollapsed
                    ? Brushes.Transparent
                    : new SolidColorBrush(
                        Color.FromRgb(23, 24, 32));

            SidebarFooter.BorderThickness =
                _isSidebarCollapsed
                    ? new Thickness(0)
                    : new Thickness(1);

            SidebarFooter.CornerRadius =
                _isSidebarCollapsed
                    ? new CornerRadius(0)
                    : new CornerRadius(13);

            SidebarFooter.HorizontalAlignment =
                HorizontalAlignment.Stretch;

            SidebarFooter.VerticalAlignment =
                VerticalAlignment.Bottom;

            UpdateNavigationState();
        }

        // ---------------------------------------------------------
        // Navigation state
        // ---------------------------------------------------------

        private void UpdateNavigationState()
        {
            SetNavigationButtonState(
                DashboardNavigationButton,
                _currentPage == ShellPage.Dashboard);

            SetNavigationButtonState(
                ReservationsNavigationButton,
                _currentPage == ShellPage.Reservations);

            SetNavigationButtonState(
                ManualBookingNavigationButton,
                _currentPage == ShellPage.ManualBooking);

            SetNavigationButtonState(
                FloorPlanNavigationButton,
                _currentPage == ShellPage.FloorPlan);

            SetNavigationButtonState(
                OperationsNavigationButton,
                _currentPage == ShellPage.Operations);
        }

        private void SetNavigationButtonState(
            Button button,
            bool isActive)
        {
            button.Background = isActive
                ? _activeNavigationBackground
                : _inactiveNavigationBackground;

            button.BorderBrush = isActive
                ? _activeNavigationBorder
                : _inactiveNavigationBorder;

            button.Foreground = isActive
                ? _activeNavigationForeground
                : _inactiveNavigationForeground;

            button.FontWeight = isActive
                ? FontWeights.Bold
                : FontWeights.Normal;
        }
    }

    // =============================================================
    // GridLength Animation
    // =============================================================

    public sealed class GridLengthAnimation : AnimationTimeline
    {
        public override Type TargetPropertyType =>
            typeof(GridLength);

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(
                nameof(From),
                typeof(GridLength),
                typeof(GridLengthAnimation),
                new PropertyMetadata(new GridLength(0)));

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(
                nameof(To),
                typeof(GridLength),
                typeof(GridLengthAnimation),
                new PropertyMetadata(new GridLength(0)));

        public IEasingFunction? EasingFunction { get; set; }

        public override object GetCurrentValue(
            object defaultOriginValue,
            object defaultDestinationValue,
            AnimationClock animationClock)
        {
            double from = From.Value;
            double to = To.Value;

            double progress =
                animationClock.CurrentProgress ?? 0;

            if (EasingFunction != null)
            {
                progress =
                    EasingFunction.Ease(progress);
            }

            double value =
                from + ((to - from) * progress);

            return new GridLength(
                Math.Max(0, value),
                GridUnitType.Pixel);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }
    }
}
