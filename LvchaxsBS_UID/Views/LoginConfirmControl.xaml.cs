using LvchaxsBS_UID.Models;
using LvchaxsBS_UID.Utils.File;
using LvchaxsBS_UID.Services.DataServices;
using LvchaxsBS_UID.Utils.Clipboard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS_UID.Views
{
    public partial class LoginConfirmWindow : Window
    {
        #region 常量定义
        private const string CHECKBOX_SETTINGS_FILE = "login_checkbox_settings.json";
        #endregion

        #region 事件定义
        public event EventHandler<LoginConfirmEventArgs> ConfirmLogin;
        public event EventHandler CancelLogin;
        public event EventHandler CloseRequested;
        public event EventHandler<bool> MarkPreferenceChanged;
        public event EventHandler<string> UidCopyRequested;
        public event EventHandler LoginNextRequested;
        public event EventHandler LoginPrevRequested;

        /// <summary>点了置底控件（在分组内置底 / 取消置底之间切换）。</summary>
        public event EventHandler MarkToggleRequested;
        #endregion

        #region 依赖属性
        public static readonly DependencyProperty IsConfirmEnabledProperty =
            DependencyProperty.Register(nameof(IsConfirmEnabled), typeof(bool), typeof(LoginConfirmWindow),
                // ★ 默认值必须是 true：WPF 的依赖属性"设成和当前值相同"时不触发回调，
                //   若默认 false，新建窗口时设 false（置底账号禁登）就不会走 OnIsConfirmEnabledChanged，
                //   按钮将保持 XAML 默认的可点状态。默认 true 时，设 false 一定有变化、一定置灰。
                new PropertyMetadata(true, OnIsConfirmEnabledChanged));

        public bool IsConfirmEnabled
        {
            get => (bool)GetValue(IsConfirmEnabledProperty);
            set => SetValue(IsConfirmEnabledProperty, value);
        }

        /// <summary>
        /// 取宿主的 DynamicResource 主题画刷。插件编译期引用不到宿主的 ResourceDictionary，
        /// 只能运行时从 Application.Resources 里查；查不到时退回 fallback，
        /// 免得主题缺一个键就把整个窗口打崩。
        /// </summary>
        private Brush ThemeBrush(string key, Brush fallback)
        {
            try
            {
                return (Application.Current?.TryFindResource(key) ?? TryFindResource(key)) as Brush ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private Brush SuccessTextBrush => ThemeBrush("SuccessBrush", Brushes.Green);
        private Brush DangerTextBrush => ThemeBrush("DangerBrush", Brushes.Red);

        private static void OnIsConfirmEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 背景色不要动 —— 按钮用的是宿主默认的 SettingsButtonStyleRound6（中性底色），
            // 改 Background 会把那套配色顶掉。
            // 但宿主样式没有定义「禁用」时的外观，只设 IsEnabled 视觉上没变化，
            // 所以用透明度表达禁用态：任何主题下都一致，也不引入固定颜色。
            if (d is LoginConfirmWindow control)
            {
                bool enabled = (bool)e.NewValue;
                control.ConfirmLoginButton.IsEnabled = enabled;
                control.ConfirmLoginButton.Opacity = enabled ? 1.0 : 0.45;
            }
        }

        public static readonly DependencyProperty ConfirmButtonTextProperty =
            DependencyProperty.Register(nameof(ConfirmButtonText), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata("确认登录", OnConfirmButtonTextChanged));

        public string ConfirmButtonText
        {
            get => (string)GetValue(ConfirmButtonTextProperty);
            set => SetValue(ConfirmButtonTextProperty, value);
        }

        private static void OnConfirmButtonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.ConfirmLoginButton.Content = e.NewValue?.ToString() ?? "确认登录";
            }
        }

        public static readonly DependencyProperty CurrentAccountNameProperty =
            DependencyProperty.Register(nameof(CurrentAccountName), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata(string.Empty, OnCurrentAccountNameChanged));

        public string CurrentAccountName
        {
            get => (string)GetValue(CurrentAccountNameProperty);
            set => SetValue(CurrentAccountNameProperty, value);
        }

        private static void OnCurrentAccountNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control && control.CurrentAccountNameText != null)
            {
                control.CurrentAccountNameText.Text = e.NewValue?.ToString() ?? string.Empty;
                control.UpdateCurrentAccountVisibility();
            }
        }

        public static readonly DependencyProperty AccountUidProperty =
            DependencyProperty.Register(nameof(AccountUid), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata(string.Empty, OnAccountUidChanged));

        public string AccountUid
        {
            get => (string)GetValue(AccountUidProperty);
            set => SetValue(AccountUidProperty, value);
        }

        private static void OnAccountUidChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                string uid = e.NewValue?.ToString() ?? string.Empty;
                if (control.AccountUidText != null)
                    control.AccountUidText.Text = uid;

                if (control.CurrentAccountSeparator != null)
                {
                    bool hasName = !string.IsNullOrEmpty(control.CurrentAccountName);
                    bool hasUid = !string.IsNullOrEmpty(uid);
                    control.CurrentAccountSeparator.Visibility = (hasName && hasUid) ? Visibility.Visible : Visibility.Collapsed;
                }

                if (control.CopyUidButton != null)
                    control.CopyUidButton.Visibility = string.IsNullOrEmpty(uid) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public static readonly DependencyProperty PrevAccountNameProperty =
            DependencyProperty.Register(nameof(PrevAccountName), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata(string.Empty, OnPrevAccountNameChanged));

        public string PrevAccountName
        {
            get => (string)GetValue(PrevAccountNameProperty);
            set => SetValue(PrevAccountNameProperty, value);
        }

        private static void OnPrevAccountNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.PrevAccountNameText.Text = e.NewValue?.ToString() ?? string.Empty;
                control.UpdatePrevAccountVisibility();
            }
        }

        public static readonly DependencyProperty PrevAccountUidProperty =
            DependencyProperty.Register(nameof(PrevAccountUid), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata(string.Empty, OnPrevAccountUidChanged));

        public string PrevAccountUid
        {
            get => (string)GetValue(PrevAccountUidProperty);
            set => SetValue(PrevAccountUidProperty, value);
        }

        private static void OnPrevAccountUidChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.PrevAccountUidText.Text = e.NewValue?.ToString() ?? string.Empty;
                control.UpdatePrevAccountVisibility();
            }
        }

        public static readonly DependencyProperty NextAccountNameProperty =
            DependencyProperty.Register(nameof(NextAccountName), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata(string.Empty, OnNextAccountNameChanged));

        public string NextAccountName
        {
            get => (string)GetValue(NextAccountNameProperty);
            set => SetValue(NextAccountNameProperty, value);
        }

        private static void OnNextAccountNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.NextAccountNameText.Text = e.NewValue?.ToString() ?? string.Empty;
                control.UpdateNextAccountVisibility();
            }
        }

        public static readonly DependencyProperty NextAccountUidProperty =
            DependencyProperty.Register(nameof(NextAccountUid), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata(string.Empty, OnNextAccountUidChanged));

        public string NextAccountUid
        {
            get => (string)GetValue(NextAccountUidProperty);
            set => SetValue(NextAccountUidProperty, value);
        }

        private static void OnNextAccountUidChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.NextAccountUidText.Text = e.NewValue?.ToString() ?? string.Empty;
                control.UpdateNextAccountVisibility();
            }
        }

        public static readonly DependencyProperty ShowMarkCheckBoxProperty =
            DependencyProperty.Register(nameof(ShowMarkCheckBox), typeof(bool), typeof(LoginConfirmWindow),
                new PropertyMetadata(false, OnShowMarkCheckBoxChanged));

        public bool ShowMarkCheckBox
        {
            get => (bool)GetValue(ShowMarkCheckBoxProperty);
            set => SetValue(ShowMarkCheckBoxProperty, value);
        }

        private static void OnShowMarkCheckBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.MarkAfterLoginCheckBox.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public static readonly DependencyProperty IsMarkCheckedProperty =
            DependencyProperty.Register(nameof(IsMarkChecked), typeof(bool), typeof(LoginConfirmWindow),
                new PropertyMetadata(false, OnIsMarkCheckedChanged));

        public bool IsMarkChecked
        {
            get => (bool)GetValue(IsMarkCheckedProperty);
            set => SetValue(IsMarkCheckedProperty, value);
        }

        private static void OnIsMarkCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.MarkAfterLoginCheckBox.IsChecked = (bool)e.NewValue;
            }
        }

        public static readonly DependencyProperty IsMarkCheckBoxEnabledProperty =
            DependencyProperty.Register(nameof(IsMarkCheckBoxEnabled), typeof(bool), typeof(LoginConfirmWindow),
                new PropertyMetadata(true, OnIsMarkCheckBoxEnabledChanged));

        public bool IsMarkCheckBoxEnabled
        {
            get => (bool)GetValue(IsMarkCheckBoxEnabledProperty);
            set => SetValue(IsMarkCheckBoxEnabledProperty, value);
        }

        private static void OnIsMarkCheckBoxEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.MarkAfterLoginCheckBox.IsEnabled = (bool)e.NewValue;
            }
        }

        public static readonly DependencyProperty TitleTextProperty =
            DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata("确认登录", OnTitleTextChanged));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        private static void OnTitleTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.LoginModalTitle.Text = e.NewValue?.ToString() ?? "确认登录";
            }
        }

        public static readonly DependencyProperty TitleColorProperty =
            DependencyProperty.Register(nameof(TitleColor), typeof(Brush), typeof(LoginConfirmWindow),
                new PropertyMetadata(new SolidColorBrush(Colors.Black), OnTitleColorChanged));

        public Brush TitleColor
        {
            get => (Brush)GetValue(TitleColorProperty);
            set => SetValue(TitleColorProperty, value);
        }

        private static void OnTitleColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control && e.NewValue is Brush brush)
            {
                control.LoginModalTitle.Foreground = brush;
            }
        }

        public static readonly DependencyProperty IsCloseEnabledProperty =
            DependencyProperty.Register(nameof(IsCloseEnabled), typeof(bool), typeof(LoginConfirmWindow),
                new PropertyMetadata(true, OnIsCloseEnabledChanged));

        public bool IsCloseEnabled
        {
            get => (bool)GetValue(IsCloseEnabledProperty);
            set => SetValue(IsCloseEnabledProperty, value);
        }

        private static void OnIsCloseEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.CloseLoginModalButton.IsEnabled = (bool)e.NewValue;
            }
        }

        public static readonly DependencyProperty StatusHintTextProperty =
            DependencyProperty.Register(nameof(StatusHintText), typeof(string), typeof(LoginConfirmWindow),
                new PropertyMetadata("测试阶段", OnStatusHintTextChanged));

        public string StatusHintText
        {
            get => (string)GetValue(StatusHintTextProperty);
            set => SetValue(StatusHintTextProperty, value);
        }

        private static void OnStatusHintTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.LoginStatusHint.Text = e.NewValue?.ToString() ?? "测试阶段";
            }
        }

        public static readonly DependencyProperty IsAccountMarkedProperty =
            DependencyProperty.Register(nameof(IsAccountMarked), typeof(bool), typeof(LoginConfirmWindow),
                new PropertyMetadata(false, OnIsAccountMarkedChanged));

        /// <summary>当前账号在分组里是否已置底（驱动置底控件的文字与提示）。</summary>
        public bool IsAccountMarked
        {
            get => (bool)GetValue(IsAccountMarkedProperty);
            set => SetValue(IsAccountMarkedProperty, value);
        }

        private static void OnIsAccountMarkedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                bool marked = (bool)e.NewValue;
                control.MarkToggleText.Text = marked ? "取消置底" : "置底账号";
                control.MarkToggleButton.ToolTip = marked
                    ? "账号已置底无法登录，取消置底可恢复"
                    : "置底后该账号在分组内无法登录，取消置底可恢复";
            }
        }

        public static readonly DependencyProperty ShowMarkToggleProperty =
            DependencyProperty.Register(nameof(ShowMarkToggle), typeof(bool), typeof(LoginConfirmWindow),
                new PropertyMetadata(false, OnShowMarkToggleChanged));

        /// <summary>是否显示置底控件（仅分组内显示，主列表没有"置底"这个概念）。</summary>
        public bool ShowMarkToggle
        {
            get => (bool)GetValue(ShowMarkToggleProperty);
            set => SetValue(ShowMarkToggleProperty, value);
        }

        private static void OnShowMarkToggleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoginConfirmWindow control)
            {
                control.MarkToggleButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        #endregion

        private bool _isClosing = false;

        public LoginConfirmWindow()
        {
            InitializeComponent();
            // 独立窗口不会继承主界面的「窗口缩放」，自己跟一下（宿主注入的系数）
            Utils.UI.UiScale.ApplyTo(this);
            this.DataContext = this;
            this.Loaded += Window_Loaded;

            // 订阅复选框事件
            SubscribeCheckBoxEvents();

            // 加载保存的复选框状态
            LoadCheckBoxStates();

            // 初始化复选框联动状态
            UpdateCheckBoxesState();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlayAnimation("FadeInAnimation", () =>
            {
                mainBorder.Opacity = 1;
                var transform = mainBorder.RenderTransform as ScaleTransform;
                if (transform != null)
                {
                    transform.ScaleX = 1;
                    transform.ScaleY = 1;
                }
            });
        }

        private void PlayAnimation(string animationKey, Action onCompleted = null)
        {
            if (FindResource(animationKey) is Storyboard animation)
            {
                if (onCompleted != null)
                    animation.Completed += (s, args) => onCompleted();
                animation.Begin(mainBorder);
            }
        }

        private void CloseWithAnimation(bool dialogResult = false)
        {
            if (_isClosing) return;

            bool canSetDialogResult = this.Owner != null && this.IsVisible && this.IsLoaded;

            if (!canSetDialogResult)
            {
                Close();
                return;
            }

            _isClosing = true;

            PlayAnimation("FadeOutAnimation", () =>
            {
                try
                {
                    DialogResult = dialogResult;
                }
                catch (InvalidOperationException)
                {
                    // 忽略异常，直接关闭
                }
                finally
                {
                    Close();
                }
            });
        }

        #region 复选框状态管理
        private void SubscribeCheckBoxEvents()
        {
            if (ExtraCheckBox1 != null)
            {
                ExtraCheckBox1.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox1.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox2 != null)
            {
                ExtraCheckBox2.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox2.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox3 != null)
            {
                ExtraCheckBox3.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox3.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox4 != null)
            {
                ExtraCheckBox4.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox4.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox5 != null)
            {
                ExtraCheckBox5.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox5.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox6 != null)
            {
                ExtraCheckBox6.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox6.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox7 != null)
            {
                ExtraCheckBox7.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox7.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox8 != null)
            {
                ExtraCheckBox8.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox8.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox9 != null)
            {
                ExtraCheckBox9.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox9.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox10 != null)
            {
                ExtraCheckBox10.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox10.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox11 != null)
            {
                ExtraCheckBox11.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox11.Unchecked += ExtraCheckBox_Unchecked;
            }
            if (ExtraCheckBox12 != null)
            {
                ExtraCheckBox12.Checked += ExtraCheckBox_Checked;
                ExtraCheckBox12.Unchecked += ExtraCheckBox_Unchecked;
            }
        }

        /// <summary>
        /// 更新相关复选框的启用状态
        /// </summary>
        private void UpdateCheckBoxesState()
        {
            bool isChatModeSelected = ExtraCheckBox2?.IsChecked == true;
            bool isMultiplayerModeSelected = ExtraCheckBox3?.IsChecked == true;

            // 联机人数复选框（4,5,6）和指令权限复选框（7,8,9）与聊天界面联动
            if (ExtraCheckBox4 != null) ExtraCheckBox4.IsEnabled = isChatModeSelected;
            if (ExtraCheckBox5 != null) ExtraCheckBox5.IsEnabled = isChatModeSelected;
            if (ExtraCheckBox6 != null) ExtraCheckBox6.IsEnabled = isChatModeSelected;
            if (ExtraCheckBox7 != null) ExtraCheckBox7.IsEnabled = isChatModeSelected;
            if (ExtraCheckBox8 != null) ExtraCheckBox8.IsEnabled = isChatModeSelected;
            if (ExtraCheckBox9 != null) ExtraCheckBox9.IsEnabled = isChatModeSelected;

            // 加入方式复选框（10,11,12）与联机界面联动
            if (ExtraCheckBox10 != null) ExtraCheckBox10.IsEnabled = isMultiplayerModeSelected;
            if (ExtraCheckBox11 != null) ExtraCheckBox11.IsEnabled = isMultiplayerModeSelected;
            if (ExtraCheckBox12 != null) ExtraCheckBox12.IsEnabled = isMultiplayerModeSelected;
        }

        /// <summary>
        /// 更新联机人数复选框（4,5,6）的互斥逻辑
        /// </summary>
        private void UpdatePlayerCountCheckBoxes(CheckBox checkedBox)
        {
            int targetCount = 4; // 默认4人

            if (checkedBox == ExtraCheckBox4) // 四人联机
            {
                if (ExtraCheckBox5 != null) ExtraCheckBox5.IsChecked = false;
                if (ExtraCheckBox6 != null) ExtraCheckBox6.IsChecked = false;
                targetCount = 4;
            }
            else if (checkedBox == ExtraCheckBox5) // 三人联机
            {
                if (ExtraCheckBox4 != null) ExtraCheckBox4.IsChecked = false;
                if (ExtraCheckBox6 != null) ExtraCheckBox6.IsChecked = false;
                targetCount = 3;
            }
            else if (checkedBox == ExtraCheckBox6) // 双人联机
            {
                if (ExtraCheckBox4 != null) ExtraCheckBox4.IsChecked = false;
                if (ExtraCheckBox5 != null) ExtraCheckBox5.IsChecked = false;
                targetCount = 2;
            }

        }

        /// <summary>
        /// 更新指令权限复选框（7,8,9）的逻辑 - 至少要选一个
        /// </summary>
        private void UpdateCommandPermissionCheckBoxes(CheckBox uncheckedBox)
        {
            // 检查是否所有权限复选框都未选中
            bool allUnchecked = (ExtraCheckBox7?.IsChecked == false || ExtraCheckBox7 == null) &&
                                 (ExtraCheckBox8?.IsChecked == false || ExtraCheckBox8 == null) &&
                                 (ExtraCheckBox9?.IsChecked == false || ExtraCheckBox9 == null);

            // 如果全部未选中，则不能取消当前这个（即恢复它的选中状态）
            if (allUnchecked && uncheckedBox != null)
            {
                uncheckedBox.IsChecked = true;
                Debug.WriteLine("[复选框] 指令权限至少要选一个，已恢复选中");
            }
        }

        /// <summary>
        /// 更新加入方式复选框（10,11,12）的互斥逻辑
        /// </summary>
        private void UpdateJoinMethodCheckBoxes(CheckBox checkedBox)
        {
            if (checkedBox == ExtraCheckBox10) // 不允许加入
            {
                if (ExtraCheckBox11 != null) ExtraCheckBox11.IsChecked = false;
                if (ExtraCheckBox12 != null) ExtraCheckBox12.IsChecked = false;
            }
            else if (checkedBox == ExtraCheckBox11) // 直接加入
            {
                if (ExtraCheckBox10 != null) ExtraCheckBox10.IsChecked = false;
                if (ExtraCheckBox12 != null) ExtraCheckBox12.IsChecked = false;
            }
            else if (checkedBox == ExtraCheckBox12) // 确认后可加入
            {
                if (ExtraCheckBox10 != null) ExtraCheckBox10.IsChecked = false;
                if (ExtraCheckBox11 != null) ExtraCheckBox11.IsChecked = false;
            }
        }

        private void SaveCheckBoxStates()
        {
            try
            {
                var settings = new Dictionary<string, bool>
                {
                    ["ExtraCheckBox1"] = ExtraCheckBox1?.IsChecked ?? false,
                    ["ExtraCheckBox2"] = ExtraCheckBox2?.IsChecked ?? false,
                    ["ExtraCheckBox3"] = ExtraCheckBox3?.IsChecked ?? false,
                    ["ExtraCheckBox4"] = ExtraCheckBox4?.IsChecked ?? false,
                    ["ExtraCheckBox5"] = ExtraCheckBox5?.IsChecked ?? false,
                    ["ExtraCheckBox6"] = ExtraCheckBox6?.IsChecked ?? false,
                    ["ExtraCheckBox7"] = ExtraCheckBox7?.IsChecked ?? false,
                    ["ExtraCheckBox8"] = ExtraCheckBox8?.IsChecked ?? false,
                    ["ExtraCheckBox9"] = ExtraCheckBox9?.IsChecked ?? false,
                    ["ExtraCheckBox10"] = ExtraCheckBox10?.IsChecked ?? false,
                    ["ExtraCheckBox11"] = ExtraCheckBox11?.IsChecked ?? false,
                    ["ExtraCheckBox12"] = ExtraCheckBox12?.IsChecked ?? false
                };

                string json = System.Text.Json.JsonSerializer.Serialize(settings);
                string filePath = AppPaths.ConfigFile(CHECKBOX_SETTINGS_FILE);
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存复选框状态失败: {ex.Message}");
            }
        }

        private void LoadCheckBoxStates()
        {
            try
            {
                string filePath = AppPaths.ConfigFile(CHECKBOX_SETTINGS_FILE);

                if (System.IO.File.Exists(filePath))
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    using var document = System.Text.Json.JsonDocument.Parse(json);
                    var root = document.RootElement;

                    if (root.TryGetProperty("ExtraCheckBox1", out var check1) && ExtraCheckBox1 != null)
                        ExtraCheckBox1.IsChecked = check1.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox2", out var check2) && ExtraCheckBox2 != null)
                        ExtraCheckBox2.IsChecked = check2.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox3", out var check3) && ExtraCheckBox3 != null)
                        ExtraCheckBox3.IsChecked = check3.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox4", out var check4) && ExtraCheckBox4 != null)
                        ExtraCheckBox4.IsChecked = check4.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox5", out var check5) && ExtraCheckBox5 != null)
                        ExtraCheckBox5.IsChecked = check5.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox6", out var check6) && ExtraCheckBox6 != null)
                        ExtraCheckBox6.IsChecked = check6.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox7", out var check7) && ExtraCheckBox7 != null)
                        ExtraCheckBox7.IsChecked = check7.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox8", out var check8) && ExtraCheckBox8 != null)
                        ExtraCheckBox8.IsChecked = check8.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox9", out var check9) && ExtraCheckBox9 != null)
                        ExtraCheckBox9.IsChecked = check9.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox10", out var check10) && ExtraCheckBox10 != null)
                        ExtraCheckBox10.IsChecked = check10.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox11", out var check11) && ExtraCheckBox11 != null)
                        ExtraCheckBox11.IsChecked = check11.GetBoolean();
                    if (root.TryGetProperty("ExtraCheckBox12", out var check12) && ExtraCheckBox12 != null)
                        ExtraCheckBox12.IsChecked = check12.GetBoolean();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载复选框状态失败: {ex.Message}");
            }
        }

        private void ExtraCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkedBox = sender as CheckBox;

            if (checkedBox == ExtraCheckBox1) // 登录后打开派蒙界面
            {
                ExtraCheckBox2.IsChecked = false;
                ExtraCheckBox3.IsChecked = false;

                Debug.WriteLine($"[复选框] 登录后打开派蒙界面 - 已勾选");

                UpdateCheckBoxesState();
            }
            else if (checkedBox == ExtraCheckBox2) // 登录后打开聊天界面
            {
                ExtraCheckBox1.IsChecked = false;
                ExtraCheckBox3.IsChecked = false;

                Debug.WriteLine($"[复选框] 登录后打开聊天界面 - 已勾选");

                UpdateCheckBoxesState();
            }
            else if (checkedBox == ExtraCheckBox3) // 登录后打开联机界面
            {
                ExtraCheckBox1.IsChecked = false;
                ExtraCheckBox2.IsChecked = false;

                Debug.WriteLine($"[复选框] 登录后打开联机界面 - 已勾选");

                UpdateCheckBoxesState();
            }
            else if (checkedBox == ExtraCheckBox4 || checkedBox == ExtraCheckBox5 || checkedBox == ExtraCheckBox6)
            {
                // 联机人数互斥逻辑
                UpdatePlayerCountCheckBoxes(checkedBox);
            }
            else if (checkedBox == ExtraCheckBox7 || checkedBox == ExtraCheckBox8 || checkedBox == ExtraCheckBox9)
            {
                // 指令权限，不需要额外处理
            }
            else if (checkedBox == ExtraCheckBox10 || checkedBox == ExtraCheckBox11 || checkedBox == ExtraCheckBox12)
            {
                // 加入方式互斥逻辑
                UpdateJoinMethodCheckBoxes(checkedBox);
            }

            SaveCheckBoxStates();
        }

        private void ExtraCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var uncheckedBox = sender as CheckBox;

            if (uncheckedBox == ExtraCheckBox1)
            {
                Debug.WriteLine($"[复选框] 登录后打开派蒙界面 - 已取消");
            }
            else if (uncheckedBox == ExtraCheckBox2)
            {
                Debug.WriteLine($"[复选框] 登录后打开聊天界面 - 已取消");

                UpdateCheckBoxesState();
            }
            else if (uncheckedBox == ExtraCheckBox3)
            {
                Debug.WriteLine($"[复选框] 登录后打开联机界面 - 已取消");

                UpdateCheckBoxesState();
            }
            else if (uncheckedBox == ExtraCheckBox7 || uncheckedBox == ExtraCheckBox8 || uncheckedBox == ExtraCheckBox9)
            {
                // 指令权限至少要选一个
                UpdateCommandPermissionCheckBoxes(uncheckedBox);
            }

            SaveCheckBoxStates();
        }
        #endregion

        #region 公共方法
        public void ResetUIState()
        {
            TitleText = "确认登录";
            TitleColor = ThemeBrush("TextPrimaryBrush", Brushes.Black);
            ConfirmButtonText = "确认登录";
            IsConfirmEnabled = false;
            IsCloseEnabled = true;
            IsMarkCheckBoxEnabled = true;
            CurrentAccountName = string.Empty;
            AccountUid = string.Empty;
            StatusHintText = "测试阶段";
        }

        public void SetLoggingInState()
        {
            TitleText = "登录中...";
            ConfirmButtonText = "停止登录";
            IsCloseEnabled = false;
            IsMarkCheckBoxEnabled = false;
            IsConfirmEnabled = true;
            StatusHintText = "正在登录中...";
        }

        public void SetLoginCompletedState(bool success)
        {
            if (success)
            {
                TitleText = "登录完成";
                TitleColor = SuccessTextBrush;
                // ★ 完成后按钮恢复成「确认登录」而不是「登录完成」：
                //   留着"登录完成"会让用户以为这是个新状态，点下去又重新跑一遍登录。
                ConfirmButtonText = "确认登录";
                IsConfirmEnabled = true;
                IsCloseEnabled = true;
                IsMarkCheckBoxEnabled = true;
                StatusHintText = "登录成功";
            }
            else
            {
                TitleText = "登录失败";
                TitleColor = DangerTextBrush;
                ConfirmButtonText = "确认登录";
                IsConfirmEnabled = true;
                IsCloseEnabled = true;
                IsMarkCheckBoxEnabled = true;
                StatusHintText = "登录失败，请重试";
            }
        }

        public void SetLoginCancelledState()
        {
            TitleText = "登录已取消";
            TitleColor = DangerTextBrush;
            ConfirmButtonText = "确认登录";
            IsConfirmEnabled = true;
            IsCloseEnabled = true;
            IsMarkCheckBoxEnabled = true;
            StatusHintText = "登录已取消";
        }

        public bool GetIsMarkChecked()
        {
            return MarkAfterLoginCheckBox.IsChecked == true;
        }

        private void UpdateCurrentAccountVisibility()
        {
            bool hasName = !string.IsNullOrEmpty(CurrentAccountName);
            bool hasUid = !string.IsNullOrEmpty(AccountUid);

            if (CurrentAccountSeparator != null)
                CurrentAccountSeparator.Visibility = (hasName && hasUid) ? Visibility.Visible : Visibility.Collapsed;

            if (CopyUidButton != null)
                CopyUidButton.Visibility = hasUid ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePrevAccountVisibility()
        {
            bool hasPrevAccount = !string.IsNullOrEmpty(PrevAccountName) || !string.IsNullOrEmpty(PrevAccountUid);

            PrevAccountBorder.Visibility = Visibility.Visible;

            if (hasPrevAccount)
            {
                PrevAccountNameText.Text = PrevAccountName;
                PrevAccountUidText.Text = PrevAccountUid;

                PrevAccountSeparator.Visibility = (!string.IsNullOrEmpty(PrevAccountName) && !string.IsNullOrEmpty(PrevAccountUid))
                    ? Visibility.Visible : Visibility.Collapsed;

                CopyPrevUidButton.Visibility = !string.IsNullOrEmpty(PrevAccountUid) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                PrevAccountNameText.Text = "无";
                PrevAccountUidText.Text = "";

                PrevAccountSeparator.Visibility = Visibility.Collapsed;
                CopyPrevUidButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateNextAccountVisibility()
        {
            bool hasNextAccount = !string.IsNullOrEmpty(NextAccountName) || !string.IsNullOrEmpty(NextAccountUid);

            NextAccountBorder.Visibility = Visibility.Visible;

            if (hasNextAccount)
            {
                NextAccountNameText.Text = NextAccountName;
                NextAccountUidText.Text = NextAccountUid;

                NextAccountSeparator.Visibility = (!string.IsNullOrEmpty(NextAccountName) && !string.IsNullOrEmpty(NextAccountUid))
                    ? Visibility.Visible : Visibility.Collapsed;

                CopyNextUidButton.Visibility = !string.IsNullOrEmpty(NextAccountUid) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                NextAccountNameText.Text = "无";
                NextAccountUidText.Text = "";

                NextAccountSeparator.Visibility = Visibility.Collapsed;
                CopyNextUidButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CopyUidToClipboard()
        {
            if (!string.IsNullOrEmpty(AccountUid))
            {
                try
                {
                    NativeClipboardHelper.SafeSetText(AccountUid);
                    UidCopyRequested?.Invoke(this, AccountUid);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"复制 UID 失败: {ex.Message}");
                }
            }
        }

        private void CopyPrevUidToClipboard()
        {
            if (!string.IsNullOrEmpty(PrevAccountUid))
            {
                try
                {
                    NativeClipboardHelper.SafeSetText(PrevAccountUid);
                    UidCopyRequested?.Invoke(this, PrevAccountUid);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"复制上一个账号 UID 失败: {ex.Message}");
                }
            }
        }

        private void CopyNextUidToClipboard()
        {
            if (!string.IsNullOrEmpty(NextAccountUid))
            {
                try
                {
                    NativeClipboardHelper.SafeSetText(NextAccountUid);
                    UidCopyRequested?.Invoke(this, NextAccountUid);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"复制下一个账号 UID 失败: {ex.Message}");
                }
            }
        }
        #endregion

        #region 事件处理方法
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmLogin?.Invoke(this, new LoginConfirmEventArgs
            {
                IsMarkChecked = MarkAfterLoginCheckBox.IsChecked == true,
                Action = ConfirmButtonText
            });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            CloseWithAnimation(false);
        }

        private void MarkAfterLoginCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            MarkPreferenceChanged?.Invoke(this, true);
        }

        private void MarkAfterLoginCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            MarkPreferenceChanged?.Invoke(this, false);
        }

        private void AccountUidText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CopyUidToClipboard();
        }

        private void CopyUidButton_Click(object sender, RoutedEventArgs e)
        {
            CopyUidToClipboard();
        }

        private void LoginPrevButton_Click(object sender, RoutedEventArgs e)
        {
            LoginPrevRequested?.Invoke(this, EventArgs.Empty);
        }

        private void LoginNextButton_Click(object sender, RoutedEventArgs e)
        {
            LoginNextRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>置底控件：在分组内置底 / 取消置底之间切换（实际动作由宿主做）。</summary>
        private void MarkToggleButton_Click(object sender, RoutedEventArgs e)
        {
            MarkToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void PrevAccountUidText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CopyPrevUidToClipboard();
        }

        private void CopyPrevUidButton_Click(object sender, RoutedEventArgs e)
        {
            CopyPrevUidToClipboard();
        }

        private void NextAccountUidText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CopyNextUidToClipboard();
        }

        private void CopyNextUidButton_Click(object sender, RoutedEventArgs e)
        {
            CopyNextUidToClipboard();
        }
        #endregion
    }

    public class LoginConfirmEventArgs : EventArgs
    {
        public bool IsMarkChecked { get; set; }
        public string Action { get; set; }
    }
}