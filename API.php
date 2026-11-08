<?php
/**
 * Plugin Name: LOBIX Operator API
 * Description: API اختصاصی اپراتور ویندوز لابیکس؛ سازگار با رزروهای قدیمی و انتخاب دستگاه/بازی.
 * Version: 2.4.3
 * Author: LOBIX
 */

if (!defined('ABSPATH')) {
    exit;
}

if (!class_exists('LBXOP_API')) {
final class LBXOP_API {
    const NS                    = 'lobix/operator/v2';
    const VERSION               = '2.4.3';
    const TOKEN_SELECTOR_META   = '_lbxop_token_selector';
    const TOKEN_HASH_META       = '_lbxop_token_hash';
    const TOKEN_CREATED_META    = '_lbxop_token_created';
    const TOKEN_EXPIRES_META    = '_lbxop_token_expires';
    const TOKEN_TTL             = 2592000; // 30 days
    const LOGIN_LIMIT           = 8;
    const LOGIN_WINDOW          = 600;     // 10 minutes
    const LOCK_TTL              = 15;
    const IDEMPOTENCY_TTL       = 86400;   // 24 hours
    const MAX_SYNC_DAYS         = 31;
    const MANUAL_OPTION_PREFIX  = 'lobix_manual_bookings_';
    const DEVICE_SESSIONS_META = '_lbxop_device_sessions_v1';
    const ORDER_DECISION_META       = '_lobix_operator_decision';
    const ORDER_DECISION_AT_META    = '_lobix_operator_decision_at';
    const ORDER_DECISION_BY_META    = '_lobix_operator_decision_by';
    const ORDER_DECISION_NOTE_META  = '_lobix_operator_decision_note';
    
    public static function boot() {
        add_action('rest_api_init', array(__CLASS__, 'register_routes'));

        // When the Game Catalog & Systems Manager saves a product,
        // reconcile its variation settings into the physical-device registry.
        add_action(
            'lobix_gsm_product_configuration_saved',
            array(__CLASS__, 'on_product_configuration_saved'),
            10,
            2
        );
    }

    public static function on_product_configuration_saved($product_id, $configuration = array()) {
        $product_id = absint($product_id);
        if (!$product_id || get_post_type($product_id) !== 'product') {
            return;
        }
        self::provision_physical_systems_from_variations($product_id);
    }

    public static function register_routes() {
        $public = '__return_true';
        register_rest_route(self::NS, '/login', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'login'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/logout', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'logout'),
            'permission_callback' => $public,
        ));
                register_rest_route(self::NS, '/devices/sessions', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'device_sessions_route'),
            'permission_callback' => $public,
        ));

        register_rest_route(self::NS, '/devices/revoke', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'revoke_device_session_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/me', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'me'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/gamenets', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'gamenets'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/gamenet', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'gamenet'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/sync', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'sync'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/availability', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'availability_endpoint'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/systems', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'physical_systems_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/systems/save', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'save_physical_system_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/systems/deactivate', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'deactivate_physical_system_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/layout', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'layout_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/layout/save', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'save_layout_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/devices/availability', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'device_availability_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/public/booking-map', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'public_booking_map_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/reports/summary', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'report_summary_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/customers/search', array(
            'methods' => WP_REST_Server::READABLE,
            'callback' => array(__CLASS__, 'customer_search_route'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/booking/create', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'create_booking'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/booking/update', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'update_booking'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/booking/delete', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'delete_booking'),
            'permission_callback' => $public,
        ));
        register_rest_route(self::NS, '/booking/decision', array(
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => array(__CLASS__, 'booking_decision'),
            'permission_callback' => $public,
        ));
    }

    /* ---------------------------------------------------------------------
     * Common responses, inputs and date/time helpers
     * ------------------------------------------------------------------ */

    private static function ok($data = array(), $status = 200, $extra = array()) {
        // Compatibility: LobixApp reads token and response fields directly from the JSON root.
        // Keep the original API envelope shape: { success: true, token: ..., gamenets: ..., ... }
        if (!is_array($data)) {
            $data = array();
        }
        if (!is_array($extra)) {
            $extra = array();
        }
        $body = array_merge(array('success' => true), $data, $extra);
        return new WP_REST_Response($body, (int) $status);
    }

    private static function fail($code, $message, $status = 400, $extra = array()) {
        $error_data = array_merge(array('status' => (int) $status), $extra);
        return new WP_Error((string) $code, (string) $message, $error_data);
    }

    private static function now() {
        return current_datetime();
    }

    private static function input($request, $keys, $default = null) {
        if (!is_array($keys)) {
            $keys = array($keys);
        }
        foreach ($keys as $key) {
            $value = $request->get_param($key);
            if ($value !== null && $value !== '') {
                return $value;
            }
        }
        return $default;
    }

    private static function clean_date($value) {
        if (!is_string($value) && !is_numeric($value)) {
            return false;
        }
        $value = trim((string) $value);
        if (!preg_match('/^\d{4}-\d{2}-\d{2}$/', $value)) {
            return false;
        }
        $tz = wp_timezone();
        $date = DateTimeImmutable::createFromFormat('!Y-m-d', $value, $tz);
        $errors = DateTimeImmutable::getLastErrors();
        if (!$date || ($errors && (!empty($errors['warning_count']) || !empty($errors['error_count'])))) {
            return false;
        }
        return $date->format('Y-m-d') === $value ? $value : false;
    }

    private static function clean_time($value) {
        if (!is_string($value) && !is_numeric($value)) {
            return false;
        }
        $value = trim((string) $value);
        if (preg_match('/^(\d{1,2}):(\d{2})$/', $value, $m)) {
            $hour = (int) $m[1];
            $minute = (int) $m[2];
            if ($hour < 0 || $hour > 23 || $minute < 0 || $minute > 59) {
                return false;
            }
            return sprintf('%02d:%02d', $hour, $minute);
        }
        if (preg_match('/^\d{1,2}$/', $value)) {
            $hour = (int) $value;
            if ($hour < 0 || $hour > 23) {
                return false;
            }
            return sprintf('%02d:00', $hour);
        }
        return false;
    }

    private static function time_hour($value, $allow_24 = false) {
        if (!is_string($value) && !is_numeric($value)) {
            return false;
        }
        $value = trim((string) $value);
        if ($allow_24 && ($value === '24' || $value === '24:00')) {
            return 24;
        }
        if (preg_match('/^(\d{1,2})(?::(\d{2}))?$/', $value, $m)) {
            $hour = (int) $m[1];
            $minute = isset($m[2]) ? (int) $m[2] : 0;
            if ($minute !== 0 || $hour < 0 || $hour > 23) {
                return false;
            }
            return $hour;
        }
        return false;
    }

    private static function clean_day_code($value) {
        $value = strtolower(trim((string) $value));
        $allowed = array('sat', 'sun', 'mon', 'tue', 'wed', 'thu', 'fri');
        return in_array($value, $allowed, true) ? $value : false;
    }

    private static function day_code_from_date($date) {
        $date = self::clean_date($date);
        if (!$date) {
            return false;
        }
        $dt = new DateTimeImmutable($date, wp_timezone());
        return strtolower($dt->format('D'));
    }

    private static function valid_day_code($value) {
        return self::clean_day_code($value) !== false;
    }

    private static function normalize_hours($value) {
        if (is_array($value)) {
            $nums = array();
            foreach ($value as $item) {
                if (is_numeric($item)) {
                    $nums[] = (int) $item;
                } elseif (is_string($item) && preg_match('/^\d{1,2}:00$/', $item)) {
                    $nums[] = (int) substr($item, 0, strpos($item, ':'));
                }
            }
            $nums = array_values(array_unique($nums));
            sort($nums, SORT_NUMERIC);
            return $nums;
        }
        if (is_numeric($value)) {
            $n = (int) $value;
            return $n >= 1 && $n <= 24 ? $n : false;
        }
        if (is_string($value)) {
            $value = trim($value);
            if (preg_match('/^\d{1,2}$/', $value)) {
                $n = (int) $value;
                return $n >= 1 && $n <= 24 ? $n : false;
            }
            $decoded = json_decode($value, true);
            if (is_array($decoded)) {
                return self::normalize_hours($decoded);
            }
            if (strpos($value, ',') !== false) {
                return self::normalize_hours(array_map('trim', explode(',', $value)));
            }
        }
        return false;
    }

    private static function format_amount($value) {
        if ($value === null || $value === '') {
            return 0;
        }
        if (is_numeric($value)) {
            return max(0, (float) $value);
        }
        return 0;
    }

    private static function request_product_id($request) {
        return absint(self::input($request, array('product_id', 'gamenet_id', 'product', 'id'), 0));
    }

    private static function request_booking_id($request) {
        $value = self::input($request, array('booking_id', 'id', 'manual_booking_id'), '');
        return is_scalar($value) ? sanitize_text_field((string) $value) : '';
    }

    /* ---------------------------------------------------------------------
     * Operator authentication and token handling
     * ------------------------------------------------------------------ */

    private static function user_is_operator($user) {
        if (!$user instanceof WP_User) {
            return false;
        }
        if (user_can($user, 'manage_options')) {
            return true;
        }
        return in_array('lobix_gamenet_owner', (array) $user->roles, true);
    }

    private static function request_bearer_token($request) {
        $header = $request->get_header('authorization');
        if (!$header && isset($_SERVER['HTTP_AUTHORIZATION'])) {
            $header = wp_unslash($_SERVER['HTTP_AUTHORIZATION']);
        }
        if (!$header && isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) {
            $header = wp_unslash($_SERVER['REDIRECT_HTTP_AUTHORIZATION']);
        }
        if (!is_string($header) || !preg_match('/^Bearer\s+(.+)$/i', trim($header), $m)) {
            return false;
        }
        return trim($m[1]);
    }

       private static function authenticate($request) {
        $token = self::request_bearer_token($request);

        if (!$token || strpos($token, '.') === false) {
            return self::fail('unauthorized', 'توکن معتبر نیست یا ارسال نشده است.', 401);
        }

        $parts = explode('.', $token, 2);
        $selector = sanitize_text_field($parts[0]);
        $secret = (string) $parts[1];

        if ($selector === '' || $secret === '') {
            return self::fail('unauthorized', 'توکن معتبر نیست.', 401);
        }

        /*
         * New multi-device sessions.
         * Each selector has its own user-meta key.
         */
        $session_meta_key = '_lbxop_session_' . $selector;

        $users = get_users(array(
            'meta_key' => $session_meta_key,
            'number' => 2,
            'count_total' => false,
            'fields' => 'all',
        ));

        if (count($users) === 1 && $users[0] instanceof WP_User) {
            $user = $users[0];
            $session = get_user_meta($user->ID, $session_meta_key, true);

            if (
                !is_array($session) ||
                empty($session['token_hash']) ||
                empty($session['expires_at']) ||
                (int) $session['expires_at'] < time() ||
                !hash_equals(
                    (string) $session['token_hash'],
                    hash('sha256', $secret)
                )
            ) {
                delete_user_meta($user->ID, $session_meta_key);

                return self::fail(
                    'unauthorized',
                    'نشست این دستگاه معتبر نیست یا منقضی شده است.',
                    401
                );
            }

            if (!self::user_is_operator($user)) {
                return self::fail(
                    'forbidden',
                    'این حساب دسترسی اپراتور ندارد.',
                    403
                );
            }

            $session['last_seen'] = time();

            update_user_meta(
                $user->ID,
                $session_meta_key,
                $session
            );

            return $user;
        }

        /*
         * Backward compatibility:
         * Accept old single-device tokens issued by API 2.3.1.
         */
        $legacy_users = get_users(array(
            'meta_key' => self::TOKEN_SELECTOR_META,
            'meta_value' => $selector,
            'number' => 2,
            'count_total' => false,
            'fields' => 'all',
        ));

        if (
            count($legacy_users) !== 1 ||
            !$legacy_users[0] instanceof WP_User
        ) {
            return self::fail(
                'unauthorized',
                'توکن معتبر نیست یا منقضی شده است.',
                401
            );
        }

        $user = $legacy_users[0];

        if (!self::user_is_operator($user)) {
            return self::fail(
                'forbidden',
                'این حساب دسترسی اپراتور ندارد.',
                403
            );
        }

        $expires = (int) get_user_meta(
            $user->ID,
            self::TOKEN_EXPIRES_META,
            true
        );

        $stored_hash = (string) get_user_meta(
            $user->ID,
            self::TOKEN_HASH_META,
            true
        );

        if (
            $expires < time() ||
            $stored_hash === '' ||
            !hash_equals($stored_hash, hash('sha256', $secret))
        ) {
            self::clear_user_token($user->ID);

            return self::fail(
                'unauthorized',
                'توکن معتبر نیست یا منقضی شده است.',
                401
            );
        }

        return $user;
    }

    private static function require_operator($request) {
        $user = self::authenticate($request);
        return is_wp_error($user) ? $user : $user;
    }

       private static function issue_user_token($user_id, $device_name = '') {
        $selector = bin2hex(random_bytes(12));
        $secret = bin2hex(random_bytes(32));

        $created = time();
        $expires = $created + self::TOKEN_TTL;

        $device_name = sanitize_text_field((string) $device_name);

        if ($device_name === '') {
            $device_name = 'Windows Device';
        }

        if (function_exists('mb_substr')) {
            $device_name = mb_substr($device_name, 0, 80);
        } else {
            $device_name = substr($device_name, 0, 80);
        }

        $session = array(
            'selector' => $selector,
            'token_hash' => hash('sha256', $secret),
            'device_name' => $device_name,
            'created_at' => $created,
            'last_seen' => $created,
            'expires_at' => $expires,
        );

        $session_meta_key = '_lbxop_session_' . $selector;

        update_user_meta(
            $user_id,
            $session_meta_key,
            $session
        );

        $sessions = get_user_meta(
            $user_id,
            self::DEVICE_SESSIONS_META,
            true
        );

        if (!is_array($sessions)) {
            $sessions = array();
        }

        $sessions[$selector] = array(
            'selector' => $selector,
            'device_name' => $device_name,
            'created_at' => $created,
            'last_seen' => $created,
            'expires_at' => $expires,
        );

        update_user_meta(
            $user_id,
            self::DEVICE_SESSIONS_META,
            $sessions
        );

        return array(
            'token' => $selector . '.' . $secret,
            'token_type' => 'Bearer',
            'expires_at' => gmdate('c', $expires),
            'expires_in' => self::TOKEN_TTL,
            'device_id' => $selector,
            'device_name' => $device_name,
        );
    }

    private static function clear_user_token($user_id) {
        delete_user_meta($user_id, self::TOKEN_SELECTOR_META);
        delete_user_meta($user_id, self::TOKEN_HASH_META);
        delete_user_meta($user_id, self::TOKEN_CREATED_META);
        delete_user_meta($user_id, self::TOKEN_EXPIRES_META);
    }

    private static function user_payload($user) {
        return array(
            'id' => (int) $user->ID,
            'user_id' => (int) $user->ID,
            'user_login' => (string) $user->user_login,
            'username' => (string) $user->user_login,
            'display_name' => (string) $user->display_name,
            'name' => (string) $user->display_name,
            'email' => (string) $user->user_email,
            'roles' => array_values((array) $user->roles),
        );
    }

    private static function login_rate_key($username) {
        $ip = isset($_SERVER['REMOTE_ADDR']) ? sanitize_text_field(wp_unslash($_SERVER['REMOTE_ADDR'])) : 'unknown';
        return 'lbxop_login_' . md5(strtolower((string) $username) . '|' . $ip);
    }

       public static function login($request) {
        $username = sanitize_text_field(
            (string) self::input(
                $request,
                array('username', 'user_login', 'email'),
                ''
            )
        );

        $password = (string) self::input(
            $request,
            array('password'),
            ''
        );

        $device_name = sanitize_text_field(
            (string) self::input(
                $request,
                array('device_name'),
                'Windows Device'
            )
        );

        if ($username === '' || $password === '') {
            return self::fail(
                'missing_credentials',
                'نام کاربری و رمز عبور الزامی است.',
                400
            );
        }

        $rate_key = self::login_rate_key($username);
        $attempts = (int) get_transient($rate_key);

        if ($attempts >= self::LOGIN_LIMIT) {
            return self::fail(
                'login_rate_limited',
                'تعداد تلاش‌های ورود بیش از حد مجاز است. کمی بعد دوباره امتحان کنید.',
                429
            );
        }

        $user = wp_signon(array(
            'user_login' => $username,
            'user_password' => $password,
            'remember' => false,
        ), is_ssl());

        if (is_wp_error($user) || !$user instanceof WP_User) {
            set_transient(
                $rate_key,
                $attempts + 1,
                self::LOGIN_WINDOW
            );

            return self::fail(
                'invalid_credentials',
                'نام کاربری یا رمز عبور اشتباه است.',
                401
            );
        }

        if (!self::user_is_operator($user)) {
            wp_logout();

            return self::fail(
                'forbidden',
                'این حساب مجاز به ورود به پنل اپراتور نیست.',
                403
            );
        }

        delete_transient($rate_key);

        /*
         * Do NOT clear previous device sessions.
         * Every login receives an independent token.
         */
        $token = self::issue_user_token(
            $user->ID,
            $device_name
        );

        return self::ok(
            array_merge(
                $token,
                array('user' => self::user_payload($user))
            )
        );
    }

       public static function logout($request) {
        $user = self::require_operator($request);

        if (is_wp_error($user)) {
            return $user;
        }

        $token = self::request_bearer_token($request);

        if (!$token || strpos($token, '.') === false) {
            return self::fail(
                'invalid_token',
                'توکن دستگاه معتبر نیست.',
                401
            );
        }

        $parts = explode('.', $token, 2);
        $selector = sanitize_text_field($parts[0]);

        $session_meta_key = '_lbxop_session_' . $selector;

        $session = get_user_meta(
            $user->ID,
            $session_meta_key,
            true
        );

        if (is_array($session)) {
            delete_user_meta(
                $user->ID,
                $session_meta_key
            );

            $sessions = get_user_meta(
                $user->ID,
                self::DEVICE_SESSIONS_META,
                true
            );

            if (is_array($sessions)) {
                unset($sessions[$selector]);

                update_user_meta(
                    $user->ID,
                    self::DEVICE_SESSIONS_META,
                    $sessions
                );
            }

            return self::ok(array(
                'logged_out' => true,
                'device_id' => $selector,
            ));
        }

        /*
         * Legacy 2.3.1 token.
         */
        self::clear_user_token($user->ID);

        return self::ok(array(
            'logged_out' => true,
        ));
    }

    public static function me($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        return self::ok(array('user' => self::user_payload($user), 'api_version' => self::VERSION));
    }
    public static function device_sessions_route($request) {
        $user = self::require_operator($request);

        if (is_wp_error($user)) {
            return $user;
        }

        $sessions = get_user_meta(
            $user->ID,
            self::DEVICE_SESSIONS_META,
            true
        );

        if (!is_array($sessions)) {
            $sessions = array();
        }

        $devices = array();
        $now = time();
        $changed = false;

        foreach ($sessions as $selector => $item) {
            $selector = sanitize_text_field((string) $selector);

            if (
                $selector === '' ||
                !is_array($item)
            ) {
                unset($sessions[$selector]);
                $changed = true;
                continue;
            }

            $session_meta_key = '_lbxop_session_' . $selector;

            $session = get_user_meta(
                $user->ID,
                $session_meta_key,
                true
            );

            if (
                !is_array($session) ||
                empty($session['expires_at']) ||
                (int) $session['expires_at'] < $now
            ) {
                delete_user_meta(
                    $user->ID,
                    $session_meta_key
                );

                unset($sessions[$selector]);
                $changed = true;
                continue;
            }

            $devices[] = array(
                'device_id' => $selector,
                'device_name' => isset($session['device_name'])
                    ? (string) $session['device_name']
                    : 'Windows Device',
                'created_at' => isset($session['created_at'])
                    ? gmdate('c', (int) $session['created_at'])
                    : '',
                'last_seen' => isset($session['last_seen'])
                    ? gmdate('c', (int) $session['last_seen'])
                    : '',
                'expires_at' => gmdate(
                    'c',
                    (int) $session['expires_at']
                ),
            );
        }

        if ($changed) {
            update_user_meta(
                $user->ID,
                self::DEVICE_SESSIONS_META,
                $sessions
            );
        }

        return self::ok(array(
            'devices' => $devices,
            'count' => count($devices),
            'server_time' => self::now()->format('c'),
        ));
    }


    public static function revoke_device_session_route($request) {
        $user = self::require_operator($request);

        if (is_wp_error($user)) {
            return $user;
        }

        $device_id = sanitize_text_field(
            (string) self::input(
                $request,
                array('device_id', 'selector'),
                ''
            )
        );

        if (
            $device_id === '' ||
            !preg_match('/^[a-f0-9]{24}$/', $device_id)
        ) {
            return self::fail(
                'invalid_device_id',
                'شناسه دستگاه معتبر نیست.',
                400
            );
        }

        $sessions = get_user_meta(
            $user->ID,
            self::DEVICE_SESSIONS_META,
            true
        );

        if (
            !is_array($sessions) ||
            !isset($sessions[$device_id])
        ) {
            return self::fail(
                'device_not_found',
                'نشست این دستگاه پیدا نشد یا قبلاً خارج شده است.',
                404
            );
        }

        $session_meta_key = '_lbxop_session_' . $device_id;

        delete_user_meta(
            $user->ID,
            $session_meta_key
        );

        unset($sessions[$device_id]);

        update_user_meta(
            $user->ID,
            self::DEVICE_SESSIONS_META,
            $sessions
        );

        return self::ok(array(
            'revoked' => true,
            'device_id' => $device_id,
            'remaining_devices' => count($sessions),
        ));
    }
    /* ---------------------------------------------------------------------
     * Product ownership, product data and supported games/devices
     * ------------------------------------------------------------------ */

    private static function user_can_access_product($user, $product_id) {
        if (!$user instanceof WP_User || !$product_id) {
            return false;
        }
        if (user_can($user, 'manage_options')) {
            return true;
        }
        return (int) get_post_meta($product_id, '_lobix_gamenet_owner_user_id', true) === (int) $user->ID;
    }

    private static function owned_product_ids($user) {
        if (!$user instanceof WP_User) {
            return array();
        }
        $statuses = array('publish', 'draft', 'private', 'pending');
        if (user_can($user, 'manage_options')) {
            $ids = get_posts(array(
                'post_type' => 'product',
                'post_status' => $statuses,
                'numberposts' => -1,
                'fields' => 'ids',
                'orderby' => 'ID',
                'order' => 'ASC',
            ));
        } else {
            $ids = get_posts(array(
                'post_type' => 'product',
                'post_status' => $statuses,
                'numberposts' => -1,
                'fields' => 'ids',
                'meta_key' => '_lobix_gamenet_owner_user_id',
                'meta_value' => (string) $user->ID,
                'orderby' => 'ID',
                'order' => 'ASC',
            ));
        }
        return array_values(array_unique(array_map('absint', (array) $ids)));
    }

    private static function require_product_access($user, $product_id) {
        if (!$product_id) {
            return self::fail('missing_product_id', 'شناسه گیمنت ارسال نشده است.', 400);
        }
        if (!self::user_can_access_product($user, $product_id)) {
            return self::fail('product_forbidden', 'به این گیمنت دسترسی ندارید.', 403);
        }
        $product = wc_get_product($product_id);
        if (!$product) {
            return self::fail('product_not_found', 'گیمنت موردنظر پیدا نشد.', 404);
        }
        return $product;
    }

    private static function variation_system_numbers($variation_id) {
        $raw = get_post_meta($variation_id, '_lobix_system_numbers_v2', true);
        $numbers = array();

        if (is_array($raw)) {
            $numbers = $raw;
        } elseif (is_string($raw) && trim($raw) !== '') {
            $decoded = maybe_unserialize($raw);
            if (is_array($decoded)) {
                $numbers = $decoded;
            } else {
                $numbers = preg_split('/[,،;؛\r\n]+/u', trim($raw));
            }
        }

        $out = array();
        $seen = array();
        foreach ($numbers as $number) {
            if (!is_scalar($number)) {
                continue;
            }
            $number = sanitize_text_field(trim((string) $number));
            if ($number === '') {
                continue;
            }
            if (function_exists('mb_substr')) {
                $number = mb_substr($number, 0, 64, 'UTF-8');
            } else {
                $number = substr($number, 0, 64);
            }
            $key = function_exists('mb_strtolower') ? mb_strtolower($number, 'UTF-8') : strtolower($number);
            if (isset($seen[$key])) {
                continue;
            }
            $seen[$key] = true;
            $out[] = $number;
            if (count($out) >= 100) {
                break;
            }
        }

        if (!$out) {
            $count = absint(get_post_meta($variation_id, '_lobix_system_count_v2', true));
            if ($count > 0 && $count <= 100) {
                for ($i = 1; $i <= $count; $i++) {
                    $out[] = (string) $i;
                }
            }
        }

        return $out;
    }

    private static function allowed_games($product_id, $variation_id = 0) {
        $product_games = get_post_meta($product_id, '_lobix_product_games_v2', true);
        if (!is_array($product_games)) {
            $product_games = maybe_unserialize($product_games);
        }
        $product_games = is_array($product_games) ? array_values(array_unique(array_map('absint', $product_games))) : array();

        if ($variation_id) {
            $variation_games = get_post_meta($variation_id, '_lobix_variation_games_v2', true);
            if (!is_array($variation_games)) {
                $variation_games = maybe_unserialize($variation_games);
            }
            $variation_games = is_array($variation_games) ? array_values(array_unique(array_map('absint', $variation_games))) : array();
            // The variation-level allow-list is intersected with the product-level
            // allow-list. An empty variation allow-list must not silently broaden access.
            $ids = array_values(array_intersect($product_games, $variation_games));
        } else {
            $ids = $product_games;
        }

        $games = array();
        foreach ($ids as $id) {
            $post = get_post($id);
            if (!$post || $post->post_type !== 'lobix_game' || $post->post_status !== 'publish') {
                continue;
            }
            $image_id = get_post_thumbnail_id($id);
            $games[] = array(
                'id' => (int) $id,
                'name' => get_the_title($id),
                'image_url' => $image_id ? (string) wp_get_attachment_image_url($image_id, 'medium') : '',
            );
        }
        return $games;
    }

    private static function variation_data($product) {
        $out = array();
        if (!$product instanceof WC_Product) {
            return $out;
        }
        if ($product->is_type('variable')) {
            foreach ($product->get_children() as $variation_id) {
                $variation = wc_get_product($variation_id);
                if (!$variation instanceof WC_Product_Variation) {
                    continue;
                }
                $image_id = $variation->get_image_id();
                if (!$image_id) {
                    $image_id = $product->get_image_id();
                }
                $image_url = $image_id ? wp_get_attachment_image_url($image_id, 'medium') : '';
                $out[] = array(
                    'id' => (int) $variation_id,
                    'variation_id' => (int) $variation_id,
                    'title' => wc_get_formatted_variation($variation, true, false, false) ?: $variation->get_name(),
                    'name' => wc_get_formatted_variation($variation, true, false, false) ?: $variation->get_name(),
                    'system_numbers' => self::variation_system_numbers($variation_id),
                    'games' => self::allowed_games($product->get_id(), $variation_id),
                    'image_url' => $image_url ? (string) $image_url : '',
                    'sku' => (string) $variation->get_sku(),
                    'price' => (string) $variation->get_price(),
                    'regular_price' => (string) $variation->get_regular_price(),
                    'sale_price' => (string) $variation->get_sale_price(),
                    'stock_status' => (string) $variation->get_stock_status(),
                    'capacity' => max(0, (int) get_post_meta($variation_id, '_gamenet_var_capacity', true)),
                    'description' => (string) get_post_meta($variation_id, '_gamenet_var_description', true),
                    'attributes' => $variation->get_variation_attributes(),
                );
            }
            return $out;
        }

        $image_id = $product->get_image_id();
        $image_url = $image_id ? wp_get_attachment_image_url($image_id, 'medium') : '';
        $out[] = array(
            'id' => 0,
            'variation_id' => 0,
            'title' => $product->get_name(),
            'name' => $product->get_name(),
            'system_numbers' => self::variation_system_numbers($product->get_id()),
            'games' => self::allowed_games($product->get_id(), 0),
            'image_url' => $image_url ? (string) $image_url : '',
            'sku' => (string) $product->get_sku(),
            'price' => (string) $product->get_price(),
            'regular_price' => (string) $product->get_regular_price(),
            'sale_price' => (string) $product->get_sale_price(),
            'stock_status' => (string) $product->get_stock_status(),
            'capacity' => max(0, (int) get_post_meta($product->get_id(), '_gamenet_var_capacity', true)),
            'description' => (string) get_post_meta($product->get_id(), '_gamenet_var_description', true),
            'attributes' => array(),
        );
        return $out;
    }

    private static function gamenet_payload($product) {
        return array(
            'id' => (int) $product->get_id(),
            'product_id' => (int) $product->get_id(),
            'name' => (string) $product->get_name(),
            'slug' => (string) $product->get_slug(),
            'status' => (string) get_post_status($product->get_id()),
            'permalink' => (string) get_permalink($product->get_id()),
            'image_url' => (string) (wp_get_attachment_image_url($product->get_image_id(), 'medium') ?: ''),
            'booking_mode' => (string) get_post_meta($product->get_id(), '_gamenet_booking_mode', true),
            'work_start' => (string) get_post_meta($product->get_id(), '_gamenet_work_start', true),
            'work_end' => (string) get_post_meta($product->get_id(), '_gamenet_work_end', true),
            'variations' => self::variation_data($product),
            'physical_systems' => self::physical_systems_for_product($product->get_id(), true),
        );
    }

    public static function gamenets($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $items = array();
        foreach (self::owned_product_ids($user) as $product_id) {
            $product = wc_get_product($product_id);
            if (!$product) {
                continue;
            }
            $items[] = array(
                'id' => (int) $product_id,
                'product_id' => (int) $product_id,
                'name' => (string) $product->get_name(),
                'slug' => (string) $product->get_slug(),
                'status' => (string) get_post_status($product_id),
                'permalink' => (string) get_permalink($product_id),
                'image_url' => (string) (wp_get_attachment_image_url($product->get_image_id(), 'medium') ?: ''),
                'booking_mode' => (string) get_post_meta($product_id, '_gamenet_booking_mode', true),
            );
        }
        return self::ok(array('gamenets' => $items, 'items' => $items));
    }

    public static function gamenet($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }
        $data = self::gamenet_payload($product);
        return self::ok(array('gamenet' => $data, 'product' => $data, 'variations' => $data['variations']));
    }

    /* ---------------------------------------------------------------------
     * Physical devices and floor-plan API (v2.3)
     * ------------------------------------------------------------------ */

    private static function physical_systems_meta_key() {
        return '_lobix_physical_systems_v1';
    }

    private static function floor_layout_meta_key() {
        return '_lobix_floor_layout_v1';
    }

    /**
     * Derive the human-readable category from the variation attributes.
     * For example, a variation whose system type is VIP returns VIP.
     */
    private static function variation_system_category($variation, $labels = array()) {
        $prefixes = array();
        foreach ((array) $labels as $label) {
            $label = trim((string) $label);
            if (preg_match('/^([\p{L}][\p{L}\p{N}_ ]*)\s*[-_ ]\s*[۰-۹٠-٩0-9]+$/u', $label, $m)) {
                $prefix = sanitize_text_field(trim($m[1]));
                if ($prefix !== '') $prefixes[strtolower($prefix)] = $prefix;
            }
        }
        if (count($prefixes) === 1) return reset($prefixes);

        // Only use a variation attribute when its name clearly denotes device type/category.
        if ($variation instanceof WC_Product_Variation) {
            foreach ((array) $variation->get_variation_attributes() as $attribute_name => $attribute_value) {
                $attribute_value = trim((string) $attribute_value);
                if ($attribute_value === '') continue;
                $key = strtolower(rawurldecode((string) preg_replace('/^attribute_/', '', (string) $attribute_name)));
                if (strpos($key, 'type') === false && strpos($key, 'category') === false && strpos($key, 'system') === false && strpos($key, 'device') === false && strpos($key, 'دستگاه') === false && strpos($key, 'نوع') === false) continue;
                $taxonomy = preg_replace('/^attribute_/', '', (string) $attribute_name);
                if ($taxonomy && taxonomy_exists($taxonomy)) {
                    $term = get_term_by('slug', $attribute_value, $taxonomy);
                    if ($term && !is_wp_error($term) && !empty($term->name)) return sanitize_text_field((string) $term->name);
                }
                return sanitize_text_field(ucwords(str_replace(array('-', '_'), ' ', $attribute_value)));
            }
        }
        return 'سیستم';
    }

    private static function physical_system_label($system) {
        $category = isset($system['category']) ? trim((string) $system['category']) : '';
        $number = isset($system['number']) ? absint($system['number']) : 0;
        return ($category !== '' ? $category . '-' : '') . $number;
    }

    private static function system_numbers_match($a, $b) {
        $a = trim((string) $a);
        $b = trim((string) $b);
        if ($a === '' || $b === '') return false;
        if (strcasecmp($a, $b) === 0) return true;
        $a_number = self::numeric_system_number($a, 0);
        $b_number = self::numeric_system_number($b, 0);
        return $a_number > 0 && $b_number > 0 && $a_number === $b_number;
    }


    private static function numeric_system_number($label, $fallback) {
        $label = trim((string) $label);
        $digits = strtr($label, array(
            '۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4',
            '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9',
            '٠' => '0', '١' => '1', '٢' => '2', '٣' => '3', '٤' => '4',
            '٥' => '5', '٦' => '6', '٧' => '7', '٨' => '8', '٩' => '9',
        ));

        if (preg_match('/(\d+)\s*$/u', $digits, $match)) {
            $number = absint($match[1]);
            if ($number > 0 && $number <= 9999) {
                return $number;
            }
        }

        $fallback = absint($fallback);
        return ($fallback > 0 && $fallback <= 9999) ? $fallback : 1;
    }

    /**
     * Idempotently provision physical systems from WooCommerce variation meta.
     * Existing IDs, active flags, creation dates and extra row fields are kept.
     * Missing systems are added; old systems are never deleted automatically.
     */
    private static function provision_physical_systems_from_variations($product_id) {
        $product_id = absint($product_id);
        if (!$product_id || get_post_type($product_id) !== 'product' || !function_exists('wc_get_product')) {
            return false;
        }

        $product = wc_get_product($product_id);
        if (!$product) {
            return false;
        }

        // Atomic per-product option lock. This is separate from the booking lock,
        // so provisioning can safely run while a booking/system endpoint is active.
        $lock_key = '_lbxop_system_provision_lock_' . $product_id;
        if (!add_option($lock_key, time(), '', 'no')) {
            $lock_time = (int) get_option($lock_key, 0);
            if ($lock_time && (time() - $lock_time) > 60) {
                delete_option($lock_key);
                if (!add_option($lock_key, time(), '', 'no')) {
                    return false;
                }
            } else {
                return false;
            }
        }

        try {
            $stored = get_post_meta($product_id, self::physical_systems_meta_key(), true);
            if (!is_array($stored)) {
                $stored = array();
            }

            $systems = array_values($stored);
            $existing_index = array();
            foreach ($systems as $index => $row) {
                if (!is_array($row)) {
                    continue;
                }
                $variation_id = absint(isset($row['variation_id']) ? $row['variation_id'] : 0);
                $number = absint(isset($row['number']) ? $row['number'] : 0);
                if ($number > 0) {
                    $existing_index[$variation_id . ':' . $number] = $index;
                }
            }

            $expected = array();
            $variation_categories = array();
            $variation_ids = $product->is_type('variable')
                ? $product->get_children()
                : array($product_id);

            foreach ($variation_ids as $variation_id) {
                $variation_id = absint($variation_id);
                if (!$variation_id) {
                    continue;
                }

                if ($product->is_type('variable')) {
                    if (get_post_type($variation_id) !== 'product_variation') {
                        continue;
                    }
                    if ((int) wp_get_post_parent_id($variation_id) !== $product_id) {
                        continue;
                    }
                    $variation = wc_get_product($variation_id);
                    if (!$variation instanceof WC_Product_Variation) {
                        continue;
                    }
                    if (get_post_status($variation_id) !== 'publish') {
                        continue;
                    }
                    $category = '';
                } else {
                    $variation = $product;
                    $category = 'سیستم';
                }

                $labels = self::variation_system_numbers($variation_id);
                if (!$labels) {
                    continue;
                }
                if ($product->is_type('variable')) {
                    $category = self::variation_system_category($variation, $labels);
                }
                $variation_categories[$variation_id] = $category;

                foreach ($labels as $position => $label) {
                    $number = self::numeric_system_number($label, $position + 1);
                    $key = $variation_id . ':' . $number;
                    if (isset($expected[$key])) {
                        continue;
                    }
                    $expected[$key] = array(
                        'variation_id' => $product->is_type('variable') ? $variation_id : 0,
                        'number' => $number,
                        'category' => $category,
                    );
                }
            }

            if (!$expected) {
                return true;
            }

            $changed = false;
            $now = self::now()->format('c');

            // One-time repair for Robina (product 1233): the five generic
            // system-1 rows were auto-created before the real system lists
            // were configured. Keep CIP-1, deactivate the other four, and
            // remove only their matching placeholder objects from the floor plan.
            $migration_meta_key = '_lobix_robina_system1_cleanup_v1';
            $robina_migration_applied = false;
            if ($product_id === 1233 && !get_post_meta($product_id, $migration_meta_key, true)) {
                $placeholder_indexes = array();
                foreach ($systems as $index => $row) {
                    if (!is_array($row)) continue;
                    $row_variation = absint(isset($row['variation_id']) ? $row['variation_id'] : 0);
                    $row_number = absint(isset($row['number']) ? $row['number'] : 0);
                    $row_category = sanitize_text_field((string) (isset($row['category']) ? $row['category'] : ''));
                    $created_at = (string) (isset($row['created_at']) ? $row['created_at'] : '');
                    if (
                        $row_number === 1 &&
                        $row_category === 'سیستم' &&
                        strpos($created_at, '2026-11-09T00:42:05') === 0 &&
                        isset($variation_categories[$row_variation])
                    ) {
                        $placeholder_indexes[] = $index;
                    }
                }

                // Do not touch anything unless the exact five reported rows
                // are present and exactly one belongs to the CIP variation.
                $cip_placeholder_count = 0;
                foreach ($placeholder_indexes as $index) {
                    $row_variation = absint(isset($systems[$index]['variation_id']) ? $systems[$index]['variation_id'] : 0);
                    if (strcasecmp((string) $variation_categories[$row_variation], 'CIP') === 0) {
                        $cip_placeholder_count++;
                    }
                }

                if (count($placeholder_indexes) === 5 && $cip_placeholder_count === 1) {
                    $layout = self::get_floor_layout($product_id);
                    $remove_layout_ids = array();
                    foreach ($placeholder_indexes as $index) {
                        $row_variation = absint(isset($systems[$index]['variation_id']) ? $systems[$index]['variation_id'] : 0);
                        $system_id = sanitize_text_field((string) (isset($systems[$index]['system_id']) ? $systems[$index]['system_id'] : ''));
                        $is_cip = strcasecmp((string) $variation_categories[$row_variation], 'CIP') === 0;

                        if ($is_cip) {
                            // Preserve the real CIP-1 identity and its map position.
                            $systems[$index]['category'] = 'CIP';
                            $systems[$index]['active'] = true;
                            foreach ($layout['objects'] as &$object) {
                                if (is_array($object) && isset($object['system_id']) && (string) $object['system_id'] === $system_id) {
                                    $object['label'] = 'CIP #1';
                                }
                            }
                            unset($object);
                        } else {
                            // Keep the registry row/ID for historical references,
                            // but do not expose it as an available physical system.
                            $systems[$index]['active'] = false;
                            $systems[$index]['hidden_from_app'] = true;
                            $remove_layout_ids[$system_id] = true;
                        }

                        $systems[$index]['updated_at'] = $now;
                    }

                    if ($remove_layout_ids) {
                        $layout['objects'] = array_values(array_filter($layout['objects'], function ($object) use ($remove_layout_ids) {
                            return !is_array($object) ||
                                empty($object['system_id']) ||
                                !isset($remove_layout_ids[(string) $object['system_id']]);
                        }));
                    }
                    $layout['updated_at'] = $now;
                    update_post_meta($product_id, self::floor_layout_meta_key(), $layout);
                    $robina_migration_applied = true;
                    $changed = true;
                }
            }

            foreach ($expected as $key => $definition) {
                if (isset($existing_index[$key])) {
                    $index = $existing_index[$key];
                    if (!isset($systems[$index]) || !is_array($systems[$index])) {
                        continue;
                    }

                    $row = $systems[$index];
                    $row_changed = false;

                    // Do not replace system_id, created_at or active state.
                    if (empty($row['system_id'])) {
                        $row['system_id'] = 'sys_' . wp_generate_uuid4();
                        $row_changed = true;
                    }
                    if ((int) (isset($row['number']) ? $row['number'] : 0) !== (int) $definition['number']) {
                        $row['number'] = (int) $definition['number'];
                        $row_changed = true;
                    }
                    if (empty($row['category']) || sanitize_text_field((string) $row['category']) === 'سیستم') {
                        $row['category'] = $definition['category'];
                        $row_changed = true;
                    }
                    if (empty($row['variation_id']) && !empty($definition['variation_id'])) {
                        $row['variation_id'] = (int) $definition['variation_id'];
                        $row_changed = true;
                    }
                    if (empty($row['created_at'])) {
                        $row['created_at'] = $now;
                        $row_changed = true;
                    }
                    if ($row_changed) {
                        $row['updated_at'] = $now;
                        $systems[$index] = $row;
                        $changed = true;
                    }
                    continue;
                }

                // Backward-compatible adoption: an older registry row may not
                // have variation_id. Match category + number before creating.
                $adopted = false;
                foreach ($systems as $index => $row) {
                    if (!is_array($row)) {
                        continue;
                    }
                    $row_variation = absint(isset($row['variation_id']) ? $row['variation_id'] : 0);
                    $row_number = absint(isset($row['number']) ? $row['number'] : 0);
                    $row_category = sanitize_text_field((string) (isset($row['category']) ? $row['category'] : ''));
                    if (
                        $row_variation === 0 &&
                        $row_number === (int) $definition['number'] &&
                        $row_category !== '' &&
                        strcasecmp($row_category, $definition['category']) === 0
                    ) {
                        $systems[$index]['variation_id'] = (int) $definition['variation_id'];
                        if (empty($systems[$index]['system_id'])) {
                            $systems[$index]['system_id'] = 'sys_' . wp_generate_uuid4();
                        }
                        if (empty($systems[$index]['created_at'])) {
                            $systems[$index]['created_at'] = $now;
                        }
                        $systems[$index]['updated_at'] = $now;
                        $existing_index[$key] = $index;
                        $changed = true;
                        $adopted = true;
                        break;
                    }
                }

                if ($adopted) {
                    continue;
                }

                $systems[] = array(
                    'system_id' => 'sys_' . wp_generate_uuid4(),
                    'number' => (int) $definition['number'],
                    'category' => (string) $definition['category'],
                    'variation_id' => (int) $definition['variation_id'],
                    'active' => true,
                    'created_at' => $now,
                    'updated_at' => $now,
                );
                $existing_index[$key] = count($systems) - 1;
                $changed = true;
            }

            if (!$changed) {
                return true;
            }

            $saved = self::save_physical_systems_for_product($product_id, $systems);
            if ($saved && $robina_migration_applied) {
                update_post_meta($product_id, $migration_meta_key, $now);
            }
            return $saved;

        } finally {
            delete_option($lock_key);
        }
    }

    private static function physical_systems_for_product($product_id, $include_inactive = true, $include_hidden = false) {
        $product_id = absint($product_id);

        // Lazy, idempotent migration/provisioning means existing products such
        // as Nuke receive their registry on first API read without SQL or reset.
        self::provision_physical_systems_from_variations($product_id);

        $stored = get_post_meta($product_id, self::physical_systems_meta_key(), true);
        if (!is_array($stored)) {
            $stored = array();
        }

        $out = array();
        foreach ($stored as $row) {
            if (!is_array($row)) {
                continue;
            }
            if (!$include_hidden && !empty($row['hidden_from_app'])) {
                continue;
            }

            $system_id = sanitize_text_field((string) (isset($row['system_id']) ? $row['system_id'] : ''));
            $number = absint(isset($row['number']) ? $row['number'] : 0);
            $category = sanitize_text_field((string) (isset($row['category']) ? $row['category'] : ''));

            if ($system_id === '' || $number < 1 || $category === '') {
                continue;
            }

            $active = !array_key_exists('active', $row) || (bool) $row['active'];
            if (!$include_inactive && !$active) {
                continue;
            }

            $out[] = array(
                'system_id' => $system_id,
                'number' => $number,
                'label' => ($category !== '' ? $category . '-' : '') . $number,
                'category' => $category,
                'variation_id' => absint(isset($row['variation_id']) ? $row['variation_id'] : 0),
                'active' => $active,
                'created_at' => sanitize_text_field((string) (isset($row['created_at']) ? $row['created_at'] : '')),
                'updated_at' => sanitize_text_field((string) (isset($row['updated_at']) ? $row['updated_at'] : '')),
            );
        }

        usort($out, function ($a, $b) {
            if ((int) $a['variation_id'] !== (int) $b['variation_id']) {
                return (int) $a['variation_id'] <=> (int) $b['variation_id'];
            }
            return ((int) $a['number'] <=> (int) $b['number']) ?: strcmp($a['system_id'], $b['system_id']);
        });

        return $out;
    }

    private static function save_physical_systems_for_product($product_id, array $systems) {
        update_post_meta(absint($product_id), self::physical_systems_meta_key(), array_values($systems));
        $saved = get_post_meta(absint($product_id), self::physical_systems_meta_key(), true);
        return is_array($saved) && count($saved) === count($systems);
    }

    private static function find_physical_system($product_id, $system_id) {
        foreach (self::physical_systems_for_product($product_id, true, true) as $row) {
            if (hash_equals((string) $row['system_id'], (string) $system_id)) return $row;
        }
        return false;
    }

    private static function resolve_system_ids($product_id, $systems) {
        if (!is_array($systems)) return $systems;
        $registry = self::physical_systems_for_product($product_id, true, true);
        $by_id = array();
        foreach ($registry as $device) $by_id[(string) $device['system_id']] = $device;

        foreach ($systems as &$item) {
            if (!is_array($item) || empty($item['system_id'])) continue;
            $id = sanitize_text_field((string) $item['system_id']);
            if (!isset($by_id[$id])) return self::fail('system_not_found', 'شناسه دستگاه در این گیمنت پیدا نشد.', 422, array('system_id' => $id));
            $device = $by_id[$id];
            if (empty($device['active'])) return self::fail('system_inactive', 'این دستگاه غیرفعال است و قابل رزرو نیست.', 409, array('system_id' => $id));
            $selected_variation = absint(isset($item['variation_id']) ? $item['variation_id'] : (isset($item['var_id']) ? $item['var_id'] : (isset($item['id']) ? $item['id'] : 0)));
            $device_variation = absint(isset($device['variation_id']) ? $device['variation_id'] : 0);
            if ($device_variation && $selected_variation && $device_variation !== $selected_variation) {
                return self::fail('system_variation_mismatch', 'این دستگاه متعلق به نوع سرویس انتخاب‌شده نیست.', 422, array('system_id' => $id, 'variation_id' => $selected_variation));
            }
            if ($device_variation) $item['variation_id'] = $device_variation;
            // Keep a readable, variation-specific label in reservation rows for legacy compatibility.
            $item['system_number'] = self::physical_system_label($device);
            $item['quantity'] = 1;
        }
        unset($item);
        return $systems;
    }


    private static function get_floor_layout($product_id) {
        $layout = get_post_meta(absint($product_id), self::floor_layout_meta_key(), true);
        if (!is_array($layout)) $layout = array();
        if (!isset($layout['version'])) $layout['version'] = 1;
        if (!isset($layout['width'])) $layout['width'] = 1000;
        if (!isset($layout['height'])) $layout['height'] = 700;
        if (!isset($layout['objects']) || !is_array($layout['objects'])) $layout['objects'] = array();
        return $layout;
    }

    public static function physical_systems_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        return self::ok(array('api_version' => self::VERSION, 'product_id' => (int) $product_id, 'physical_systems' => self::physical_systems_for_product($product_id, true)));
    }

    public static function save_physical_system_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $number = absint($request->get_param('number'));
        $category = sanitize_text_field((string) $request->get_param('category'));
        $system_id = sanitize_text_field((string) $request->get_param('system_id'));
        $variation_id = absint($request->get_param('variation_id'));
        if ($variation_id && !array_key_exists($variation_id, self::variation_map($product))) return self::fail('invalid_variation', 'نوع سرویس انتخاب‌شده متعلق به این گیمنت نیست.', 422, array('variation_id' => $variation_id));
        if ($number < 1 || $number > 9999) return self::fail('invalid_system_number', 'شماره دستگاه باید بین ۱ تا ۹۹۹۹ باشد.', 400);
        if ($category === '' || (function_exists('mb_strlen') ? mb_strlen($category) : strlen($category)) > 40) return self::fail('invalid_system_category', 'نوع دستگاه الزامی است و حداکثر ۴۰ نویسه دارد.', 400);
        $lock = self::lock_product($product_id);
        if (is_wp_error($lock)) return $lock;
        try {
            $systems = self::physical_systems_for_product($product_id, true, true);
            $found = false;
            foreach ($systems as $row) {
                if ($system_id !== '' && hash_equals($row['system_id'], $system_id)) { $found = true; continue; }
                $same_variation = $variation_id ? ((int) $row['variation_id'] === $variation_id) : ((int) $row['variation_id'] === 0 && strcasecmp((string) $row['category'], $category) === 0);
                if ($same_variation && (int) $row['number'] === $number) return self::fail('duplicate_system_number', 'این شماره قبلاً در همین دسته/سرویس ثبت شده است؛ شماره دستگاه غیرفعال نیز دوباره استفاده نمی‌شود.', 409, array('number' => $number, 'variation_id' => $variation_id));
            }
            if ($system_id !== '' && !$found) return self::fail('system_not_found', 'شناسه دستگاه در این گیمنت پیدا نشد.', 404);
            $now = self::now()->format('c');
            if ($found) {
                foreach ($systems as &$row) if (hash_equals($row['system_id'], $system_id)) { $row['number'] = $number; $row['category'] = $category; if ($variation_id) $row['variation_id'] = $variation_id; $row['updated_at'] = $now; break; }
                unset($row);
            } else {
                $systems[] = array('system_id' => 'sys_' . wp_generate_uuid4(), 'number' => $number, 'category' => $category, 'variation_id' => $variation_id, 'active' => true, 'created_at' => $now, 'updated_at' => $now);
            }
            if (!self::save_physical_systems_for_product($product_id, $systems)) return self::fail('physical_system_storage_error', 'ذخیره دستگاه‌ها تأیید نشد.', 500);
            return self::ok(array('api_version' => self::VERSION, 'product_id' => (int) $product_id, 'physical_systems' => self::physical_systems_for_product($product_id, true)));
        } finally { self::unlock_product($lock); }
    }

    public static function deactivate_physical_system_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $system_id = sanitize_text_field((string) $request->get_param('system_id'));
        if ($system_id === '') return self::fail('missing_system_id', 'شناسه دستگاه ارسال نشده است.', 400);
        $lock = self::lock_product($product_id);
        if (is_wp_error($lock)) return $lock;
        try {
            $systems = self::physical_systems_for_product($product_id, true, true); $found = false;
            foreach ($systems as &$row) if (hash_equals($row['system_id'], $system_id)) { $row['active'] = false; $row['updated_at'] = self::now()->format('c'); $found = true; break; }
            unset($row);
            if (!$found) return self::fail('system_not_found', 'شناسه دستگاه در این گیمنت پیدا نشد.', 404);
            if (!self::save_physical_systems_for_product($product_id, $systems)) return self::fail('physical_system_storage_error', 'غیرفعال‌سازی دستگاه ذخیره نشد.', 500);
            return self::ok(array('product_id' => (int) $product_id, 'physical_systems' => self::physical_systems_for_product($product_id, true)));
        } finally { self::unlock_product($lock); }
    }

    public static function layout_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $systems = self::physical_systems_for_product($product_id, true);
        return self::ok(array('api_version' => self::VERSION, 'product_id' => (int) $product_id, 'layout' => self::get_floor_layout($product_id), 'physical_systems' => $systems));
    }

    public static function save_layout_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $layout = $request->get_param('layout');
        if (is_string($layout)) $layout = json_decode($layout, true);
        if (!is_array($layout)) return self::fail('invalid_layout', 'محتوای نقشه معتبر نیست.', 422);
        $width = isset($layout['width']) ? absint($layout['width']) : 1000;
        $height = isset($layout['height']) ? absint($layout['height']) : 700;
        $objects = isset($layout['objects']) && is_array($layout['objects']) ? $layout['objects'] : array();
        if ($width < 200 || $width > 10000 || $height < 200 || $height > 10000 || count($objects) > 500) return self::fail('invalid_layout_dimensions', 'ابعاد نقشه یا تعداد اجزای آن خارج از محدوده مجاز است.', 422);
        $clean_objects = array();
        foreach ($objects as $object) {
            if (!is_array($object)) return self::fail('invalid_layout_object', 'یکی از اجزای نقشه معتبر نیست.', 422);
            $id = sanitize_text_field((string) (isset($object['id']) ? $object['id'] : ''));
            $type = sanitize_key((string) (isset($object['type']) ? $object['type'] : 'object'));
            $x = isset($object['x']) && is_numeric($object['x']) ? (float) $object['x'] : -1;
            $y = isset($object['y']) && is_numeric($object['y']) ? (float) $object['y'] : -1;
            $w = isset($object['width']) && is_numeric($object['width']) ? (float) $object['width'] : 0;
            $h = isset($object['height']) && is_numeric($object['height']) ? (float) $object['height'] : 0;
            if ($id === '' || $x < 0 || $y < 0 || $x > $width || $y > $height || $w < 0 || $h < 0 || $w > $width || $h > $height) return self::fail('invalid_layout_object', 'مختصات یا ابعاد یکی از اجزای نقشه معتبر نیست.', 422);
            $clean = array('id' => $id, 'type' => $type, 'x' => $x, 'y' => $y, 'width' => $w, 'height' => $h, 'rotation' => isset($object['rotation']) && is_numeric($object['rotation']) ? (float) $object['rotation'] : 0, 'label' => sanitize_text_field((string) (isset($object['label']) ? $object['label'] : '')));
            if (!empty($object['room_id'])) $clean['room_id'] = sanitize_text_field((string) $object['room_id']);
            if (!empty($object['system_id'])) {
                $sid = sanitize_text_field((string) $object['system_id']);
                if (!self::find_physical_system($product_id, $sid)) return self::fail('layout_system_not_found', 'یکی از دستگاه‌های نقشه در فهرست دستگاه‌ها وجود ندارد.', 422, array('system_id' => $sid));
                $clean['system_id'] = $sid;
            }
            $clean_objects[] = $clean;
        }
        $normalized = array('version' => 1, 'width' => $width, 'height' => $height, 'objects' => $clean_objects, 'updated_at' => self::now()->format('c'));
        update_post_meta($product_id, self::floor_layout_meta_key(), $normalized);
        $saved = self::get_floor_layout($product_id);
        if (count($saved['objects']) !== count($clean_objects)) return self::fail('layout_save_failed', 'ذخیره نقشه تأیید نشد.', 500);
        return self::ok(array('product_id' => (int) $product_id, 'layout' => $saved));
    }

    public static function device_availability_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $date = self::clean_date((string) self::input($request, array('date_iso', 'date'), ''));
        $start = self::clean_time((string) self::input($request, array('start_time', 'time'), ''));
        $hours = absint(self::input($request, 'hours', 1));
        if (!$date || $start === false || $hours < 1 || $hours > 24) return self::fail('invalid_time_range', 'تاریخ، ساعت شروع و مدت معتبر الزامی است.', 422);
        $start_hour = (int) substr($start, 0, 2); $end_hour = $start_hour + $hours;
        $manuals = self::get_manuals_authoritative($product_id);
        if (is_wp_error($manuals)) return $manuals;
        $online = self::online_bookings($product_id, $date, $date);
        $devices = self::physical_systems_for_product($product_id, true);
        $now = self::now(); $now_date = $now->format('Y-m-d'); $now_hour = ((int) $now->format('H')) + ((int) $now->format('i') / 60);
        $out = array();
        foreach ($devices as $device) {
            $status = empty($device['active']) ? 'off' : 'free'; $conflict = null;
            $conflict_start_hour = null; $conflict_hours = 0;
            if ($status !== 'off') {
                foreach ($manuals as $row) {
                    if (!is_array($row) || !self::row_date_matches($row, $date)) continue;
                    $row_number = isset($row['system_number']) ? trim((string) $row['system_number']) : '';
                    $row_system_id = isset($row['system_id']) ? trim((string) $row['system_id']) : '';
                    $row_hour = self::row_hour($row);
                    if ($row_hour === false || $row_hour < $start_hour || $row_hour >= $end_hour) continue;
                    $row_var = isset($row['var_id']) ? absint($row['var_id']) : (isset($row['variation_id']) ? absint($row['variation_id']) : 0);
                    $device_label = isset($device['label']) ? (string) $device['label'] : self::physical_system_label($device);
                    $same_variation = $row_var === (int) $device['variation_id'];
                    if (($same_variation && ($row_number === '' || self::system_numbers_match($row_number, $device_label))) || ($row_system_id !== '' && $row_system_id === $device['system_id'])) {
                        $conflict_start_hour = $row_hour; $conflict_hours = 1;
                        $conflict = array('source' => 'manual', 'booking_id' => isset($row['id']) ? (string) $row['id'] : '', 'variation_id' => $row_var, 'hour' => $row_hour);
                        break;
                    }
                }
                if (!$conflict) foreach ($online as $booking) {
                    if (!self::online_booking_occupies_capacity($booking)) continue;
                    $row_number = isset($booking['system_number']) ? trim((string) $booking['system_number']) : '';
                    $row_start = self::time_hour($booking['start_time']);
                    if ($row_start === false || !self::times_overlap($start_hour, $hours, $row_start, $booking['hours'])) continue;
                    $device_label = isset($device['label']) ? (string) $device['label'] : self::physical_system_label($device);
                    $booking_variation = isset($booking['variation_id']) ? (int) $booking['variation_id'] : 0;
                    if ($booking_variation === (int) $device['variation_id'] && ($row_number === '' || self::system_numbers_match($row_number, $device_label))) {
                        $conflict_start_hour = $row_start; $conflict_hours = max(1, (int) $booking['hours']);
                        $conflict = array('source' => 'online', 'order_id' => (int) $booking['order_id'], 'variation_id' => (int) $booking['variation_id'], 'start_time' => $booking['start_time']);
                        break;
                    }
                }
                if ($conflict) {
                    $status = ($date === $now_date && $conflict_start_hour !== null && $conflict_start_hour <= $now_hour && ($conflict_start_hour + max(1, $conflict_hours)) > $now_hour) ? 'in_use' : 'reserved';
                }
            }
            $out[] = array_merge($device, array('status' => $status, 'conflict' => $conflict));
        }
        return self::ok(array('product_id' => (int) $product_id, 'date' => $date, 'start_time' => $start, 'hours' => $hours, 'devices' => $out, 'server_time' => self::now()->format('c')));
    }
