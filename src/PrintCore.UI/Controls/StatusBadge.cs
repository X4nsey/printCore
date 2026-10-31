using System.Windows;
using System.Windows.Controls;
using PrintCore.Models;

namespace PrintCore.Controls
{
    /// <summary>Компактный индикатор состояния (бейдж) с цветовым тоном.</summary>
    public class StatusBadge : Control
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(null));

        public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
            nameof(Tone), typeof(StatusTone), typeof(StatusBadge), new PropertyMetadata(StatusTone.Neutral));

        static StatusBadge()
        {
            FocusableProperty.OverrideMetadata(typeof(StatusBadge), new FrameworkPropertyMetadata(false));
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public StatusTone Tone
        {
            get { return (StatusTone)GetValue(ToneProperty); }
            set { SetValue(ToneProperty, value); }
        }
    }
}
