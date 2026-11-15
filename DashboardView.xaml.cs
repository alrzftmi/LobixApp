using LobixApp.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Globalization;
using LobixApp.Services;
using LobixApp.Utilities;

namespace LobixApp.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly GamenetInfo _gamenet;
        private readonly OperatorSession _session;
        public event Action<string>? NavigationRequested;
        public string SyncStatusDisplay => DashboardSyncText?.Text ?? "—";

        public DashboardView(GamenetInfo gamenet, OperatorSession session)
        {
            InitializeComponent();
            _gamenet = gamenet;
            _session = session;
            LoadGamenetInformation();
            Loaded += async (_, _) => await LoadDashboardStatsAsync();
        }

        private void LoadGamenetInformation()
        {
            GamenetNameTextBlock.Text = string.IsNullOrWhiteSpace(_gamenet.Name) ? "گیمنت لابیکس" : _gamenet.Name;
            OperatorNameTextBlock.Text = "اپراتور: " + (string.IsNullOrWhiteSpace(_session.DisplayName) ? _session.Username : _session.DisplayName);
            bool isActive = string.Equals(_gamenet.Status, "publish", StringComparison.OrdinalIgnoreCase);
            GamenetStatusTextBlock.Text = isActive ? "فعال" : (string.IsNullOrWhiteSpace(_gamenet.Status) ? "وضعیت نامشخص" : _gamenet.Status);
            GamenetStatusBorder.Background = isActive ? new SolidColorBrush(Color.FromRgb(23, 43, 37)) : new SolidColorBrush(Color.FromRgb(53, 29, 35));
            GamenetStatusBorder.BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(40, 83, 66)) : new SolidColorBrush(Color.FromRgb(89, 48, 57));
            GamenetStatusTextBlock.Foreground = isActive ? new SolidColorBrush(Color.FromRgb(85, 214, 160)) : new SolidColorBrush(Color.FromRgb(255, 133, 141));
        }

        public async Task RefreshStatsAsync()
        {
            await LoadDashboardStatsAsync();
        }

        private async Task LoadDashboardStatsAsync()
        {
            DashboardSyncText.Text = "در حال دریافت…";
            try
            {
                var api = new LobixApiService();
                api.SetAuthentication(_session.Token);
                DateTime today = DateTime.Today;
                string from = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string to = today.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var response = await api.SyncAsync(_gamenet.Id, from, to);
                var data = response?.Data;
                if (data == null) throw new InvalidOperationException("پاسخ همگام‌سازی خالی بود.");
                int systems = (data.Variations ?? new System.Collections.Generic.List<GamenetVariation>()).Sum(v =>
                    v.SystemNumbers != null && v.SystemNumbers.Count > 0 ? v.SystemNumbers.Count : Math.Max(0, v.Capacity));
                string todayKey = from;
                int manualToday = (data.ManualBookings ?? new System.Collections.Generic.List<ManualBooking>()).Count(b =>
                    string.Equals(b.Date, todayKey, StringComparison.Ordinal) || string.Equals(b.Date, todayKey + "T00:00:00", StringComparison.Ordinal));
                int onlineToday = (data.OnlineBookings ?? new System.Collections.Generic.List<OnlineBooking>()).Count(b =>
                    string.Equals(b.DateIso, todayKey, StringComparison.Ordinal));
                int manualCount = manualToday;
                int onlineCount = onlineToday;
                DateTime horizon = today.AddDays(7);
                int totalBookings = (data.ManualBookings ?? new System.Collections.Generic.List<ManualBooking>()).Count(b => TryBookingDate(b.Date, out var d) && d.Date >= today && d.Date < horizon)
                    + (data.OnlineBookings ?? new System.Collections.Generic.List<OnlineBooking>()).Count(b => TryBookingDate(b.DateIso, out var d) && d.Date >= today && d.Date < horizon);
                SystemsCountText.Text = systems.ToString("N0", CultureInfo.InvariantCulture);
                TodayBookingsText.Text = (manualToday + onlineToday).ToString("N0", CultureInfo.InvariantCulture);
                UpcomingBookingsText.Text = totalBookings.ToString("N0", CultureInfo.InvariantCulture);
                ManualTodayText.Text = manualCount.ToString("N0", CultureInfo.InvariantCulture);
                OnlineTodayText.Text = onlineCount.ToString("N0", CultureInfo.InvariantCulture);
                DashboardSyncText.Text = DateTime.Now.ToString("HH:mm");
                DashboardSyncText.Foreground = new SolidColorBrush(Color.FromRgb(143, 228, 184));
            }
            catch (Exception ex)
            {
                UserFacingErrors.Record(ex);
                SystemsCountText.Text = "—";
                TodayBookingsText.Text = "—";
                UpcomingBookingsText.Text = "—";
                ManualTodayText.Text = "—";
                OnlineTodayText.Text = "—";
                DashboardSyncText.Text = "عدم اتصال";
                DashboardSyncText.Foreground = new SolidColorBrush(Color.FromRgb(255, 133, 141));
            }
        }

        private static bool TryBookingDate(string? value, out DateTime date)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date);
        }

        private void NewBooking_Click(object sender, RoutedEventArgs e) => NavigationRequested?.Invoke("booking");
        private void Reservations_Click(object sender, RoutedEventArgs e) => NavigationRequested?.Invoke("reservations");
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadDashboardStatsAsync();
    }
}