public static function public_booking_map_route($request) {

    // فقط محصول منتشرشده و قابل مشاهده در فروشگاه مجاز است.
    $product_id = absint($request->get_param('product_id'));

    if (!$product_id || get_post_type($product_id) !== 'product') {
        return self::fail(
            'invalid_product',
            'گیمنت موردنظر معتبر نیست.',
            404
        );
    }

    $product = wc_get_product($product_id);

    if (
        !$product ||
        get_post_status($product_id) !== 'publish' ||
        !$product->is_visible()
    ) {
        return self::fail(
            'product_not_public',
            'این گیمنت در دسترس نیست.',
            404
        );
    }

    // دریافت و اعتبارسنجی تاریخ، ساعت و مدت رزرو
    $date = self::clean_date(
        (string) self::input($request, array('date_iso', 'date'), '')
    );

    $start = self::clean_time(
        (string) self::input($request, array('start_time', 'time'), '')
    );

    $hours = absint(self::input($request, 'hours', 1));

    if (!$date || $start === false || $hours < 1 || $hours > 24) {
        return self::fail(
            'invalid_time_range',
            'تاریخ، ساعت شروع و مدت رزرو معتبر نیست.',
            422
        );
    }

    $start_hour = (int) substr($start, 0, 2);
    $end_hour = $start_hour + $hours;

    // دریافت رزروهای واقعی؛ این اطلاعات فقط در سمت سرور استفاده می‌شوند.
    $manuals = self::get_manuals_authoritative($product_id);

    if (is_wp_error($manuals)) {
        return $manuals;
    }

    $online = self::online_bookings($product_id, $date, $date);

    // دریافت نقشه و دستگاه‌های همین گیمنت
    $layout = self::get_floor_layout($product_id);
    $devices = self::physical_systems_for_product($product_id, true);

    // فقط فیلدهای لازم برای نمایش نقشه را نگه می‌داریم.
    $safe_objects = array();

    foreach ($layout['objects'] as $object) {

        if (!is_array($object)) {
            continue;
        }

        $safe_object = array(
            'id'       => isset($object['id']) ? sanitize_text_field((string) $object['id']) : '',
            'type'     => isset($object['type']) ? sanitize_key((string) $object['type']) : 'object',
            'x'        => isset($object['x']) ? (float) $object['x'] : 0,
            'y'        => isset($object['y']) ? (float) $object['y'] : 0,
            'width'    => isset($object['width']) ? (float) $object['width'] : 0,
            'height'   => isset($object['height']) ? (float) $object['height'] : 0,
            'rotation' => isset($object['rotation']) ? (float) $object['rotation'] : 0,
            'label'    => isset($object['label']) ? sanitize_text_field((string) $object['label']) : '',
        );

        if (!empty($object['room_id'])) {
            $safe_object['room_id'] = sanitize_text_field((string) $object['room_id']);
        }

        if (!empty($object['system_id'])) {
            $safe_object['system_id'] = sanitize_text_field((string) $object['system_id']);
        }

        $safe_objects[] = $safe_object;
    }

    // محاسبه وضعیت دستگاه‌ها بدون خروج اطلاعات رزرو
    $safe_devices = array();

    foreach ($devices as $device) {

        $status = empty($device['active']) ? 'off' : 'free';

        if ($status !== 'off') {

            // بررسی رزروهای دستی
            foreach ($manuals as $row) {

                if (!is_array($row) || !self::row_date_matches($row, $date)) {
                    continue;
                }

                $row_hour = self::row_hour($row);

                if (
                    $row_hour === false ||
                    $row_hour < $start_hour ||
                    $row_hour >= $end_hour
                ) {
                    continue;
                }

                $row_number = isset($row['system_number'])
                    ? trim((string) $row['system_number'])
                    : '';

                $row_system_id = isset($row['system_id'])
                    ? trim((string) $row['system_id'])
                    : '';

                $row_variation = isset($row['var_id'])
                    ? absint($row['var_id'])
                    : (isset($row['variation_id']) ? absint($row['variation_id']) : 0);

                $device_label = isset($device['label'])
                    ? (string) $device['label']
                    : self::physical_system_label($device);

                $same_variation = $row_variation === (int) $device['variation_id'];

                $same_device =
                    (
                        $same_variation &&
                        (
                            $row_number === '' ||
                            self::system_numbers_match($row_number, $device_label)
                        )
                    ) ||
                    (
                        $row_system_id !== '' &&
                        $row_system_id === $device['system_id']
                    );

                if ($same_device) {
                    $status = 'reserved';
                    break;
                }
            }

            // بررسی رزروهای آنلاین ووکامرس
            if ($status === 'free') {

                foreach ($online as $booking) {

                    if (!self::online_booking_occupies_capacity($booking)) {
                        continue;
                    }

                    $row_start = self::time_hour($booking['start_time']);

                    if (
                        $row_start === false ||
                        !self::times_overlap(
                            $start_hour,
                            $hours,
                            $row_start,
                            $booking['hours']
                        )
                    ) {
                        continue;
                    }

                    $row_number = isset($booking['system_number'])
                        ? trim((string) $booking['system_number'])
                        : '';

                    $device_label = isset($device['label'])
                        ? (string) $device['label']
                        : self::physical_system_label($device);

                    $same_variation =
                        (int) $booking['variation_id'] === (int) $device['variation_id'];

                    $same_device =
                        $same_variation &&
                        (
                            $row_number === '' ||
                            self::system_numbers_match($row_number, $device_label)
                        );

                    if ($same_device) {
                        $status = 'reserved';
                        break;
                    }
                }
            }
        }

        // فقط اطلاعات عمومی دستگاه به مرورگر فرستاده می‌شود.
        $safe_devices[] = array(
            'system_id'    => (string) $device['system_id'],
            'number'       => (int) $device['number'],
            'label'        => (string) $device['label'],
            'category'     => (string) $device['category'],
            'variation_id' => (int) $device['variation_id'],
            'active'       => !empty($device['active']),
            'status'       => $status,
        );
    }

    return self::ok(array(
        'product_id' => (int) $product_id,

        'layout' => array(
            'version' => isset($layout['version']) ? (int) $layout['version'] : 1,
            'width'   => isset($layout['width']) ? (int) $layout['width'] : 1000,
            'height'  => isset($layout['height']) ? (int) $layout['height'] : 700,
            'objects' => $safe_objects,
        ),

        'date'       => $date,
        'start_time' => $start,
        'hours'      => $hours,
        'devices'    => $safe_devices,
        'server_time' => self::now()->format('c'),
    ));
}
    public static function report_summary_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $today = self::now()->format('Y-m-d');
        $from = self::clean_date((string) self::input($request, 'from', $today));
        $to = self::clean_date((string) self::input($request, 'to', $today));
        if (!$from || !$to || $to < $from) return self::fail('invalid_date_range', 'بازه گزارش معتبر نیست.', 422);
        $manuals = self::grouped_manuals_for_range($product_id, $from, $to);
        if (is_wp_error($manuals)) return $manuals;
        $online = self::online_bookings($product_id, $from, $to);
        $manual_count = count($manuals); $online_count = count($online); $manual_amount = 0.0; $online_amount = 0.0;
        foreach ($manuals as $row) $manual_amount += isset($row['amount']) ? (float) $row['amount'] : 0.0;
        foreach ($online as $row) $online_amount += isset($row['amount']) ? (float) $row['amount'] : 0.0;
        return self::ok(array('product_id' => (int) $product_id, 'range' => array('from' => $from, 'to' => $to), 'reservations' => array('manual' => $manual_count, 'online' => $online_count, 'total' => $manual_count + $online_count), 'amounts' => array('manual' => $manual_amount, 'online' => $online_amount, 'total' => $manual_amount + $online_amount), 'note' => 'این جمع‌بندی از داده‌های رزرو قابل‌خواندن API است؛ جایگزین دفترکل مالی یا گزارش حسابداری نیست.'));
    }

    public static function customer_search_route($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) return $user;
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) return $product;
        $term = trim(sanitize_text_field((string) self::input($request, array('q', 'search'), '')));
        if ($term !== '' && (function_exists('mb_strlen') ? mb_strlen($term) < 2 : strlen($term) < 2)) return self::fail('search_too_short', 'برای جست‌وجوی مشتری حداقل دو نویسه وارد کنید؛ برای دیدن همه مشتریان، q را خالی بگذارید.', 422);
        $manuals = self::get_manuals_authoritative($product_id);
        if (is_wp_error($manuals)) return $manuals;
        $found = array(); $needle = function_exists('mb_strtolower') ? mb_strtolower($term) : strtolower($term);
        foreach ($manuals as $row) {
            if (!is_array($row)) continue;
            $hay = trim((string) (isset($row['customer']) ? $row['customer'] : '') . ' ' . (isset($row['customer_id']) ? $row['customer_id'] : '') . ' ' . (isset($row['phone']) ? $row['phone'] : ''));
            $hay_cmp = function_exists('mb_strtolower') ? mb_strtolower($hay) : strtolower($hay);
            if ($hay !== '' && strpos($hay_cmp, $needle) !== false) {
                $id = isset($row['id']) ? (string) $row['id'] : '';
                $customer_key = trim((string) (isset($row['customer_id']) ? $row['customer_id'] : '') . '|' . (isset($row['phone']) ? $row['phone'] : ''));
                if ($customer_key === '|') $customer_key = trim((string) (isset($row['customer']) ? $row['customer'] : ''));
                if ($id !== '' && $customer_key !== '') $found[$customer_key] = array('customer' => isset($row['customer']) ? (string) $row['customer'] : '', 'customer_id' => isset($row['customer_id']) ? (string) $row['customer_id'] : '', 'phone' => isset($row['phone']) ? (string) $row['phone'] : '', 'last_booking_id' => $id, 'date_iso' => isset($row['date_iso']) ? (string) $row['date_iso'] : '', 'amount' => isset($row['amount']) ? (float) $row['amount'] : 0);
            }
            if (count($found) >= 50) break;
        }
        return self::ok(array('product_id' => (int) $product_id, 'query' => $term, 'customers' => array_values($found), 'limit' => 50));
    }

    /* ---------------------------------------------------------------------
     * Manual reservation persistence (authoritative database reads)
     * ------------------------------------------------------------------ */

    private static function manual_option_name($product_id) {
        return self::MANUAL_OPTION_PREFIX . absint($product_id);
    }

    private static function read_manual_option_from_database($product_id) {
        global $wpdb;
        $option_name = self::manual_option_name($product_id);
        $wpdb->last_error = '';
        $row = $wpdb->get_row($wpdb->prepare(
            "SELECT option_value FROM {$wpdb->options} WHERE option_name = %s LIMIT 1",
            $option_name
        ), ARRAY_A);
        if (!empty($wpdb->last_error)) {
            return array('found' => false, 'records' => array(), 'error' => $wpdb->last_error);
        }
        if (!$row) {
            return array('found' => false, 'records' => array(), 'error' => '');
        }
        $records = maybe_unserialize($row['option_value']);
        if (!is_array($records)) {
            return array('found' => true, 'records' => array(), 'error' => 'قالب داده ذخیره‌شده رزروهای دستی معتبر نیست.');
        }
        $clean = array();
        foreach ($records as $record) {
            if (is_array($record)) {
                $clean[] = $record;
            }
        }
        return array('found' => true, 'records' => array_values($clean), 'error' => '');
    }

    private static function get_manuals_authoritative($product_id) {
        $result = self::read_manual_option_from_database($product_id);
        if ($result['error'] !== '') {
            return self::fail('manual_storage_read_failed', 'خواندن رزروهای دستی از پایگاه داده ناموفق بود.', 500, array('detail' => $result['error']));
        }
        return $result['records'];
    }

    private static function get_manuals($product_id) {
        $records = self::get_manuals_authoritative($product_id);
        return is_wp_error($records) ? $records : $records;
    }

    private static function save_manuals($product_id, $records) {
        global $wpdb;
        $product_id = absint($product_id);
        if (!$product_id || !is_array($records)) {
            return self::fail('manual_storage_invalid', 'داده رزروهای دستی معتبر نیست.', 500);
        }
        $records = array_values(array_filter($records, 'is_array'));
        $option_name = self::manual_option_name($product_id);

        // update_option can return false both on failure and when the value is unchanged;
        // therefore verify the actual database value before deciding whether it failed.
        update_option($option_name, $records, false);
        wp_cache_delete($option_name, 'options');
        $verify = self::read_manual_option_from_database($product_id);
        if ($verify['error'] === '' && $verify['found'] && $verify['records'] === $records) {
            return true;
        }
        if ($verify['error'] === '' && !$records && !$verify['found']) {
            return true;
        }

        $serialized = maybe_serialize($records);
        $exists = $wpdb->get_var($wpdb->prepare(
            "SELECT option_id FROM {$wpdb->options} WHERE option_name = %s LIMIT 1",
            $option_name
        ));
        if ($exists) {
            $written = $wpdb->update(
                $wpdb->options,
                array('option_value' => $serialized, 'autoload' => 'no'),
                array('option_name' => $option_name),
                array('%s', '%s'),
                array('%s')
            );
            if ($written === false) {
                return self::fail('manual_storage_write_failed', 'ذخیره رزروهای دستی در پایگاه داده ناموفق بود.', 500, array('detail' => $wpdb->last_error));
            }
        } else {
            $written = $wpdb->insert(
                $wpdb->options,
                array('option_name' => $option_name, 'option_value' => $serialized, 'autoload' => 'no'),
                array('%s', '%s', '%s')
            );
            if ($written === false) {
                return self::fail('manual_storage_write_failed', 'ذخیره رزروهای دستی در پایگاه داده ناموفق بود.', 500, array('detail' => $wpdb->last_error));
            }
        }
        wp_cache_delete($option_name, 'options');
        $verify = self::read_manual_option_from_database($product_id);
        if ($verify['error'] !== '' || !$verify['found'] || $verify['records'] !== $records) {
            return self::fail('manual_storage_verify_failed', 'ذخیره رزروها انجام شد اما بررسی نهایی داده ناموفق بود.', 500, array('detail' => $verify['error']));
        }
        return true;
    }

    private static function locate_manual_booking($product_id, $booking_id) {
        $product_id = absint($product_id);
        $booking_id = (string) $booking_id;
        if (!$product_id || $booking_id === '') {
            return self::fail('booking_not_found', 'رزرو دستی پیدا نشد.', 404);
        }
        $records = self::get_manuals_authoritative($product_id);
        if (is_wp_error($records)) {
            return $records;
        }
        $found = array();
        foreach ($records as $record) {
            if (isset($record['id']) && (string) $record['id'] === $booking_id) {
                $found[] = $record;
            }
        }
        if ($found) {
            return array('product_id' => $product_id, 'records' => $records, 'booking_records' => $found);
        }

        // Compatibility path: some older app revisions wrote records into a different
        // product option. Search only the known Lobix options, never arbitrary options.
        global $wpdb;
        $like = $wpdb->esc_like(self::MANUAL_OPTION_PREFIX) . '%';
        $option_names = $wpdb->get_col($wpdb->prepare(
            "SELECT option_name FROM {$wpdb->options} WHERE option_name LIKE %s",
            $like
        ));
        foreach ((array) $option_names as $option_name) {
            if ($option_name === self::manual_option_name($product_id)) {
                continue;
            }
            if (!preg_match('/^' . preg_quote(self::MANUAL_OPTION_PREFIX, '/') . '(\d+)$/', $option_name, $m)) {
                continue;
            }
            $other_id = absint($m[1]);
            $other = self::get_manuals_authoritative($other_id);
            if (is_wp_error($other)) {
                return $other;
            }
            $matches = array();
            foreach ($other as $record) {
                if (isset($record['id']) && (string) $record['id'] === $booking_id) {
                    $matches[] = $record;
                }
            }
            if ($matches) {
                return array('product_id' => $other_id, 'records' => $other, 'booking_records' => $matches);
            }
        }
        return self::fail('booking_not_found', 'رزرو دستی پیدا نشد.', 404);
    }

    private static function new_manual_id() {
        if (function_exists('wp_generate_uuid4')) {
            return 'manual_' . str_replace('-', '', wp_generate_uuid4());
        }
        return 'manual_' . wp_generate_password(32, false, false);
    }

    private static function row_date_matches($record, $date) {
        if (!empty($record['date_iso'])) {
            return self::clean_date((string) $record['date_iso']) === $date;
        }
        $day = isset($record['day_code']) ? self::clean_day_code($record['day_code']) : false;
        return $day !== false && $day === self::day_code_from_date($date);
    }

    private static function row_hour($record) {
        if (!isset($record['hour'])) {
            return false;
        }
        $value = $record['hour'];
        if (is_numeric($value)) {
            $h = (int) $value;
            return ($h >= 0 && $h <= 23) ? $h : false;
        }
        if (is_string($value)) {
            $value = self::clean_time($value);
            return $value === false ? false : (int) substr($value, 0, 2);
        }
        return false;
    }

    private static function expand_booking_rows($booking_id, $product_id, $normalized, $date, $day_code, $start_hour, $hours, $customer, $customer_id, $phone, $amount, $note) {
        $rows = array();
        $created_at = self::now()->format('c');
        foreach ($normalized as $system) {
            $quantity = max(1, (int) $system['quantity']);
            for ($copy = 0; $copy < $quantity; $copy++) {
                for ($offset = 0; $offset < $hours; $offset++) {
                    $hour = $start_hour + $offset;
                    $rows[] = array(
                        'id' => (string) $booking_id,
                        'product_id' => (int) $product_id,
                        'var_id' => (int) $system['variation_id'],
                        'variation_id' => (int) $system['variation_id'],
                        'day_code' => (string) $day_code,
                        'hour' => (int) $hour,
                        'customer' => (string) $customer,
                        'customer_id' => (string) $customer_id,
                        'phone' => (string) $phone,
                        'amount' => $amount,
                        'note' => (string) $note,
                        'created_at' => $created_at,
                        'type' => 'manual',
                        'date_iso' => (string) $date,
                        'system_number' => isset($system['system_number']) ? (string) $system['system_number'] : '',
                        'system_id' => isset($system['system_id']) ? (string) $system['system_id'] : '',
                        'game_id' => isset($system['game_id']) ? (int) $system['game_id'] : 0,
                        'game_name' => isset($system['game_name']) ? (string) $system['game_name'] : '',
                    );
                }
            }
        }
        return $rows;
    }

    /* ---------------------------------------------------------------------
     * Reservation normalization, opening hours, capacity and conflicts
     * ------------------------------------------------------------------ */

    private static function variation_map($product) {
        $map = array();
        foreach (self::variation_data($product) as $variation) {
            $map[(int) $variation['id']] = $variation;
        }
        return $map;
    }

    private static function normalize_systems($systems, $variation_data) {
        if (!is_array($systems) || !$systems) {
            return self::fail('invalid_systems', 'حداقل یک دستگاه یا نوع سرویس باید انتخاب شود.', 400);
        }

        $expanded = array();
        foreach ($systems as $key => $item) {
            if (is_array($item)) {
                $var_id = absint(isset($item['variation_id']) ? $item['variation_id'] : (isset($item['var_id']) ? $item['var_id'] : (isset($item['id']) ? $item['id'] : 0)));
                $quantity = isset($item['quantity']) && is_numeric($item['quantity']) ? (int) $item['quantity'] : 1;
                $system_number = isset($item['system_number']) ? trim((string) $item['system_number']) : (isset($item['system']) ? trim((string) $item['system']) : '');
                $system_id = isset($item['system_id']) ? sanitize_text_field((string) $item['system_id']) : '';
                $game_id = isset($item['game_id']) ? absint($item['game_id']) : 0;
                $game_name = isset($item['game_name']) && is_scalar($item['game_name']) ? sanitize_text_field((string) $item['game_name']) : '';
            } else {
                // Legacy payload compatibility: { "901": 2 } or [901, 902].
                if (is_numeric($key) && (int) $key > 0 && is_numeric($item)) {
                    $var_id = absint($key);
                    $quantity = (int) $item;
                } elseif (is_numeric($item)) {
                    $var_id = absint($item);
                    $quantity = 1;
                } else {
                    continue;
                }
                $system_number = '';
                $system_id = '';
                $game_id = 0;
                $game_name = '';
            }
            if (!array_key_exists($var_id, $variation_data)) {
                return self::fail('invalid_variation', 'یکی از سرویس‌های انتخاب‌شده متعلق به این گیمنت نیست.', 422, array('variation_id' => $var_id));
            }
            if ($quantity < 1 || $quantity > 250) {
                return self::fail('invalid_quantity', 'تعداد دستگاه انتخاب‌شده معتبر نیست.', 422, array('variation_id' => $var_id));
            }
            $variant = $variation_data[$var_id];
            $valid_systems = isset($variant['system_numbers']) && is_array($variant['system_numbers']) ? array_map('strval', $variant['system_numbers']) : array();
            $games = isset($variant['games']) && is_array($variant['games']) ? $variant['games'] : array();
            $game_ids = array();
            foreach ($games as $game) {
                if (isset($game['id'])) {
                    $game_ids[] = (int) $game['id'];
                }
            }
            if ($system_number !== '') {
                if ($system_id === '' && (!$valid_systems || !in_array($system_number, $valid_systems, true))) {
                    return self::fail('invalid_system_number', 'شماره دستگاه انتخاب‌شده برای این سرویس معتبر نیست.', 422, array('variation_id' => $var_id, 'system_number' => $system_number));
                }
                if ($quantity !== 1) {
                    return self::fail('invalid_quantity', 'برای دستگاه مشخص، تعداد باید دقیقاً یک باشد.', 422, array('variation_id' => $var_id, 'system_number' => $system_number));
                }
            }
            if ($game_id > 0) {
                if (!$game_ids || !in_array($game_id, $game_ids, true)) {
                    return self::fail('invalid_game', 'بازی انتخاب‌شده برای این سرویس مجاز نیست.', 422, array('variation_id' => $var_id, 'game_id' => $game_id));
                }
                foreach ($games as $game) {
                    if ((int) $game['id'] === $game_id) {
                        $game_name = (string) $game['name'];
                        break;
                    }
                }
            }
            $expanded[] = array(
                'variation_id' => $var_id,
                'quantity' => $quantity,
                'system_number' => $system_number,
                'system_id' => $system_id,
                'game_id' => $game_id,
                'game_name' => $game_name,
            );
        }
        if (!$expanded) {
            return self::fail('invalid_systems', 'دستگاه یا سرویس انتخاب‌شده معتبر نیست.', 422);
        }

        $seen = array();
        $legacy_seen = array();
        foreach ($expanded as $entry) {
            if ($entry['system_number'] !== '') {
                $key = $entry['variation_id'] . '|' . $entry['system_number'];
                if (isset($seen[$key])) {
                    return self::fail('duplicate_system', 'یک دستگاه بیش از یک بار انتخاب شده است.', 422, array('variation_id' => $entry['variation_id'], 'system_number' => $entry['system_number']));
                }
                $seen[$key] = true;
            } else {
                $legacy_seen[$entry['variation_id']] = isset($legacy_seen[$entry['variation_id']]) ? $legacy_seen[$entry['variation_id']] + $entry['quantity'] : $entry['quantity'];
            }
        }
        foreach ($legacy_seen as $var_id => $quantity) {
            $limit = isset($variation_data[$var_id]['system_numbers']) ? count((array) $variation_data[$var_id]['system_numbers']) : 0;
            if ($limit > 0 && $quantity > $limit) {
                return self::fail('invalid_quantity', 'تعداد دستگاه از ظرفیت فیزیکی این سرویس بیشتر است.', 422, array('variation_id' => (int) $var_id, 'quantity' => $quantity, 'max_quantity' => $limit));
            }
        }
        return array('normalized' => $expanded);
    }

    private static function booking_disabled($product) {
        $product_id = $product->get_id();
        $mode = strtolower(trim((string) get_post_meta($product_id, '_gamenet_booking_mode', true)));
        if (in_array($mode, array('disabled', 'closed', 'off', '0', 'no'), true)) {
            return true;
        }
        if (get_post_meta($product_id, '_gamenet_booking_disabled', true) === 'yes') {
            return true;
        }
        return false;
    }

    private static function booking_window($product, $date, $start_hour, $hours) {
        $date = self::clean_date($date);
        if (!$date) {
            return self::fail('invalid_date', 'تاریخ رزرو معتبر نیست.', 422);
        }
        $start_hour = (int) $start_hour;
        $hours = (int) $hours;
        if ($start_hour < 0 || $start_hour > 23 || $hours < 1 || $hours > 24 || ($start_hour + $hours) > 24) {
            return self::fail('invalid_time_range', 'ساعت یا مدت رزرو معتبر نیست.', 422);
        }

        $product_id = $product->get_id();
        $work_start = self::time_hour((string) get_post_meta($product_id, '_gamenet_work_start', true));
        $work_end = self::time_hour((string) get_post_meta($product_id, '_gamenet_work_end', true), true);
        if ($work_start === false) {
            $work_start = 8;
        }
        if ($work_end === false) {
            $work_end = 24;
        }
        if ($work_end <= $work_start) {
            return self::fail('invalid_work_hours', 'ساعت کاری گیمنت به‌درستی تنظیم نشده است.', 422);
        }
        if ($start_hour < $work_start || ($start_hour + $hours) > $work_end) {
            return self::fail('outside_work_hours', 'زمان انتخاب‌شده خارج از ساعت کاری گیمنت است.', 422, array('work_start' => $work_start, 'work_end' => $work_end));
        }

        $start = new DateTimeImmutable($date . ' ' . sprintf('%02d:00:00', $start_hour), wp_timezone());
        if ($start <= self::now()) {
            return self::fail('booking_in_past', 'زمان شروع رزرو باید در آینده باشد.', 422);
        }
        return true;
    }

    private static function get_capacity($product_id, $variation_id, $variation_data = array()) {
        $meta_id = $variation_id ? $variation_id : $product_id;
        $capacity = absint(get_post_meta($meta_id, '_gamenet_var_capacity', true));
        if ($capacity > 0) {
            return $capacity;
        }
        if (isset($variation_data[$variation_id]['system_numbers']) && count((array) $variation_data[$variation_id]['system_numbers']) > 0) {
            return count((array) $variation_data[$variation_id]['system_numbers']);
        }
        $systems = self::variation_system_numbers($meta_id);
        return count($systems);
    }

    private static function reservations_for_hour($product_id, $variation_id, $date, $hour, $exclude_booking_id = '') {
        // Read the manual reservation option once per product/request, not once per hour.
        static $manual_cache = array();
        $cache_key = absint($product_id);
        if (!array_key_exists($cache_key, $manual_cache)) {
            $manual_cache[$cache_key] = self::get_manuals_authoritative($product_id);
        }
        $manuals = $manual_cache[$cache_key];
        if (is_wp_error($manuals)) {
            return $manuals;
        }
        $reserved = 0;
        foreach ($manuals as $record) {
            if (!isset($record['id']) || (string) $record['id'] === (string) $exclude_booking_id) {
                continue;
            }
            $row_var = isset($record['var_id']) ? absint($record['var_id']) : (isset($record['variation_id']) ? absint($record['variation_id']) : 0);
            if ($row_var !== (int) $variation_id || !self::row_date_matches($record, $date)) {
                continue;
            }
            if (self::row_hour($record) === (int) $hour) {
                $reserved++;
            }
        }
        $online = self::online_bookings($product_id, $date, $date);
        foreach ($online as $booking) {
            if (!self::online_booking_occupies_capacity($booking)) {
                continue;
            }
            if ((int) $booking['variation_id'] !== (int) $variation_id) {
                continue;
            }
            $start = self::time_hour($booking['start_time']);
            if ($start === false) {
                continue;
            }
            $duration = max(1, (int) $booking['hours']);
            if ((int) $hour >= $start && (int) $hour < ($start + $duration)) {
                $reserved += max(1, (int) $booking['quantity']);
            }
        }
        return $reserved;
    }

    private static function validate_capacity($product_id, $quantity_map, $variation_data, $date, $start_hour, $end_hour, $exclude_booking_id = '') {
        foreach ($quantity_map as $variation_id => $quantity) {
            $variation_id = (int) $variation_id;
            $quantity = (int) $quantity;
            if ($quantity < 1) {
                continue;
            }
            $capacity = self::get_capacity($product_id, $variation_id, $variation_data);
            if ($capacity < 1) {
                return self::fail('capacity_not_configured', 'ظرفیت این سرویس تنظیم نشده است.', 422, array('variation_id' => $variation_id));
            }
            for ($hour = (int) $start_hour; $hour < (int) $end_hour; $hour++) {
                $reserved = self::reservations_for_hour($product_id, $variation_id, $date, $hour, $exclude_booking_id);
                if (is_wp_error($reserved)) {
                    return $reserved;
                }
                if (((int) $reserved + $quantity) > $capacity) {
                    return self::fail('capacity_full', 'ظرفیت این سرویس در یکی از ساعت‌های انتخاب‌شده تکمیل است.', 409, array(
                        'variation_id' => $variation_id,
                        'date' => $date,
                        'hour' => $hour,
                        'capacity' => $capacity,
                        'reserved' => (int) $reserved,
                        'requested' => $quantity,
                        'available' => max(0, $capacity - (int) $reserved),
                    ));
                }
            }
        }
        return true;
    }

    private static function times_overlap($a_start, $a_hours, $b_start, $b_hours) {
        $a_start = (int) $a_start;
        $b_start = (int) $b_start;
        $a_end = $a_start + max(1, (int) $a_hours);
        $b_end = $b_start + max(1, (int) $b_hours);
        return $a_start < $b_end && $b_start < $a_end;
    }

    private static function check_device_conflicts($product_id, $normalized, $date, $start_hour, $hours, $exclude_booking_id = '') {
        $manuals = self::get_manuals_authoritative($product_id);
        if (is_wp_error($manuals)) {
            return $manuals;
        }
        $online = self::online_bookings($product_id, $date, $date);
        $end_hour = (int) $start_hour + (int) $hours;
        foreach ($normalized as $selected) {
            $var_id = (int) $selected['variation_id'];
            $selected_system = (string) $selected['system_number'];
            foreach ($manuals as $record) {
                if (empty($record['id']) || (string) $record['id'] === (string) $exclude_booking_id) {
                    continue;
                }
                $row_var = isset($record['var_id']) ? absint($record['var_id']) : (isset($record['variation_id']) ? absint($record['variation_id']) : 0);
                if ($row_var !== $var_id || !self::row_date_matches($record, $date)) {
                    continue;
                }
                $row_hour = self::row_hour($record);
                if ($row_hour === false || $row_hour < $start_hour || $row_hour >= $end_hour) {
                    continue;
                }
                $existing_system = isset($record['system_number']) ? trim((string) $record['system_number']) : '';
                $selected_id = isset($selected['system_id']) ? trim((string) $selected['system_id']) : '';
                $existing_id = isset($record['system_id']) ? trim((string) $record['system_id']) : '';
                // Exact IDs are authoritative. Older records fall back to the full category-number label.
                $same_device = ($selected_id !== '' && $existing_id !== '')
                    ? hash_equals($selected_id, $existing_id)
                    : self::system_numbers_match($existing_system, $selected_system);
                // Legacy bookings without device metadata reserve an unknown physical device.
                if ($selected_system === '' || $existing_system === '' || $same_device) {
                    return self::fail('device_conflict', 'این دستگاه در بازه انتخاب‌شده قبلاً رزرو شده است.', 409, array(
                        'source' => 'manual', 'booking_id' => (string) $record['id'], 'variation_id' => $var_id,
                        'system_number' => $selected_system !== '' ? $selected_system : $existing_system,
                        'date' => $date, 'hour' => (int) $row_hour,
                    ));
                }
            }
            foreach ($online as $booking) {
                if (!self::online_booking_occupies_capacity($booking)) {
                    continue;
                }
                if ((int) $booking['variation_id'] !== $var_id) {
                    continue;
                }
                $online_start = self::time_hour($booking['start_time']);
                if ($online_start === false || !self::times_overlap($start_hour, $hours, $online_start, $booking['hours'])) {
                    continue;
                }
                $online_system = isset($booking['system_number']) ? trim((string) $booking['system_number']) : '';
                if ($selected_system === '' || $online_system === '' || $online_system === $selected_system) {
                    return self::fail('device_conflict', 'این دستگاه در بازه انتخاب‌شده از طریق سایت رزرو شده است.', 409, array(
                        'source' => 'online', 'order_id' => (int) $booking['order_id'], 'variation_id' => $var_id,
                        'system_number' => $selected_system !== '' ? $selected_system : $online_system,
                        'date' => $date, 'start_time' => $booking['start_time'],
                    ));
                }
            }
        }
        return true;
    }

    /* ---------------------------------------------------------------------
     * Online WooCommerce reservations
     * ------------------------------------------------------------------ */

    private static function online_bookings($product_id, $from, $to) {
        static $cache = array();
        if (!function_exists('wc_get_orders')) {
            return array();
        }
        $from = self::clean_date($from);
        $to = self::clean_date($to);
        if (!$from || !$to) {
            return array();
        }
        $cache_key = absint($product_id) . '|' . $from . '|' . $to;
        if (isset($cache[$cache_key])) {
            return $cache[$cache_key];
        }
        $out = array();
        // Bounded pages avoid loading every WooCommerce order into memory at once.
        // Do not filter by order creation date: the order date is not the booked session date.
        $page = 1;
        $page_size = 100;
        do {
            $orders = wc_get_orders(array(
                'limit' => $page_size,
                'page' => $page,
                'paginate' => false,
                'status' => array('processing', 'completed', 'on-hold'),
                'product' => array((int) $product_id),
                'return' => 'objects',
                'orderby' => 'date',
                'order' => 'DESC',
            ));
            foreach ((array) $orders as $order) {
            if (!$order instanceof WC_Order) {
                continue;
            }
            foreach ($order->get_items('line_item') as $item_id => $item) {

                if (!$item instanceof WC_Order_Item_Product || (int) $item->get_product_id() !== (int) $product_id) {
                    continue;
                }
                $date = self::clean_date((string) $item->get_meta('_lx_date_iso', true));
                if (!$date || $date < $from || $date > $to) {
                    continue;
                }
                $start_time = self::clean_time((string) $item->get_meta('_lx_start_time', true));
                if ($start_time === false) {
                    continue;
                }
                $raw_hours = $item->get_meta('_lx_hours', true);
                $hours = self::normalize_hours($raw_hours);
                if (is_array($hours)) {
                    $hours = count($hours);
                }
                if ($hours === false || $hours < 1) {
                    $hours = 1;
                }
                $variation_id = (int) $item->get_variation_id();
                $system_number = $item->get_meta('_lx_system_number', true);
                if ($system_number === '' || $system_number === null) {
                    $system_number = $item->get_meta('lx_system_number', true);
                }
                $game_id = absint($item->get_meta('_lx_game_id', true));
                if (!$game_id) {
                    $game_id = absint($item->get_meta('lx_game_id', true));
                }
                $game_name = $item->get_meta('_lx_game_name', true);
                if (!$game_name) {
                    $game_name = $item->get_meta('lx_game_name', true);
                }
                $created = $order->get_date_created();
                $out[] = array(
                    'source' => 'online',
                    'order_id' => (int) $order->get_id(),
                    'item_id' => (int) $item_id,
                    'variation_id' => $variation_id,
                    'date' => $date,
                    'date_iso' => $date,
                    'start_time' => $start_time,
                    'hours' => (int) $hours,
                    'quantity' => max(1, (int) $item->get_quantity()),
                    'system_number' => is_scalar($system_number) ? (string) $system_number : '',
                    'game_id' => (int) $game_id,
                    'game_name' => is_scalar($game_name) ? (string) $game_name : '',
                    'customer' => trim($order->get_billing_first_name() . ' ' . $order->get_billing_last_name()),
                    'phone' => (string) $order->get_billing_phone(),
                    'amount' => (float) $item->get_total(),
                    'status' => (string) $order->get_status(),
                    'operator_status' => self::get_order_operator_decision($order),
                    'created_at' => $created ? $created->date('c') : '',
                );
            }
            }
            $has_more = is_array($orders) && count($orders) === $page_size;
            $page++;
            // Hard safety ceiling prevents pathological infinite loops on broken providers.
        } while ($has_more && $page <= 1001);
        $cache[$cache_key] = $out;
        return $out;
    }

    /**
     * Read the order-level operator decision.
     * Legacy online orders without this metadata remain pending for compatibility.
     */
    private static function get_order_operator_decision($order) {
        if (!$order instanceof WC_Order) {
            return 'pending';
        }
        $decision = strtolower(trim((string) $order->get_meta(self::ORDER_DECISION_META, true)));
        return in_array($decision, array('pending', 'confirmed', 'rejected'), true)
            ? $decision
            : 'pending';
    }

    /**
     * Rejected online orders remain visible in Sync/history but do not occupy
     * capacity or block a physical device.
     */
    private static function online_booking_occupies_capacity($booking) {
        if (!is_array($booking)) {
            return false;
        }
        $decision = isset($booking['operator_status'])
            ? strtolower(trim((string) $booking['operator_status']))
            : 'pending';
        return $decision !== 'rejected';
    }

    /**
     * Set one operator decision for the whole WooCommerce order.
     * No WooCommerce payment/order status is changed.
     */
    public static function booking_decision($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }

        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }

        $order_id = absint(self::input($request, array('order_id'), 0));
        if (!$order_id) {
            return self::fail('missing_order_id', 'شناسه سفارش ارسال نشده است.', 400);
        }

        $decision = strtolower(sanitize_text_field((string) self::input($request, array('decision'), '')));
        if (!in_array($decision, array('confirmed', 'rejected'), true)) {
            return self::fail('invalid_decision', 'تصمیم باید confirmed یا rejected باشد.', 422);
        }

        $note = sanitize_textarea_field((string) self::input($request, array('decision_note', 'note'), ''));
        if (function_exists('mb_substr')) {
            $note = mb_substr($note, 0, 500, 'UTF-8');
        } else {
            $note = substr($note, 0, 500);
        }

        $lock = self::lock_product($product_id);
        if (is_wp_error($lock)) {
            return $lock;
        }

        try {
            if (!function_exists('wc_get_order')) {
                return self::fail('woocommerce_unavailable', 'سرویس سفارش ووکامرس در دسترس نیست.', 503);
            }

            $order = wc_get_order($order_id);
            if (!$order instanceof WC_Order) {
                return self::fail('order_not_found', 'سفارش موردنظر پیدا نشد.', 404);
            }

            // Match the same order states that online_bookings() currently exposes.
            if (!in_array($order->get_status(), array('processing', 'completed', 'on-hold'), true)) {
                return self::fail(
                    'order_not_bookable',
                    'وضعیت فعلی سفارش اجازه تعیین تکلیف رزرو را نمی‌دهد.',
                    409,
                    array('order_status' => (string) $order->get_status())
                );
            }

            // The order must contain at least one actual booking line for this gamenet.
            $booking_item_count = 0;
            $reserved_system_quantity = 0;
            $booking_product_ids = array();

            foreach ($order->get_items('line_item') as $item) {
                if (!$item instanceof WC_Order_Item_Product) {
                    continue;
                }

                $item_date = self::clean_date((string) $item->get_meta('_lx_date_iso', true));
                $item_time = self::clean_time((string) $item->get_meta('_lx_start_time', true));
                if (!$item_date || $item_time === false) {
                    continue;
                }

                $line_product_id = (int) $item->get_product_id();
                $booking_product_ids[$line_product_id] = true;

                if ($line_product_id === (int) $product_id) {
                    $booking_item_count++;
                    $reserved_system_quantity += max(1, (int) $item->get_quantity());
                }
            }

            // A single order-level decision must not let one gamenet owner decide
            // reservations belonging to a different gamenet in the same order.
            if (count($booking_product_ids) > 1) {
                return self::fail(
                    'mixed_gamenet_order',
                    'این سفارش شامل رزرو چند گیمنت است؛ تصمیم یکپارچه سفارش برای آن امن نیست.',
                    409,
                    array('booking_product_ids' => array_map('absint', array_keys($booking_product_ids)))
                );
            }

            if ($booking_item_count < 1) {
                return self::fail(
                    'order_booking_not_found',
                    'این سفارش شامل رزرو معتبری برای این گیمنت نیست.',
                    404,
                    array('order_id' => $order_id, 'product_id' => $product_id)
                );
            }

            $current = self::get_order_operator_decision($order);

            // Repeating the same decision is safe; reversing a final decision is not.
            if ($current === $decision) {
                return self::ok(array(
                    'order_id' => (int) $order_id,
                    'product_id' => (int) $product_id,
                    'operator_status' => $current,
                    'already_set' => true,
                    'booking_item_count' => (int) $booking_item_count,
                    'reserved_system_quantity' => (int) $reserved_system_quantity,
                    'order_status' => (string) $order->get_status(),
                    'decision_at' => (string) $order->get_meta(self::ORDER_DECISION_AT_META, true),
                    'decision_by' => (int) $order->get_meta(self::ORDER_DECISION_BY_META, true),
                ));
            }

            if ($current !== 'pending') {
                return self::fail(
                    'decision_already_final',
                    'این سفارش قبلاً تأیید یا رد شده و تغییر تصمیم آن مجاز نیست.',
                    409,
                    array('operator_status' => $current)
                );
            }

            $decision_at = self::now()->format('c');
            $order->update_meta_data(self::ORDER_DECISION_META, $decision);
            $order->update_meta_data(self::ORDER_DECISION_AT_META, $decision_at);
            $order->update_meta_data(self::ORDER_DECISION_BY_META, (int) $user->ID);
            if ($note !== '') {
                $order->update_meta_data(self::ORDER_DECISION_NOTE_META, $note);
            } else {
                $order->delete_meta_data(self::ORDER_DECISION_NOTE_META);
            }

            $order->save();

            // Verify persistence before reporting success to the Windows client.
            $saved_order = wc_get_order($order_id);
            $saved_decision = self::get_order_operator_decision($saved_order);
            if ($saved_decision !== $decision) {
                return self::fail('decision_save_failed', 'ذخیره تصمیم اپراتور در سفارش تأیید نشد.', 500);
            }

            return self::ok(array(
                'order_id' => (int) $order_id,
                'product_id' => (int) $product_id,
                'operator_status' => $saved_decision,
                'already_set' => false,
                'booking_item_count' => (int) $booking_item_count,
                'reserved_system_quantity' => (int) $reserved_system_quantity,
                'decision_at' => $decision_at,
                'decision_by' => (int) $user->ID,
                'order_status' => (string) $saved_order->get_status(),
            ));
        } finally {
            self::unlock_product($lock);
        }
    }

    /* ---------------------------------------------------------------------
     * Idempotency and short-lived per-product database lock
     * ------------------------------------------------------------------ */

    private static function idempotency_key($request) {
        $key = (string) $request->get_header('x-lobix-idempotency-key');
        if ($key === '') {
            $key = (string) $request->get_param('idempotency_key');
        }
        $key = trim($key);
        if ($key === '') {
            return false;
        }
        if (strlen($key) > 128 || !preg_match('/^[A-Za-z0-9._:\-]+$/', $key)) {
            return false;
        }
        return $key;
    }

    private static function idempotency_option_name($user_id, $key) {
        return 'lbxop_idem_' . absint($user_id) . '_' . hash('sha256', (string) $key);
    }

    private static function request_payload_hash($request) {
        $body = $request->get_json_params();
        if (!is_array($body)) {
            $body = $request->get_body_params();
        }
        if (!is_array($body)) {
            $body = array();
        }
        unset($body['idempotency_key']);
        if (isset($body['_wpnonce'])) {
            unset($body['_wpnonce']);
        }
        ksort($body);
        return hash('sha256', wp_json_encode($body));
    }

    private static function idempotency_get($user_id, $key) {
        $name = self::idempotency_option_name($user_id, $key);
        $record = get_option($name, false);
        if (!is_array($record)) {
            return false;
        }
        $created = isset($record['created_at']) ? (int) $record['created_at'] : 0;
        if (!$created || (time() - $created) > self::IDEMPOTENCY_TTL) {
            delete_option($name);
            return false;
        }
        return $record;
    }

    private static function idempotency_store($user_id, $key, $payload_hash, $body, $status = 200) {
        $name = self::idempotency_option_name($user_id, $key);
        $record = array(
            'created_at' => time(),
            'payload_hash' => (string) $payload_hash,
            'body' => $body,
            'status' => (int) $status,
        );
        update_option($name, $record, false);
        return get_option($name, false) === $record;
    }

    private static function idempotency_replay_or_error($user_id, $key, $payload_hash) {
        $cached = self::idempotency_get($user_id, $key);
        if (!$cached) {
            return false;
        }
        if (!isset($cached['payload_hash']) || !hash_equals((string) $cached['payload_hash'], (string) $payload_hash)) {
            return self::fail('idempotency_conflict', 'این کلید تکرارپذیری قبلاً برای درخواست دیگری استفاده شده است.', 409);
        }
        $body = isset($cached['body']) && is_array($cached['body']) ? $cached['body'] : array('success' => true, 'data' => array());
        return new WP_REST_Response($body, isset($cached['status']) ? (int) $cached['status'] : 200);
    }

    private static function lock_product($product_id) {
        global $wpdb;
        $name = 'lbxop_' . substr(md5((string) absint($product_id)), 0, 32);
        $result = $wpdb->get_var($wpdb->prepare('SELECT GET_LOCK(%s, %d)', $name, self::LOCK_TTL));
        if ((string) $result !== '1') {
            return self::fail('booking_locked', 'ثبت یا ویرایش رزرو دیگری در حال انجام است؛ دوباره تلاش کنید.', 409);
        }
        return $name;
    }

    private static function unlock_product($lock_name) {
        if (!$lock_name || is_wp_error($lock_name)) {
            return;
        }
        global $wpdb;
        $wpdb->get_var($wpdb->prepare('SELECT RELEASE_LOCK(%s)', (string) $lock_name));
    }

    private static function quantity_map_from_normalized($normalized) {
        $map = array();
        foreach ($normalized as $item) {
            $var_id = (int) $item['variation_id'];
            $map[$var_id] = isset($map[$var_id]) ? $map[$var_id] + (int) $item['quantity'] : (int) $item['quantity'];
        }
        return $map;
    }

    private static function booking_fields($request, $fallback = array()) {
        $date_raw = self::input($request, array('date_iso', 'date', 'booking_date'), isset($fallback['date_iso']) ? $fallback['date_iso'] : '');
        $date = self::clean_date((string) $date_raw);
        $start_raw = self::input($request, array('start_time', 'start_hour', 'hour'), isset($fallback['start_hour']) ? $fallback['start_hour'] : '');
        $start_time = self::clean_time((string) $start_raw);
        $hours_raw = self::input($request, array('hours', 'duration'), isset($fallback['hours']) ? $fallback['hours'] : 1);
        $hours = self::normalize_hours($hours_raw);
        if (is_array($hours)) {
            $hours = count($hours);
        }
        if ($hours === false) {
            $hours = 0;
        }
        $customer = sanitize_text_field((string) self::input($request, array('customer', 'customer_name', 'name'), isset($fallback['customer']) ? $fallback['customer'] : ''));
        $customer_id = sanitize_text_field((string) self::input($request, array('customer_id', 'national_id'), isset($fallback['customer_id']) ? $fallback['customer_id'] : ''));
        $phone = sanitize_text_field((string) self::input($request, array('phone', 'customer_phone'), isset($fallback['phone']) ? $fallback['phone'] : ''));
        $note = sanitize_textarea_field((string) self::input($request, array('note', 'description'), isset($fallback['note']) ? $fallback['note'] : ''));
        $amount = self::format_amount(self::input($request, array('amount', 'price', 'total'), isset($fallback['amount']) ? $fallback['amount'] : 0));
        if (!$date) {
            return self::fail('invalid_date', 'تاریخ رزرو معتبر نیست.', 422);
        }
        if ($start_time === false) {
            return self::fail('invalid_start_time', 'ساعت شروع رزرو معتبر نیست.', 422);
        }
        if (!is_int($hours) || $hours < 1 || $hours > 24) {
            return self::fail('invalid_hours', 'مدت رزرو باید بین ۱ تا ۲۴ ساعت باشد.', 422);
        }
        $start_hour = (int) substr($start_time, 0, 2);
        if (($start_hour + $hours) > 24) {
            return self::fail('invalid_time_range', 'رزرو نمی‌تواند از نیمه‌شب عبور کند.', 422);
        }
        if ($customer === '') {
            return self::fail('missing_customer', 'نام مشتری الزامی است.', 422);
        }
        if ($phone !== '' && !preg_match('/^[0-9+()\-\s]{5,25}$/', $phone)) {
            return self::fail('invalid_phone', 'شماره تماس معتبر نیست.', 422);
        }
        return array(
            'date' => $date,
            'start_time' => $start_time,
            'start_hour' => $start_hour,
            'hours' => $hours,
            'end_hour' => $start_hour + $hours,
            'customer' => $customer,
            'customer_id' => $customer_id,
            'phone' => $phone,
            'note' => $note,
            'amount' => $amount,
        );
    }

    /* ---------------------------------------------------------------------
     * Create manual reservation
     * ------------------------------------------------------------------ */

    public static function create_booking($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }

        $key = self::idempotency_key($request);
        if (!$key) {
            return self::fail('idempotency_key_required', 'هدر X-Lobix-Idempotency-Key برای ثبت رزرو الزامی است.', 400);
        }
        $payload_hash = self::request_payload_hash($request);
        $replay = self::idempotency_replay_or_error($user->ID, $key, $payload_hash);
        if ($replay !== false) {
            return $replay;
        }

        $fields = self::booking_fields($request);
        if (is_wp_error($fields)) {
            return $fields;
        }
        if (self::booking_disabled($product)) {
            return self::fail('booking_disabled', 'ثبت رزرو برای این گیمنت غیرفعال است.', 409);
        }
        $window = self::booking_window($product, $fields['date'], $fields['start_hour'], $fields['hours']);
        if (is_wp_error($window)) {
            return $window;
        }

        $var_data = self::variation_map($product);
        $raw_systems = self::input($request, array('systems', 'selected_systems', 'items'), null);
        if ($raw_systems === null) {
            // Older builds sent a single variation_id and quantity instead of systems[].
            $old_var = absint(self::input($request, array('variation_id', 'var_id'), 0));
            $old_qty = absint(self::input($request, array('quantity'), 1));
            $raw_systems = $old_var ? array(array('variation_id' => $old_var, 'quantity' => max(1, $old_qty))) : array();
        }
        $raw_systems = self::resolve_system_ids($product_id, $raw_systems);
        if (is_wp_error($raw_systems)) {
            return $raw_systems;
        }
        $normalized_result = self::normalize_systems($raw_systems, $var_data);
        if (is_wp_error($normalized_result)) {
            return $normalized_result;
        }
        $normalized = $normalized_result['normalized'];

        $lock = self::lock_product($product_id);
        if (is_wp_error($lock)) {
            return $lock;
        }
        try {
            // An idempotent replay can arrive while another request was waiting for the lock.
            $replay = self::idempotency_replay_or_error($user->ID, $key, $payload_hash);
            if ($replay !== false) {
                return $replay;
            }
            $capacity = self::validate_capacity($product_id, self::quantity_map_from_normalized($normalized), $var_data, $fields['date'], $fields['start_hour'], $fields['end_hour']);
            if (is_wp_error($capacity)) {
                return $capacity;
            }
            $conflicts = self::check_device_conflicts($product_id, $normalized, $fields['date'], $fields['start_hour'], $fields['hours']);
            if (is_wp_error($conflicts)) {
                return $conflicts;
            }

            $old_records = self::get_manuals_authoritative($product_id);
            if (is_wp_error($old_records)) {
                return $old_records;
            }
            $booking_id = self::new_manual_id();
            $new_rows = self::expand_booking_rows(
                $booking_id, $product_id, $normalized, $fields['date'], self::day_code_from_date($fields['date']),
                $fields['start_hour'], $fields['hours'], $fields['customer'], $fields['customer_id'],
                $fields['phone'], $fields['amount'], $fields['note']
            );
            $saved = self::save_manuals($product_id, array_merge($old_records, $new_rows));
            if (is_wp_error($saved)) {
                self::save_manuals($product_id, $old_records);
                return $saved;
            }
            $body = array(
                'success' => true,
                'data' => array(
                    'booking_id' => $booking_id,
                    'id' => $booking_id,
                    'status' => 'created',
                    'product_id' => (int) $product_id,
                    'date_iso' => $fields['date'],
                    'start_time' => $fields['start_time'],
                    'hours' => (int) $fields['hours'],
                    'systems' => $normalized,
                ),
            );
            self::idempotency_store($user->ID, $key, $payload_hash, $body, 200);
            return new WP_REST_Response($body, 200);
        } finally {
            self::unlock_product($lock);
        }
    }

    /* ---------------------------------------------------------------------
     * Update manual reservation (capacity and physical-device conflicts included)
     * ------------------------------------------------------------------ */

    private static function normalized_from_existing_rows($rows) {
        $grouped = array();
        $per_hour_legacy_counts = array();
        foreach ($rows as $row) {
            $var_id = isset($row['var_id']) ? absint($row['var_id']) : (isset($row['variation_id']) ? absint($row['variation_id']) : 0);
            $system = isset($row['system_number']) ? trim((string) $row['system_number']) : '';
            $game_id = isset($row['game_id']) ? absint($row['game_id']) : 0;
            $game_name = isset($row['game_name']) ? (string) $row['game_name'] : '';
            if ($system !== '') {
                $key = $var_id . '|' . $system . '|' . $game_id;
                if (!isset($grouped[$key])) {
                    $grouped[$key] = array('variation_id' => $var_id, 'quantity' => 1, 'system_number' => $system, 'system_id' => isset($row['system_id']) ? (string) $row['system_id'] : '', 'game_id' => $game_id, 'game_name' => $game_name);
                }
            } else {
                $hour = isset($row['hour']) ? (string) $row['hour'] : 'unknown';
                $key = $var_id . '|legacy|' . $game_id;
                if (!isset($grouped[$key])) {
                    $grouped[$key] = array('variation_id' => $var_id, 'quantity' => 1, 'system_number' => '', 'game_id' => $game_id, 'game_name' => $game_name);
                    $per_hour_legacy_counts[$key] = array();
                }
                $per_hour_legacy_counts[$key][$hour] = isset($per_hour_legacy_counts[$key][$hour]) ? $per_hour_legacy_counts[$key][$hour] + 1 : 1;
            }
        }
        foreach ($per_hour_legacy_counts as $key => $counts) {
            $grouped[$key]['quantity'] = $counts ? max($counts) : 1;
        }
        return array_values($grouped);
    }

    public static function update_booking($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }
        $booking_id = self::request_booking_id($request);
        if ($booking_id === '') {
            return self::fail('missing_booking_id', 'شناسه رزرو ارسال نشده است.', 400);
        }
        $lock = self::lock_product($product_id);
        if (is_wp_error($lock)) {
            return $lock;
        }
        try {
            $located = self::locate_manual_booking($product_id, $booking_id);
            if (is_wp_error($located)) {
                return $located;
            }
            if ((int) $located['product_id'] !== (int) $product_id) {
                return self::fail('booking_product_mismatch', 'این رزرو به گیمنت دیگری تعلق دارد.', 409);
            }
            $old_records = $located['records'];
            $old_rows = $located['booking_records'];
            $first = reset($old_rows);
            if (!is_array($first)) {
                return self::fail('booking_not_found', 'رزرو دستی پیدا نشد.', 404);
            }

            $old_hours = array();
            foreach ($old_rows as $row) {
                $h = self::row_hour($row);
                if ($h !== false) {
                    $old_hours[] = $h;
                }
            }
            $old_hours = array_values(array_unique($old_hours));
            sort($old_hours, SORT_NUMERIC);
            $fallback = array(
                'date_iso' => isset($first['date_iso']) ? $first['date_iso'] : '',
                'start_hour' => $old_hours ? min($old_hours) : (isset($first['hour']) ? $first['hour'] : ''),
                'hours' => $old_hours ? count($old_hours) : 1,
                'customer' => isset($first['customer']) ? $first['customer'] : '',
                'customer_id' => isset($first['customer_id']) ? $first['customer_id'] : '',
                'phone' => isset($first['phone']) ? $first['phone'] : '',
                'note' => isset($first['note']) ? $first['note'] : '',
                'amount' => isset($first['amount']) ? $first['amount'] : 0,
            );
            $fields = self::booking_fields($request, $fallback);
            if (is_wp_error($fields)) {
                return $fields;
            }
            if (self::booking_disabled($product)) {
                return self::fail('booking_disabled', 'ثبت رزرو برای این گیمنت غیرفعال است.', 409);
            }
            $window = self::booking_window($product, $fields['date'], $fields['start_hour'], $fields['hours']);
            if (is_wp_error($window)) {
                return $window;
            }

            $var_data = self::variation_map($product);
            $raw_systems = self::input($request, array('systems', 'selected_systems', 'items'), null);
            if ($raw_systems === null) {
                $raw_systems = self::normalized_from_existing_rows($old_rows);
            }
            $raw_systems = self::resolve_system_ids($product_id, $raw_systems);
            if (is_wp_error($raw_systems)) {
                return $raw_systems;
            }
            $normalized_result = self::normalize_systems($raw_systems, $var_data);
            if (is_wp_error($normalized_result)) {
                return $normalized_result;
            }
            $normalized = $normalized_result['normalized'];
            $capacity = self::validate_capacity($product_id, self::quantity_map_from_normalized($normalized), $var_data, $fields['date'], $fields['start_hour'], $fields['end_hour'], $booking_id);
            if (is_wp_error($capacity)) {
                return $capacity;
            }
            $conflicts = self::check_device_conflicts($product_id, $normalized, $fields['date'], $fields['start_hour'], $fields['hours'], $booking_id);
            if (is_wp_error($conflicts)) {
                return $conflicts;
            }

            $remaining = array();
            foreach ($old_records as $record) {
                if (!isset($record['id']) || (string) $record['id'] !== $booking_id) {
                    $remaining[] = $record;
                }
            }
            $new_rows = self::expand_booking_rows(
                $booking_id, $product_id, $normalized, $fields['date'], self::day_code_from_date($fields['date']),
                $fields['start_hour'], $fields['hours'], $fields['customer'], $fields['customer_id'],
                $fields['phone'], $fields['amount'], $fields['note']
            );
            $saved = self::save_manuals($product_id, array_merge($remaining, $new_rows));
            if (is_wp_error($saved)) {
                self::save_manuals($product_id, $old_records);
                return $saved;
            }
            return self::ok(array(
                'booking_id' => $booking_id,
                'id' => $booking_id,
                'status' => 'updated',
                'product_id' => (int) $product_id,
                'date_iso' => $fields['date'],
                'start_time' => $fields['start_time'],
                'hours' => (int) $fields['hours'],
                'systems' => $normalized,
            ));
        } finally {
            self::unlock_product($lock);
        }
    }

    /* ---------------------------------------------------------------------
     * Delete manual reservation
     * ------------------------------------------------------------------ */

    public static function delete_booking($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }
        $booking_id = self::request_booking_id($request);
        if ($booking_id === '') {
            return self::fail('missing_booking_id', 'شناسه رزرو ارسال نشده است.', 400);
        }
        $lock = self::lock_product($product_id);
        if (is_wp_error($lock)) {
            return $lock;
        }
        try {
            $located = self::locate_manual_booking($product_id, $booking_id);
            if (is_wp_error($located)) {
                return $located;
            }
            if ((int) $located['product_id'] !== (int) $product_id) {
                return self::fail('booking_product_mismatch', 'این رزرو به گیمنت دیگری تعلق دارد.', 409);
            }
            $remaining = array();
            $deleted = 0;
            foreach ($located['records'] as $record) {
                if (isset($record['id']) && (string) $record['id'] === $booking_id) {
                    $deleted++;
                } else {
                    $remaining[] = $record;
                }
            }
            if (!$deleted) {
                return self::fail('booking_not_found', 'رزرو دستی پیدا نشد.', 404);
            }
            $saved = self::save_manuals($product_id, $remaining);
            if (is_wp_error($saved)) {
                self::save_manuals($product_id, $located['records']);
                return $saved;
            }
            return self::ok(array('booking_id' => $booking_id, 'id' => $booking_id, 'status' => 'deleted', 'deleted_rows' => $deleted));
        } finally {
            self::unlock_product($lock);
        }
    }

    /* ---------------------------------------------------------------------
     * Group reservations for sync, preserving old fields and exposing systems[]
     * ------------------------------------------------------------------ */

    private static function grouped_manuals_for_range($product_id, $from, $to) {
        $records = self::get_manuals_authoritative($product_id);
        if (is_wp_error($records)) {
            return $records;
        }
        $grouped_rows = array();
        $start = new DateTimeImmutable($from, wp_timezone());
        $end = new DateTimeImmutable($to, wp_timezone());
        $booking_ids = array();
        foreach ($records as $index => $record) {
            if (empty($record['id'])) {
                continue;
            }
            $booking_id = (string) $record['id'];
            if (!isset($booking_ids[$booking_id])) {
                $booking_ids[$booking_id] = true;
            }
            if (!empty($record['date_iso'])) {
                $date = self::clean_date((string) $record['date_iso']);
                if ($date && $date >= $from && $date <= $to) {
                    $key = $booking_id . '|' . $date;
                    $grouped_rows[$key]['id'] = $booking_id;
                    $grouped_rows[$key]['date_iso'] = $date;
                    $grouped_rows[$key]['records'][] = $record;
                }
                continue;
            }
            // Legacy recurring records use day_code without a concrete date.
            $day_code = isset($record['day_code']) ? self::clean_day_code($record['day_code']) : false;
            if (!$day_code) {
                continue;
            }
            $cursor = $start;
            while ($cursor <= $end) {
                if (strtolower($cursor->format('D')) === $day_code) {
                    $date = $cursor->format('Y-m-d');
                    $key = $booking_id . '|' . $date;
                    $grouped_rows[$key]['id'] = $booking_id;
                    $grouped_rows[$key]['date_iso'] = $date;
                    $grouped_rows[$key]['records'][] = $record;
                }
                $cursor = $cursor->modify('+1 day');
            }
        }

        $out = array();
        foreach ($grouped_rows as $group) {
            $rows = isset($group['records']) ? $group['records'] : array();
            if (!$rows) {
                continue;
            }
            $first = reset($rows);
            $hours = array();
            $systems_map = array();
            $legacy_counts = array();
            foreach ($rows as $row) {
                $hour = self::row_hour($row);
                if ($hour === false) {
                    continue;
                }
                $hours[] = $hour;
                $var_id = isset($row['var_id']) ? absint($row['var_id']) : (isset($row['variation_id']) ? absint($row['variation_id']) : 0);
                $system_number = isset($row['system_number']) ? trim((string) $row['system_number']) : '';
                $game_id = isset($row['game_id']) ? absint($row['game_id']) : 0;
                $game_name = isset($row['game_name']) ? (string) $row['game_name'] : '';
                $system_id = isset($row['system_id']) ? sanitize_text_field((string) $row['system_id']) : '';
                $sys_key = $var_id . '|' . ($system_id !== '' ? $system_id : ($system_number !== '' ? $system_number : 'legacy')) . '|' . $game_id;
                if (!isset($systems_map[$sys_key])) {
                    $systems_map[$sys_key] = array(
                        'variation_id' => $var_id,
                        'var_id' => $var_id,
                        'quantity' => 1,
                        'hours' => array(),
                        'system_number' => $system_number,
                        'system_id' => $system_id,
                        'game_id' => $game_id,
                        'game_name' => $game_name,
                    );
                    $legacy_counts[$sys_key] = array();
                }
                $systems_map[$sys_key]['hours'][] = $hour;
                if ($system_number === '') {
                    $legacy_counts[$sys_key][$hour] = isset($legacy_counts[$sys_key][$hour]) ? $legacy_counts[$sys_key][$hour] + 1 : 1;
                }
            }
            foreach ($systems_map as $sys_key => &$system) {
                $system_hour_numbers = array_values(array_unique(array_map('intval', $system['hours'])));
                sort($system_hour_numbers, SORT_NUMERIC);
                // Keep the wire contract used by the Windows app: hours are HH:00 strings.
                $system['hours'] = array_map(function ($h) { return sprintf('%02d:00', (int) $h); }, $system_hour_numbers);
                if ($system['system_number'] === '' && !empty($legacy_counts[$sys_key])) {
                    $system['quantity'] = max($legacy_counts[$sys_key]);
                }
            }
            unset($system);
            $hour_numbers = array_values(array_unique(array_map('intval', $hours)));
            sort($hour_numbers, SORT_NUMERIC);
            $hours = array_map(function ($h) { return sprintf('%02d:00', (int) $h); }, $hour_numbers);
            $start_hour = $hour_numbers ? min($hour_numbers) : 0;
            $out[] = array(
                'id' => (string) $group['id'],
                'booking_id' => (string) $group['id'],
                'product_id' => (int) $product_id,
                'date_iso' => (string) $group['date_iso'],
                'date' => (string) $group['date_iso'],
                'day_code' => isset($first['day_code']) ? (string) $first['day_code'] : self::day_code_from_date($group['date_iso']),
                'start_time' => sprintf('%02d:00', $start_hour),
                'start_hour' => $start_hour,
                'hours' => $hours,
                'duration' => count($hours),
                'customer' => isset($first['customer']) ? (string) $first['customer'] : '',
                'customer_id' => isset($first['customer_id']) ? (string) $first['customer_id'] : '',
                'phone' => isset($first['phone']) ? (string) $first['phone'] : '',
                'amount' => isset($first['amount']) ? $first['amount'] : 0,
                'note' => isset($first['note']) ? (string) $first['note'] : '',
                'created_at' => isset($first['created_at']) ? (string) $first['created_at'] : '',
                'type' => 'manual',
                'systems' => array_values($systems_map),
            );
        }
        usort($out, function ($a, $b) {
            $ak = $a['date_iso'] . ' ' . $a['start_time'];
            $bk = $b['date_iso'] . ' ' . $b['start_time'];
            return strcmp($ak, $bk);
        });
        return $out;
    }

    private static function request_sync_range($request) {
        $today = self::now()->format('Y-m-d');
        $from = self::clean_date((string) self::input($request, array('from', 'date_from', 'start_date', 'start'), $today));
        $default_to = (new DateTimeImmutable($today, wp_timezone()))->modify('+6 days')->format('Y-m-d');
        $to = self::clean_date((string) self::input($request, array('to', 'date_to', 'end_date', 'end'), $default_to));
        if (!$from || !$to) {
            return self::fail('invalid_date_range', 'بازه تاریخ Sync معتبر نیست.', 422);
        }
        if ($to < $from) {
            return self::fail('invalid_date_range', 'تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد.', 422);
        }
        $a = new DateTimeImmutable($from, wp_timezone());
        $b = new DateTimeImmutable($to, wp_timezone());
        $days = (int) $a->diff($b)->days + 1;
        if ($days > self::MAX_SYNC_DAYS) {
            return self::fail('sync_range_too_large', 'بازه Sync نمی‌تواند بیشتر از ۳۱ روز باشد.', 422, array('max_days' => self::MAX_SYNC_DAYS));
        }
        return array('from' => $from, 'to' => $to, 'days' => $days);
    }

    public static function sync($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }
        $range = self::request_sync_range($request);
        if (is_wp_error($range)) {
            return $range;
        }
        $manuals = self::grouped_manuals_for_range($product_id, $range['from'], $range['to']);
        if (is_wp_error($manuals)) {
            return $manuals;
        }
        $online = self::online_bookings($product_id, $range['from'], $range['to']);
        self::provision_physical_systems_from_variations($product_id);
        $gamenet = self::gamenet_payload($product);
        $data = array(
            'product_id' => (int) $product_id,
            'range' => array('from' => $range['from'], 'to' => $range['to'], 'days' => $range['days']),
            'gamenet' => $gamenet,
            'variations' => self::variation_data($product),
            'manual_bookings' => $manuals,
            'online_bookings' => $online,
            'physical_systems' => self::physical_systems_for_product($product_id, true),
            'floor_layout' => self::get_floor_layout($product_id),
        );
        $sync_at = self::now()->format('c');
        $hash = hash('sha256', wp_json_encode($data));
        // Compatibility: LobixApp deserializes Sync into GamenetSyncResponse
        // and expects the payload under `data`, while older clients read fields
        // directly from the root. Return both shapes without removing root fields.
        return self::ok(array_merge($data, array('data' => $data)), 200, array(
            'api_version' => self::VERSION,
            'server_time' => $sync_at,
            'sync_at' => $sync_at,
            'hash' => $hash,
        ));
    }

    /* ---------------------------------------------------------------------
     * Availability endpoint
     * ------------------------------------------------------------------ */

    public static function availability_endpoint($request) {
        $user = self::require_operator($request);
        if (is_wp_error($user)) {
            return $user;
        }
        $product_id = self::request_product_id($request);
        $product = self::require_product_access($user, $product_id);
        if (is_wp_error($product)) {
            return $product;
        }
        $date = self::clean_date((string) self::input($request, array('date_iso', 'date', 'booking_date'), ''));
        if (!$date) {
            return self::fail('invalid_date', 'تاریخ برای بررسی ظرفیت الزامی و باید معتبر باشد.', 422);
        }
        $variation_id = absint(self::input($request, array('variation_id', 'var_id'), 0));
        $var_data = self::variation_map($product);
        if (!array_key_exists($variation_id, $var_data)) {
            return self::fail('invalid_variation', 'سرویس انتخاب‌شده متعلق به این گیمنت نیست.', 422, array('variation_id' => $variation_id));
        }
        $exclude = self::request_booking_id($request);
        $variant = $var_data[$variation_id];
        $meta_id = $variation_id ? $variation_id : $product_id;
        $work_start = self::time_hour((string) get_post_meta($meta_id, '_gamenet_work_start', true));
        $work_end = self::time_hour((string) get_post_meta($meta_id, '_gamenet_work_end', true), true);
        if ($work_start === false) {
            $work_start = self::time_hour((string) get_post_meta($product_id, '_gamenet_work_start', true));
        }
        if ($work_end === false) {
            $work_end = self::time_hour((string) get_post_meta($product_id, '_gamenet_work_end', true), true);
        }
        if ($work_start === false) {
            $work_start = 8;
        }
        if ($work_end === false) {
            $work_end = 24;
        }
        if ($work_end <= $work_start) {
            return self::fail('invalid_work_hours', 'ساعت کاری سرویس به‌درستی تنظیم نشده است.', 422);
        }
        $capacity = self::get_capacity($product_id, $variation_id, $var_data);
        $slots = array();
        for ($hour = $work_start; $hour < $work_end; $hour++) {
            $reserved = self::reservations_for_hour($product_id, $variation_id, $date, $hour, $exclude);
            if (is_wp_error($reserved)) {
                return $reserved;
            }
            $available = max(0, $capacity - (int) $reserved);
            $slots[] = array(
                'hour' => (int) $hour,
                'time' => sprintf('%02d:00', $hour),
                'capacity' => (int) $capacity,
                'reserved' => (int) $reserved,
                'available' => $available,
                'bookable' => $available > 0 && !self::booking_disabled($product),
            );
        }
        $availability_data = array(
            'product_id' => (int) $product_id,
            'variation_id' => (int) $variation_id,
            'date' => $date,
            'date_iso' => $date,
            'system_numbers' => isset($variant['system_numbers']) ? $variant['system_numbers'] : array(),
            'games' => isset($variant['games']) ? $variant['games'] : array(),
            'capacity' => (int) $capacity,
            'work_start' => (int) $work_start,
            'work_end' => (int) $work_end,
            'slots' => $slots,
            'hours' => $slots,
        );

        // Compatibility: the Windows client reads `Availability`; retain the
        // root-level fields for any older client that expects them there.
        return self::ok(array_merge(
            $availability_data,
            array('availability' => $availability_data)
        ));
    }
}

LBXOP_API::boot();
}
