using LvchaxsBS.UI.Helpers;
using LvchaxsBS.Services;
using LvchaxsBS.Services.Common;
using LvchaxsBS.Services.Hooks;
using LvchaxsBS.Services.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LvchaxsBS.Core
{
    public static class SubtitleOverlayService
    {
        public enum SubtitleOwner
        {
            None,
            AutoCook,
            Fishing,
            AutoLumber,
        }

        #region 物理像素基准常量

        private const double AVATAR_SIZE = 109;
        private const double AVATAR_MARGIN_RIGHT = 24;

        private const double BUBBLE_HEIGHT = 87;
        private const double BUBBLE_CORNER_RADIUS = 42;
        private const double BUBBLE_TAIL_RADIUS = 42;
        private const double BUBBLE_TAIL_RADIUS_Y = 25;
        private const double BUBBLE_MIN_WIDTH = 210;
        private const double BUBBLE_TEXT_EXTRA = 88;

        private const double TEXT_FONT_SIZE = 29;
        private const double TEXT_MARGIN_LEFT = 54;
        private const double TEXT_MARGIN_RIGHT = 34;

        private const int FADE_IN_MS = 100;
        private const int FADE_OUT_MS = 100;

        private const int SECOND_SUBTITLE_OFFSET_Y = 130;

        #endregion

        #region 字段

        private static readonly string[] _avatarFiles =
        {
            "空.png",
            "荧.png",
        };

        private static Window? _overlayWindow;
        private static bool _isOverlayVisible = false;
        private static bool _isFadingOut = false;

        private static StackPanel? _container;
        private static Image? _avatarImage;
        private static Path? _subtitleBorder;
        private static TextBlock? _subtitleText;

        private static DispatcherTimer? _overlayTimer;
        private const int TOPMOST_REFRESH_MS = 50;

        private static bool _isFocused = false;
        private static bool _isActive = false;

        private static int _currentWidth = 0;
        private static int _currentHeight = 0;

        private static string _positionKey = "";
        private static string _currentAvatarPath = "";

        private static Window? _overlayWindow2;
        private static bool _isOverlayVisible2 = false;
        private static bool _isFadingOut2 = false;

        private static StackPanel? _container2;
        private static Image? _avatarImage2;
        private static Path? _subtitleBorder2;
        private static TextBlock? _subtitleText2;

        private static bool _isActive2 = false;
        private static string _currentAvatarPath2 = "";

        #endregion

        #region 初始化

        public static void Initialize()
        {
            DpiService.Initialize();

            WindowFocusService.FocusChanged += OnFocusChanged;
            WindowFocusService.BoundsChanged += OnBoundsChanged;

            _isFocused = WindowFocusService.IsTargetFocused;
            var initBounds = WindowFocusService.LastBounds;
            if (!initBounds.IsEmpty)
            {
                _currentWidth = initBounds.Width;
                _currentHeight = initBounds.Height;
            }

            StartOverlayTimer();
        }

        public static void Shutdown()
        {
            WindowFocusService.FocusChanged -= OnFocusChanged;
            WindowFocusService.BoundsChanged -= OnBoundsChanged;
            StopOverlayTimer();
            CloseOverlay();
            CloseOverlay2();
        }

        public static void ResetAvatarForNewRun()
        {
            _currentAvatarPath = "";
            _currentAvatarPath2 = "";
        }

        #endregion

        #region 窗口焦点

        private static void OnFocusChanged(object? sender, bool focused)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _isFocused = focused;
                UpdateOverlayVisibility();
                UpdateOverlayVisibility2();
            });
        }

        private static void OnBoundsChanged(object? sender, WindowFocusService.WindowBounds info)
        {
            if (info.IsEmpty) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (info.Width > 0 && info.Height > 0)
                {
                    bool resolutionChanged = (_currentWidth != info.Width || _currentHeight != info.Height);
                    _currentWidth = info.Width;
                    _currentHeight = info.Height;

                    if (resolutionChanged)
                    {
                        ApplySizes();
                        ApplySizes2();

                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            UpdateWindowPosition();
                            UpdateWindowPosition2();
                        }, DispatcherPriority.Render);
                    }
                }

                UpdateOverlayVisibility();
                UpdateOverlayVisibility2();
            });
        }

        #endregion

        #region 定时刷新

        private static void StartOverlayTimer()
        {
            StopOverlayTimer();

            _overlayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TOPMOST_REFRESH_MS)
            };
            _overlayTimer.Tick += (s, e) => OverlayTick();
            _overlayTimer.Start();
        }

        private static void StopOverlayTimer()
        {
            if (_overlayTimer != null)
            {
                _overlayTimer.Stop();
                _overlayTimer = null;
            }
        }

        private static void OverlayTick()
        {
            if (_isOverlayVisible && _overlayWindow != null)
                UpdateWindowPosition();

            if (_isOverlayVisible2 && _overlayWindow2 != null)
                UpdateWindowPosition2();
        }

        #endregion

        #region 显示/隐藏（第一个字幕）

        private static void UpdateOverlayVisibility()
        {
            bool shouldShow = _isActive && _isFocused;

            if (shouldShow)
                ShowOverlay();
            else
                HideOverlay();
        }

        public static void SetUserEnabled(bool enabled)
        {
        }

        public static void SetPositionKey(string key)
        {
            _positionKey = key ?? "";
        }

        public static void ShowSubtitle()
        {
            _isActive = true;
            UpdateOverlayVisibility();
        }

        public static void HideSubtitle()
        {
            _isActive = false;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleText != null) _subtitleText.Text = "";
            });

            UpdateOverlayVisibility();
        }

        public static void ShowOverlay()
        {
            if (!_isActive) return;
            if (!_isFocused) return;

            if (_isFadingOut)
            {
                _isFadingOut = false;
            }

            if (_isOverlayVisible && _overlayWindow != null && _overlayWindow.IsVisible)
            {
                if (_overlayWindow.Opacity < 1.0)
                {
                    StartFadeIn();
                }
                return;
            }

            CreateWindowIfNeeded();
            RefreshAvatarIfNeeded();
            ApplySizes();

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateWindowPosition();
            }, DispatcherPriority.Render);

            if (_overlayWindow != null && !_overlayWindow.IsVisible)
            {
                _overlayWindow.Opacity = 0;
                _overlayWindow.Show();
                _isOverlayVisible = true;
                StartFadeIn();
            }
        }

        public static void HideOverlay()
        {
            if (_overlayWindow == null || !_overlayWindow.IsVisible) return;
            if (_isFadingOut) return;

            _isFadingOut = true;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(FADE_OUT_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeOut.Completed += (s, e) =>
            {
                if (!_isFadingOut) return;

                _isFadingOut = false;

                if (_overlayWindow != null && _overlayWindow.IsVisible)
                {
                    _overlayWindow.Hide();
                    _overlayWindow.Opacity = 1;
                }
                _isOverlayVisible = false;
            };

            _overlayWindow.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private static void StartFadeIn()
        {
            if (_overlayWindow == null) return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(FADE_IN_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            _overlayWindow.BeginAnimation(Window.OpacityProperty, fadeIn);
            _overlayWindow.Opacity = 1;
        }

        #endregion

        #region 字幕文本（第一个）

        public static void SetSubtitle(string text)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleText == null) return;

                if (!_isActive)
                {
                    _subtitleText.Text = "";
                    return;
                }

                _subtitleText.Text = text ?? string.Empty;
                UpdateBubbleWidth();
            });
        }

        public static void AppendSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleText == null) return;

                string cur = _subtitleText.Text ?? "";
                if (cur.EndsWith(suffix)) return;

                _subtitleText.Text = cur + suffix;
                UpdateBubbleWidth();
            });
        }

        public static string GetSubtitle()
        {
            string result = "";
            Application.Current?.Dispatcher.Invoke(() =>
            {
                result = _subtitleText?.Text ?? "";
            });
            return result;
        }

        public static void SetBackgroundOpacity(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            byte alpha = (byte)Math.Round(percent * 255.0 / 100.0);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleBorder != null)
                {
                    _subtitleBorder.Fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                }
            });
        }

        #endregion

        #region 显示/隐藏（第二个字幕）

        private static void UpdateOverlayVisibility2()
        {
            bool shouldShow = _isActive2 && _isFocused;

            if (shouldShow)
                ShowOverlay2();
            else
                HideOverlay2();
        }

        public static void ShowSubtitle2()
        {
            _isActive2 = true;
            UpdateOverlayVisibility2();
        }

        public static void HideSubtitle2()
        {
            _isActive2 = false;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleText2 != null) _subtitleText2.Text = "";
            });

            UpdateOverlayVisibility2();
        }

        public static void ShowOverlay2()
        {
            if (!_isActive2) return;
            if (!_isFocused) return;

            if (_isFadingOut2)
            {
                _isFadingOut2 = false;
            }

            if (_isOverlayVisible2 && _overlayWindow2 != null && _overlayWindow2.IsVisible)
            {
                if (_overlayWindow2.Opacity < 1.0)
                {
                    StartFadeIn2();
                }
                return;
            }

            CreateWindowIfNeeded2();
            RefreshAvatarIfNeeded2();
            ApplySizes2();

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                UpdateWindowPosition2();
            }, DispatcherPriority.Render);

            if (_overlayWindow2 != null && !_overlayWindow2.IsVisible)
            {
                _overlayWindow2.Opacity = 0;
                _overlayWindow2.Show();
                _isOverlayVisible2 = true;
                StartFadeIn2();
            }
        }

        public static void HideOverlay2()
        {
            if (_overlayWindow2 == null || !_overlayWindow2.IsVisible) return;
            if (_isFadingOut2) return;

            _isFadingOut2 = true;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(FADE_OUT_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            fadeOut.Completed += (s, e) =>
            {
                if (!_isFadingOut2) return;

                _isFadingOut2 = false;

                if (_overlayWindow2 != null && _overlayWindow2.IsVisible)
                {
                    _overlayWindow2.Hide();
                    _overlayWindow2.Opacity = 1;
                }
                _isOverlayVisible2 = false;
            };

            _overlayWindow2.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private static void StartFadeIn2()
        {
            if (_overlayWindow2 == null) return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(FADE_IN_MS),
                FillBehavior = FillBehavior.HoldEnd
            };

            _overlayWindow2.BeginAnimation(Window.OpacityProperty, fadeIn);
            _overlayWindow2.Opacity = 1;
        }

        #endregion

        #region 字幕文本（第二个）

        public static void SetSubtitle2(string text)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleText2 == null) return;

                if (!_isActive2)
                {
                    _subtitleText2.Text = "";
                    return;
                }

                _subtitleText2.Text = text ?? string.Empty;
                UpdateBubbleWidth2();
            });
        }

        public static void AppendSuffix2(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_subtitleText2 == null) return;

                string cur = _subtitleText2.Text ?? "";
                if (cur.EndsWith(suffix)) return;

                _subtitleText2.Text = cur + suffix;
                UpdateBubbleWidth2();
            });
        }

        public static string GetSubtitle2()
        {
            string result = "";
            Application.Current?.Dispatcher.Invoke(() =>
            {
                result = _subtitleText2?.Text ?? "";
            });
            return result;
        }

        #endregion

        #region 尺寸计算

        private static double GetScaleByResolution()
        {
            return ResolutionScaleHelper.GetScale(_currentWidth, _currentHeight);
        }

        private static void ApplySizes()
        {
            if (_avatarImage == null || _subtitleText == null) return;

            double scale = GetScaleByResolution();

            double avatarPhys = AVATAR_SIZE * scale;
            double avatarMarginRightPhys = AVATAR_MARGIN_RIGHT * scale;
            double fontPhys = TEXT_FONT_SIZE * scale;
            double textMarginLeftPhys = TEXT_MARGIN_LEFT * scale;
            double textMarginRightPhys = TEXT_MARGIN_RIGHT * scale;

            _avatarImage.Width = DpiService.PhysicalToDipX(avatarPhys);
            _avatarImage.Height = DpiService.PhysicalToDipY(avatarPhys);
            _avatarImage.Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(avatarMarginRightPhys), 0);

            _subtitleText.FontSize = DpiService.PhysicalToDipY(fontPhys);
            _subtitleText.Margin = new Thickness(
                DpiService.PhysicalToDipX(textMarginLeftPhys), 0,
                DpiService.PhysicalToDipX(textMarginRightPhys), 0);

            UpdateBubbleWidth();
        }

        private static void ApplySizes2()
        {
            if (_avatarImage2 == null || _subtitleText2 == null) return;

            double scale = GetScaleByResolution();

            double avatarPhys = AVATAR_SIZE * scale;
            double avatarMarginRightPhys = AVATAR_MARGIN_RIGHT * scale;
            double fontPhys = TEXT_FONT_SIZE * scale;
            double textMarginLeftPhys = TEXT_MARGIN_LEFT * scale;
            double textMarginRightPhys = TEXT_MARGIN_RIGHT * scale;

            _avatarImage2.Width = DpiService.PhysicalToDipX(avatarPhys);
            _avatarImage2.Height = DpiService.PhysicalToDipY(avatarPhys);
            _avatarImage2.Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(avatarMarginRightPhys), 0);

            _subtitleText2.FontSize = DpiService.PhysicalToDipY(fontPhys);
            _subtitleText2.Margin = new Thickness(
                DpiService.PhysicalToDipX(textMarginLeftPhys), 0,
                DpiService.PhysicalToDipX(textMarginRightPhys), 0);

            UpdateBubbleWidth2();
        }

        private static void UpdateBubbleWidth()
        {
            if (_subtitleBorder == null || _subtitleText == null) return;
            _subtitleBorder.Data = BuildBubbleGeometry(_subtitleText);
        }

        private static void UpdateBubbleWidth2()
        {
            if (_subtitleBorder2 == null || _subtitleText2 == null) return;
            _subtitleBorder2.Data = BuildBubbleGeometry(_subtitleText2);
        }

        private static Geometry BuildBubbleGeometry(TextBlock textBlock)
        {
            double scale = GetScaleByResolution();

            string text = textBlock.Text ?? "";

            double textWidth;
            try
            {
                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                    textBlock.FontSize,
                    Brushes.White,
                    new NumberSubstitution(),
                    TextFormattingMode.Ideal);

                textWidth = formattedText.Width;
            }
            catch
            {
                textWidth = 0;
                foreach (char c in text)
                {
                    if (c > 127) textWidth += 29;
                    else textWidth += 16;
                }
            }

            double textWidthPhys = DpiService.DipToPhysicalX(textWidth);

            double targetWidthPhys = Math.Max(BUBBLE_MIN_WIDTH * scale,
                                              textWidthPhys + BUBBLE_TEXT_EXTRA * scale);
            double hPhys = BUBBLE_HEIGHT * scale;
            double rPhys = BUBBLE_CORNER_RADIUS * scale;

            double tw = DpiService.PhysicalToDipX(targetWidthPhys);
            double h = DpiService.PhysicalToDipY(hPhys);
            double r = DpiService.PhysicalToDipX(rPhys);

            double xTail = DpiService.PhysicalToDipX(62 * scale);
            double xNotch1 = DpiService.PhysicalToDipX(24 * scale);
            double xNotch2 = DpiService.PhysicalToDipX(17 * scale);
            double xNotch3 = DpiService.PhysicalToDipX(59 * scale);

            double yNotch1 = DpiService.PhysicalToDipY(67 * scale);
            double yNotch2 = DpiService.PhysicalToDipY(52 * scale);
            double yNotch3 = DpiService.PhysicalToDipY(45 * scale);

            double rxTail = DpiService.PhysicalToDipX(BUBBLE_TAIL_RADIUS * scale);
            double ryTail = DpiService.PhysicalToDipY(BUBBLE_TAIL_RADIUS * scale);
            double ryTail2 = DpiService.PhysicalToDipY(BUBBLE_TAIL_RADIUS_Y * scale);

            string newData =
                $"M {xTail},0 " +
                $"L {tw - r},0 " +
                $"A {r},{r} 0 0,1 {tw},{r} " +
                $"L {tw},{h - r} " +
                $"A {r},{r} 0 0,1 {tw - r},{h} " +
                $"L {xTail},{h} " +
                $"A {rxTail},{ryTail} 0 0,1 {xNotch1},{yNotch1} " +
                $"A {rxTail},{ryTail2} 0 0,1 0,{yNotch2} " +
                $"A {rxTail},{ryTail} 0 0,0 {xNotch2},{yNotch3} " +
                $"A {rxTail},{ryTail} 0 0,1 {xNotch3},0 Z";

            return Geometry.Parse(newData);
        }

        #endregion

        #region 头像

        private static string PickRandomAvatarPath()
        {
            var random = new Random();
            string file = _avatarFiles[random.Next(_avatarFiles.Length)];
            return $"pack://application:,,,/Resources/Template/角色头像/150x150/{file}";
        }

        private static string PickOtherAvatarPath(string otherPath)
        {
            foreach (var file in _avatarFiles)
            {
                string path = $"pack://application:,,,/Resources/Template/角色头像/150x150/{file}";
                if (path != otherPath)
                    return path;
            }

            return PickRandomAvatarPath();
        }

        private static void RefreshAvatarIfNeeded()
        {
            if (_avatarImage == null) return;

            if (string.IsNullOrEmpty(_currentAvatarPath))
            {
                _currentAvatarPath = PickRandomAvatarPath();
            }

            _avatarImage.Source = ResourceImageHelper.Load(_currentAvatarPath);
        }

        private static void RefreshAvatarIfNeeded2()
        {
            if (_avatarImage2 == null) return;

            if (string.IsNullOrEmpty(_currentAvatarPath2))
            {
                _currentAvatarPath2 = PickOtherAvatarPath(_currentAvatarPath);
            }

            _avatarImage2.Source = ResourceImageHelper.Load(_currentAvatarPath2);
        }

        #endregion

        #region 创建窗口

        private static void CreateWindowIfNeeded()
        {
            if (_overlayWindow != null) return;

            _overlayWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false,
                Opacity = 0
            };

            var helper = new WindowInteropHelper(_overlayWindow);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow);

            _container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent
            };

            if (string.IsNullOrEmpty(_currentAvatarPath))
            {
                _currentAvatarPath = PickRandomAvatarPath();
            }

            _avatarImage = new Image
            {
                Width = DpiService.PhysicalToDipX(AVATAR_SIZE),
                Height = DpiService.PhysicalToDipY(AVATAR_SIZE),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(AVATAR_MARGIN_RIGHT), 0),
                Source = ResourceImageHelper.Load(_currentAvatarPath),
                Visibility = Visibility.Visible
            };
            RenderOptions.SetBitmapScalingMode(_avatarImage, BitmapScalingMode.HighQuality);
            _container.Children.Add(_avatarImage);

            var bubbleGrid = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _subtitleBorder = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                Stretch = Stretch.None,
                Data = Geometry.Parse(
                    "M 62,0 L 168,0 A 42,42 0 0,1 210,42 L 210,45 A 42,42 0 0,1 168,87 L 62,87 " +
                    "A 42,42 0 0,1 24,67 A 42,25 0 0,1 0,52 A 42,42 0 0,0 17,45 A 42,42 0 0,1 59,0 Z")
            };
            bubbleGrid.Children.Add(_subtitleBorder);

            _subtitleText = new TextBlock
            {
                Text = "",
                FontSize = DpiService.PhysicalToDipY(TEXT_FONT_SIZE),
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(
                    DpiService.PhysicalToDipX(TEXT_MARGIN_LEFT), 0,
                    DpiService.PhysicalToDipX(TEXT_MARGIN_RIGHT), 0)
            };
            bubbleGrid.Children.Add(_subtitleText);

            _container.Children.Add(bubbleGrid);

            _overlayWindow.Content = _container;
            _overlayWindow.MouseLeftButtonDown += (s, e) => HideOverlay();

            ApplySizes();
        }

        private static void CreateWindowIfNeeded2()
        {
            if (_overlayWindow2 != null) return;

            _overlayWindow2 = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Focusable = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowActivated = false,
                Opacity = 0
            };

            var helper = new WindowInteropHelper(_overlayWindow2);
            helper.EnsureHandle();
            OverlayWindowService.ApplyOverlayStyle(_overlayWindow2);

            _container2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.Transparent
            };

            if (string.IsNullOrEmpty(_currentAvatarPath2))
            {
                _currentAvatarPath2 = PickOtherAvatarPath(_currentAvatarPath);
            }

            _avatarImage2 = new Image
            {
                Width = DpiService.PhysicalToDipX(AVATAR_SIZE),
                Height = DpiService.PhysicalToDipY(AVATAR_SIZE),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                Margin = new Thickness(0, 0, DpiService.PhysicalToDipX(AVATAR_MARGIN_RIGHT), 0),
                Source = ResourceImageHelper.Load(_currentAvatarPath2),
                Visibility = Visibility.Visible
            };
            RenderOptions.SetBitmapScalingMode(_avatarImage2, BitmapScalingMode.HighQuality);
            _container2.Children.Add(_avatarImage2);

            var bubbleGrid2 = new Grid
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            _subtitleBorder2 = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)),
                Stretch = Stretch.None,
                Data = Geometry.Parse(
                    "M 62,0 L 168,0 A 42,42 0 0,1 210,42 L 210,45 A 42,42 0 0,1 168,87 L 62,87 " +
                    "A 42,42 0 0,1 24,67 A 42,25 0 0,1 0,52 A 42,42 0 0,0 17,45 A 42,42 0 0,1 59,0 Z")
            };
            bubbleGrid2.Children.Add(_subtitleBorder2);

            _subtitleText2 = new TextBlock
            {
                Text = "",
                FontSize = DpiService.PhysicalToDipY(TEXT_FONT_SIZE),
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(
                    DpiService.PhysicalToDipX(TEXT_MARGIN_LEFT), 0,
                    DpiService.PhysicalToDipX(TEXT_MARGIN_RIGHT), 0)
            };
            bubbleGrid2.Children.Add(_subtitleText2);

            _container2.Children.Add(bubbleGrid2);

            _overlayWindow2.Content = _container2;
            _overlayWindow2.MouseLeftButtonDown += (s, e) => HideOverlay2();

            ApplySizes2();
        }

        #endregion

        #region 窗口定位

        private static void UpdateWindowPosition()
        {
            if (_overlayWindow == null) return;
            if (string.IsNullOrEmpty(_positionKey)) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>(_positionKey);
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double wDip = _overlayWindow.ActualWidth > 0 ? _overlayWindow.ActualWidth : 300;
            double hDip = _overlayWindow.ActualHeight > 0 ? _overlayWindow.ActualHeight : 80;

            double wPhys = DpiService.DipToPhysicalX(wDip);
            double hPhys = DpiService.DipToPhysicalY(hDip);

            OverlayWindowService.SetBounds(_overlayWindow,
                (int)cachedPos.X, (int)cachedPos.Y,
                (int)wPhys, (int)hPhys);
        }

        private static void UpdateWindowPosition2()
        {
            if (_overlayWindow2 == null) return;
            if (string.IsNullOrEmpty(_positionKey)) return;

            var cachedPosObj = CoordinateFormats.GetCached<object>(_positionKey);
            if (cachedPosObj == null) return;

            var cachedPos = (CoordinateFormats.Point)cachedPosObj;

            double wDip = _overlayWindow2.ActualWidth > 0 ? _overlayWindow2.ActualWidth : 300;
            double hDip = _overlayWindow2.ActualHeight > 0 ? _overlayWindow2.ActualHeight : 80;

            double wPhys = DpiService.DipToPhysicalX(wDip);
            double hPhys = DpiService.DipToPhysicalY(hDip);

            double scale = GetScaleByResolution();
            int offsetY = (int)(SECOND_SUBTITLE_OFFSET_Y * scale);

            OverlayWindowService.SetBounds(_overlayWindow2,
                (int)cachedPos.X, (int)cachedPos.Y + offsetY,
                (int)wPhys, (int)hPhys);
        }

        #endregion

        #region 关闭

        private static void CloseOverlay()
        {
            if (_overlayWindow != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow);
                _overlayWindow.Close();
                _overlayWindow = null;
                _isOverlayVisible = false;
                _isFadingOut = false;
            }
        }

        private static void CloseOverlay2()
        {
            if (_overlayWindow2 != null)
            {
                OverlayWindowService.StopTopmostKeepAlive(_overlayWindow2);
                _overlayWindow2.Close();
                _overlayWindow2 = null;
                _isOverlayVisible2 = false;
                _isFadingOut2 = false;
            }
        }

        #endregion
    }
}