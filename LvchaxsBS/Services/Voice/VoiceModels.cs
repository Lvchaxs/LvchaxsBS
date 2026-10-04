using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LvchaxsBS.Services.Voice
{
    public class VoiceSubtitle
    {
        public string Text { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public bool HasAvatar { get; set; }
    }

    public class VoiceCardItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public BitmapImage? ImageSource { get; set; }
        public int VoiceCount { get; set; }
        public string VoiceCountText => $"{VoiceCount}个音频";
        public List<string> VoiceTexts { get; set; } = new List<string>();
        public List<VoiceTextItem> VoiceTextItems { get; set; } = new List<VoiceTextItem>();
    }

    public class VoiceTextItem : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private bool _isEditing = false;

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged(nameof(Text));
                }
            }
        }

        public Brush ColorBrush { get; set; } = new SolidColorBrush(Colors.Black);
        public string AudioFileName { get; set; } = string.Empty;

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                    OnPropertyChanged(nameof(IsDisplayMode));
                    OnPropertyChanged(nameof(IsEditMode));
                }
            }
        }

        public bool IsDisplayMode => !_isEditing;
        public bool IsEditMode => _isEditing;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}