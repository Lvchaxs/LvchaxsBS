using LvchaxsBS_UID.Models;
using LvchaxsBS_UID.Services.DataServices;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LvchaxsBS_UID.Views
{
    public partial class AddEditAccountWindow : Window
    {
        public Account Account { get; private set; }
        public bool IsEditMode { get; private set; }
        private bool _isClosing = false;
        private bool _isPasswordVisible = false;

        /// <summary>全部可选头像（未过滤），来源是资源包里的角色图 + 其他头像。</summary>
        private readonly List<AvatarItem> _allAvatarItems = new();

        /// <summary>按中文拼音/笔画排序的比较器（头像名排序用）。</summary>
        private static readonly StringComparer PinyinComparer =
            StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), true);

        /// <summary>当前选中的筛选；<c>null</c> 表示不筛选（也就是「全部」）；<c>OtherFilterKey</c> 表示只看其他头像。单选。</summary>
        private string? _selectedElement;

        /// <summary>「其他头像」（NPC / 任务类）筛选用的伪元素名 —— 第 9 个按钮的 Tag + 它的勾选状态靠它区分。</summary>
        private const string OtherFilterKey = "其他";

        /// <summary>八个筛选按钮（Tag = 元素名，或 <see cref="OtherFilterKey"/>），「全部」按钮单独持有。</summary>
        private List<ToggleButton> _elementToggles = new();

        public delegate void ShowToastDelegate(string title, string message, bool isSuccess = true);
        public event ShowToastDelegate ShowToastRequested;

        public AddEditAccountWindow(Account account = null)
        {
            InitializeComponent();
            // 独立窗口不会继承主界面的「窗口缩放」，自己跟一下（宿主注入的系数）
            Utils.UI.UiScale.ApplyTo(this);

            _elementToggles = new List<ToggleButton>
            {
                filterFireToggle, filterWaterToggle, filterWindToggle, filterThunderToggle,
                filterGrassToggle, filterIceToggle, filterRockToggle, filterOtherToggle
            };

            if (account == null)
            {
                // 创建新账号
                Account = new Account();
                Account.CreatedTime = DateTime.Now;
            }
            else
            {
                // 编辑现有账号
                Account = account;
            }

            IsEditMode = account != null;

            InitializeWindow();
            LoadAvatarItems();
            UpdateWatermarks();
            UpdatePasswordIcon();

            this.Loaded += Window_Loaded;
        }

        private void InitializeWindow()
        {
            windowTitle.Text = IsEditMode ? "编辑账号" : "添加账号";

            if (IsEditMode)
            {
                uidTextBox.Text = Account.Uid;
                levelTextBox.Text = Account.Level.ToString();
                nicknameTextBox.Text = Account.Nickname;
                usernameTextBox.Text = Account.Username;
                passwordBox.Password = Account.Password;
                passwordTextBox.Text = Account.Password;
            }

            SetPasswordVisibility(false);
            UpdatePasswordStrength(IsEditMode ? Account.Password : "");
        }

        /// <summary>
        /// 填充右侧头像列表：可选头像 = 资源包
        /// <c>Resources/Images/Pictures/角色头像</c> 里的角色图。
        ///
        /// <para>
        /// 顺序：**旅行者（空 / 荧）固定在最前**（001、002 正序），
        /// 其余按角色编号 <b>倒序</b> —— 也就是最新上线的排前面
        /// （沃雅妮莎 122 → …），最后才是 1.0 的老角色。
        /// 没编号的角色（将来新增但还没登记）排在最后，再按中文排序兜底。
        /// <b>「其他头像」（NPC / 任务类）没有编号，统一排在所有角色之后</b>，按中文名排序。
        /// 默认选中当前账号已选的头像，新建账号则选中「空」。
        /// 资源包缺失时列表为空 —— 保存时会回落到默认头像，不会报错。
        /// </para>
        /// </summary>
        private void LoadAvatarItems()
        {
            _allAvatarItems.Clear();

            var names = Utils.UI.AssetLocator.ListFileNames("Images/Pictures/角色头像");

            // 排序键：主角组(0) 在最前且按编号正序 → 其余组(1) 按编号倒序（最新在前）。
            // 取不到编号的用一个大正数，自然落到最后。
            int SortKey(string n)
            {
                int order = Models.CharacterElements.OrderOf(n);
                if (order == int.MaxValue) return 1_000_000;
                return Models.CharacterElements.IsTraveler(n) ? order : -order;
            }

            var ordered = names
                .OrderBy(n => Models.CharacterElements.IsTraveler(n) ? 0 : 1)
                .ThenBy(SortKey)
                .ThenBy(n => n, PinyinComparer)
                .ToList();

            foreach (string name in ordered)
            {
                _allAvatarItems.Add(new AvatarItem
                {
                    Name = name,
                    // 列表里一次铺开一百多张，按 64px 解码就够（显示 48px）
                    Image = Utils.UI.AssetLocator.Load($"Images/Pictures/角色头像/{name}.png", 64)
                });
            }

            // 「其他头像」（NPC / 任务类）：编号是独立的一套，按它**倒序**排
            // （最新实装的在前，和角色一致）；没登记的（编号 0）排最后，再按中文名兜底。
            int OtherKey(string n)
            {
                int order = Models.CharacterElements.OtherOrderOf(n);
                return order == 0 ? int.MinValue : -order;
            }

            var others = Utils.UI.AssetLocator.ListFileNames("Images/Pictures/其他头像")
                .OrderBy(OtherKey)
                .ThenBy(n => n, PinyinComparer)
                .ToList();

            foreach (string name in others)
            {
                _allAvatarItems.Add(new AvatarItem
                {
                    Name = name,
                    IsOther = true,
                    Image = Utils.UI.AssetLocator.Load($"Images/Pictures/其他头像/{name}.png", 64)
                });
            }

            // 默认「全部」：清空元素筛选再刷新
            _selectedElement = null;
            SyncFilterToggles();
            ApplyAvatarFilter();

            // 预选：账号已选的头像 → 否则默认头像「空」→ 否则第一个
            var target = _allAvatarItems.FirstOrDefault(i => i.Name == this.Account.Avatar)
                         ?? _allAvatarItems.FirstOrDefault(i => i.Name == Models.Account.DefaultAvatarName)
                         ?? _allAvatarItems.FirstOrDefault();

            if (target != null)
            {
                avatarListBox.SelectedItem = target;
                avatarListBox.ScrollIntoView(target);
            }
        }

        /// <summary>
        /// 元素筛选按钮点击。<b>单选</b>：
        /// 点某个元素 = 只显示它（原来选中的自动取消）；再点同一个 = 取消，回到「全部」；
        /// 点「全部」= 清空筛选。
        /// </summary>
        private void ElementFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggle) return;

            if (toggle.Tag is not string element)
            {
                // 「全部」
                _selectedElement = null;
            }
            else if (toggle.IsChecked == true)
            {
                _selectedElement = element;
            }
            else
            {
                // 取消勾选：只有取消的就是当前选中项时才回到「全部」
                if (_selectedElement == element) _selectedElement = null;
            }

            SyncFilterToggles();
            ApplyAvatarFilter();
        }

        /// <summary>让按钮的勾选状态与 <see cref="_selectedElement"/> 保持一致（互斥）。</summary>
        private void SyncFilterToggles()
        {
            filterAllToggle.IsChecked = _selectedElement == null;

            foreach (var toggle in _elementToggles)
                toggle.IsChecked = toggle.Tag is string el && el == _selectedElement;
        }

        /// <summary>
        /// 按当前筛选刷新头像列表。
        /// 「全部」= 旅行者 + 角色（含末尾的其他头像）；
        /// 选元素 = 只看该元素的角色（<b>旅行者不算元素，任何元素筛选里都不出现</b>）；
        /// 选「其他」= 只看其他头像（NPC / 任务类）。
        /// 换 <c>ItemsSource</c> 会清空 <c>SelectedItem</c>，所以这里把原来选中的头像恢复回来。
        /// </summary>
        private void ApplyAvatarFilter()
        {
            var previous = avatarListBox.SelectedItem as AvatarItem;

            List<AvatarItem> view;
            if (_selectedElement == null)
            {
                view = _allAvatarItems;
            }
            else if (_selectedElement == OtherFilterKey)
            {
                view = _allAvatarItems.Where(i => i.IsOther).ToList();
            }
            else
            {
                // 旅行者（空 / 荧）没有元素归属：只出现在「全部」里，元素筛选一律排除。
                // （数据里她们 element 也是留空的，这里的 IsTraveler 只是双保险。）
                view = _allAvatarItems
                    .Where(i => !i.IsOther
                                && !CharacterElements.IsTraveler(i.Name)
                                && CharacterElements.ElementOf(i.Name) == _selectedElement)
                    .ToList();
            }

            avatarListBox.ItemsSource = view;

            if (previous != null && view.Contains(previous))
                avatarListBox.SelectedItem = previous;

            // 换过 ItemsSource 后布局要重新走一遍：
            // 重新算格子边长（按容器宽度）＋ 重新判断要不要显示容器外那根滚动条
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAvatarGrid();
                SyncAvatarScrollBar();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SetPasswordVisibility(bool isVisible)
        {
            _isPasswordVisible = isVisible;

            if (isVisible)
            {
                passwordTextBox.Text = passwordBox.Password;
                passwordTextBox.Visibility = Visibility.Visible;
                passwordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                passwordBox.Password = passwordTextBox.Text;
                passwordBox.Visibility = Visibility.Visible;
                passwordTextBox.Visibility = Visibility.Collapsed;
            }

            UpdatePasswordIcon();
            UpdateWatermarks();
        }

        private void UpdatePasswordIcon()
        {
            eyeOpenIcon.Visibility = _isPasswordVisible ? Visibility.Visible : Visibility.Collapsed;
            eyeClosedIcon.Visibility = _isPasswordVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlayAnimation("FadeInAnimation", () =>
            {
                mainBorder.Opacity = 1;
                (mainBorder.RenderTransform as ScaleTransform)?.SetValue(ScaleTransform.ScaleXProperty, 1.0);
                (mainBorder.RenderTransform as ScaleTransform)?.SetValue(ScaleTransform.ScaleYProperty, 1.0);
            });

            // 布局完成后再找列表内部的 ScrollViewer（容器外那根滚动条要跟它同步）
            Dispatcher.BeginInvoke(new Action(HookAvatarScroll),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===== 容器外的滚动条 ↔ 列表内部滚动 的同步 =====

        /// <summary>头像列表内部的 ScrollViewer（列表自己的滚动条是 Hidden）。</summary>
        private ScrollViewer? _avatarScrollViewer;

        /// <summary>找到列表内部的 ScrollViewer 并挂上同步（幂等）。</summary>
        private void HookAvatarScroll()
        {
            if (_avatarScrollViewer != null) return;

            _avatarScrollViewer = FindVisualChild<ScrollViewer>(avatarListBox);
            if (_avatarScrollViewer == null) return;

            _avatarScrollViewer.ScrollChanged += (s, e) => SyncAvatarScrollBar();
            avatarListBox.SizeChanged += (s, e) => UpdateAvatarGrid();
            SyncAvatarScrollBar();

            Dispatcher.BeginInvoke(new Action(UpdateAvatarGrid),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>头像一行固定 5 个。</summary>
        private const int AvatarColumns = 5;

        /// <summary>
        /// 按容器可用宽度算出格子边长（= 可用宽 / 5）写进 WrapPanel 的 ItemWidth/ItemHeight。
        /// 这样不管容器多宽，都是稳定的「一行 5 个」；格子是正方形，
        /// 头像在里面居中，所以横竖间距也保持一致。
        /// </summary>
        private void UpdateAvatarGrid()
        {
            var panel = FindVisualChild<WrapPanel>(avatarListBox);
            if (panel == null || panel.ActualWidth <= 0) return;

            double cell = Math.Floor(panel.ActualWidth / AvatarColumns);
            if (cell <= 0) return;
            if (Math.Abs(panel.ItemWidth - cell) < 0.5) return;   // 没变化就不动，避免反复触发布局

            panel.ItemWidth = cell;
            panel.ItemHeight = cell;
        }

        /// <summary>
        /// 把内部滚动状态同步给容器外那根滚动条；内容不足一屏时把滚动条藏起来。
        /// 滚动条在容器外面，所以它的显隐/宽度**不会**影响 5 列头像的布局。
        /// </summary>
        private void SyncAvatarScrollBar()
        {
            if (_avatarScrollViewer == null) return;

            bool scrollable = _avatarScrollViewer.ScrollableHeight > 0.5;
            avatarScrollBar.Visibility = scrollable ? Visibility.Visible : Visibility.Collapsed;

            avatarScrollBar.Maximum = _avatarScrollViewer.ScrollableHeight;
            avatarScrollBar.ViewportSize = _avatarScrollViewer.ViewportHeight;
            avatarScrollBar.Value = _avatarScrollViewer.VerticalOffset;
        }

        /// <summary>拖动外部滚动条 → 滚列表。</summary>
        private void AvatarScrollBar_Scroll(object sender, ScrollEventArgs e)
            => _avatarScrollViewer?.ScrollToVerticalOffset(e.NewValue);

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;

                var deeper = FindVisualChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
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

        private void UpdateWatermarks()
        {
            uidWatermarkText.Visibility = string.IsNullOrWhiteSpace(uidTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            levelWatermarkText.Visibility = string.IsNullOrWhiteSpace(levelTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            nicknameWatermarkText.Visibility = string.IsNullOrWhiteSpace(nicknameTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            usernameWatermarkText.Visibility = string.IsNullOrWhiteSpace(usernameTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            passwordWatermarkText.Visibility = string.IsNullOrWhiteSpace(_isPasswordVisible ? passwordTextBox.Text : passwordBox.Password)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                passwordStrengthText.Text = "密码强度：";
                passwordStrengthBar.Width = 0;
                passwordStrengthBar.Background = Brushes.Transparent;
                return;
            }

            int strength = CalculatePasswordStrength(password);

            if (strength <= 2)
                SetPasswordStrengthDisplay("弱", Brushes.Red, 100);
            else if (strength <= 4)
                SetPasswordStrengthDisplay("中", Brushes.Orange, 200);
            else
                SetPasswordStrengthDisplay("强", Brushes.Green, 300);
        }

        private int CalculatePasswordStrength(string password)
        {
            int strength = 0;
            if (password.Length >= 8) strength++;
            if (password.Length >= 12) strength++;
            if (Regex.IsMatch(password, @"\d")) strength++;
            if (Regex.IsMatch(password, @"[a-z]")) strength++;
            if (Regex.IsMatch(password, @"[A-Z]")) strength++;
            if (Regex.IsMatch(password, @"[^A-Za-z0-9]")) strength++;
            return strength;
        }

        private void SetPasswordStrengthDisplay(string level, Brush color, double width)
        {
            passwordStrengthText.Text = $"密码强度：{level}";
            passwordStrengthText.Foreground = color;
            passwordStrengthBar.Background = color;
            passwordStrengthBar.Width = width;
        }

        private bool ValidateInput()
        {
            // UID：《原神》官服与国际服（亚/美/欧/港澳台）都是 9 位数字，首位决定服务器归属
            string uidText = uidTextBox.Text.Trim();
            if (!long.TryParse(uidText, out _) || uidText.Length != 9)
            {
                ShowToast("UID 必须是 9 位数字", false);
                return false;
            }

            if (!int.TryParse(levelTextBox.Text, out int level) || level < 1 || level > 60)
            {
                ShowToast("冒险等级必须是1-60之间的数字", false);
                return false;
            }

            if (string.IsNullOrWhiteSpace(nicknameTextBox.Text))
            {
                ShowToast("请输入昵称", false);
                return false;
            }

            if (!IsValidUsername(usernameTextBox.Text))
            {
                ShowToast("账号格式不支持。仅支持：①中国大陆手机号 ②邮箱地址 ③通行证账号名（字母开头，4~20 位字母/数字/._-）", false);
                return false;
            }

            string currentPassword = _isPasswordVisible ? passwordTextBox.Text : passwordBox.Password;
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                ShowToast("请输入密码", false);
                return false;
            }

            // 头像必选。一个头像都没有说明资源包缺失，这时放行、由默认头像兜底。
            // （筛选后把选中项过滤掉了也会走到这里，提示用户重新选一个。）
            if (_allAvatarItems.Count > 0 && avatarListBox.SelectedItem is not AvatarItem)
            {
                ShowToast("请选择头像", false);
                return false;
            }

            return true;
        }

        private void ShowToast(string message, bool isSuccess = true)
        {
            ShowToastRequested?.Invoke(isSuccess ? "输入验证" : "输入错误", message, isSuccess);
        }

        #region 账号格式校验（官服 / 国际服通用）
        // ============================================================================
        // PC 端启动《原神》后的登录框里，「账号」一栏实际可以输入三类凭证。
        // 官方账号中心 FAQ 原话：「在游戏登录页面直接输入用户名/邮箱和密码即可登录」。
        //
        //   1) 中国大陆手机号      —— 仅官服（米哈游通行证）
        //   2) 邮箱地址            —— 官服 / 国际服（HoYoverse 通行证）都支持，且【不限邮箱服务商】
        //   3) 通行证账号名        —— 官服「米哈游通行证账号」、国际服「HoYoverse Account」都支持
        //
        // 所以这里按「手机号 or 邮箱 or 通行证账号名」做并集校验，三者命中任意一个即通过。
        // ============================================================================

        // 中国大陆手机号：1 开头，第二位 3-9，共 11 位
        private static readonly Regex CnPhoneRegex = new(@"^1[3-9]\d{9}$", RegexOptions.Compiled);

        // 邮箱：通用格式校验，不限制服务商（原来只放行了 QQ/163/Gmail 等十几个域名，太窄）
        private static readonly Regex EmailRegex = new(
            @"^[A-Za-z0-9]([A-Za-z0-9._%+\-]*)@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}$",
            RegexOptions.Compiled);

        // 通行证账号名 / HoYoverse 用户名：字母开头，允许字母、数字、点、下划线、连字符，长度 4~20
        private static readonly Regex PassportNameRegex = new(
            @"^[A-Za-z][A-Za-z0-9._\-]{3,19}$", RegexOptions.Compiled);

        /// <summary>判断输入的账号是否符合 PC 客户端登录框可接受的格式。</summary>
        private static bool IsValidUsername(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            string s = username.Trim();

            return CnPhoneRegex.IsMatch(s) || EmailRegex.IsMatch(s) || PassportNameRegex.IsMatch(s);
        }
        #endregion

        private void CloseWithAnimation(bool dialogResult = false)
        {
            if (_isClosing) return;
            _isClosing = true;

            PlayAnimation("FadeOutAnimation", () =>
            {
                DialogResult = dialogResult;
                Close();
            });
        }

        // 事件处理方法
        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);

        private void NumberOnlyTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text, 0);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdatePasswordStrength(passwordBox.Password);
            if (_isPasswordVisible) passwordTextBox.Text = passwordBox.Password;
            UpdateWatermarks();
        }

        private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPasswordVisible) passwordBox.Password = passwordTextBox.Text;
            UpdatePasswordStrength(passwordTextBox.Text);
            UpdateWatermarks();
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            SetPasswordVisibility(!_isPasswordVisible);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput()) return;

            string inputUid = uidTextBox.Text.Trim();
            var allAccounts = AccountDataService.LoadAccounts();
            bool uidExists;

            if (IsEditMode && Account != null)
                uidExists = allAccounts.Any(a => a.Id != Account.Id && a.Uid == inputUid);
            else
                uidExists = allAccounts.Any(a => a.Uid == inputUid);

            if (uidExists)
            {
                ShowToast($"UID「{inputUid}」已存在，无法保存", false);
                return;
            }

            Account.Uid = inputUid;
            Account.Level = int.Parse(levelTextBox.Text.Trim());
            Account.Nickname = nicknameTextBox.Text.Trim();
            // 账号统一去首尾空格后再加密保存，避免「看起来对、登录时却多了一个空格」
            Account.Username = usernameTextBox.Text.Trim();
            Account.Password = _isPasswordVisible ? passwordTextBox.Text : passwordBox.Password;
            // 头像：取选中的角色名；资源包缺失（列表为空）时回落到默认头像「空」
            Account.Avatar = (avatarListBox.SelectedItem as AvatarItem)?.Name
                             ?? Models.Account.DefaultAvatarName;

            // 如果是新建账号，设置创建时间
            if (!IsEditMode)
            {
                Account.CreatedTime = DateTime.Now;
            }

            CloseWithAnimation(true);
        }

        // 统一的TextChanged处理方法
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateWatermarks();
        }

        private void UIDTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
        private void LevelTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
        private void NicknameTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
        private void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e) => TextBox_TextChanged(sender, e);
    }
}