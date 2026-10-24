using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace LvchaxsBS_UID.Models
{
    /// <summary>
    /// 分组列表里「每一张分组卡片」的显示对象。
    /// ★ 以前这里是 <c>SortingService.CreateGroupDisplayObject</c> 里现搓的**匿名类型** ——
    ///   匿名类型没有 <see cref="INotifyPropertyChanged"/>，属性一改界面不会跟着变，
    ///   所以没法做「先把星显出来、等动画播完再改数据」那套（见 AccountManagerView.ApplyStarToggle）。
    ///   改成正经的类型之后，<see cref="ShowStar"/> 就能像账号卡片那样被临时翻起来做动画预告。
    /// ★ 其余字段与原来匿名类型一一对应，XAML 里的绑定键名全部没变。
    /// </summary>
    public class GroupDisplayItem : INotifyPropertyChanged
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        /// <summary>备注（就是分组的 <c>TagNote</c>，原样、不加前导符号）——
        /// 卡片第二行显示，位置相当于账号卡片那行的 UID/账号/密码。</summary>
        public string Remark { get; set; } = "";

        public List<int> AccountIds { get; set; } = new List<int>();

        /// <summary>分组里的账号总数（卡片第一个标签「包含 N 个账号」）。</summary>
        public int AccountCount { get; set; }

        /// <summary>分组里已置底的账号数（卡片第二个标签「已标记 N 个」）。</summary>
        public int MarkedCount { get; set; }

        /// <summary>分组里还没置底的账号数（卡片第三个标签「未标记 N 个」）。</summary>
        public int UnmarkedCount { get; set; }

        /// <summary>该分组有没有已置底的账号 —— 决定「清除置底」按钮显示不显示（code-behind 反射读它）。</summary>
        public bool HasMarks { get; set; }

        public bool IsPinned { get; set; }

        private bool _showStar;

        /// <summary>
        /// 卡片是否显示「置顶」竖条（左缘那条蓝色竖条，= 该分组自己的 <see cref="IsPinned"/>）。
        /// ★ 纯界面状态：只在列表构建时按分组数据算好写进来；
        ///   动画预告期间会被临时置 true（见 AccountManagerView.PreviewPinHighlight），
        ///   真的置顶数据要等动画播完才落。
        /// </summary>
        [JsonIgnore]
        public bool ShowStar
        {
            get => _showStar;
            set
            {
                if (_showStar != value)
                {
                    _showStar = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
