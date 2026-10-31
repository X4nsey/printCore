using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrintCore.Controls
{
    /// <summary>
    /// ContentControl с ненавязчивой анимацией появления (прозрачность + сдвиг) при смене содержимого.
    /// Используется для центральной рабочей области при переходе между разделами.
    /// </summary>
    public class AnimatedContentControl : ContentControl
    {
        private static readonly Duration AnimationDuration = new Duration(TimeSpan.FromMilliseconds(220));

        private readonly TranslateTransform _translate = new TranslateTransform();

        public AnimatedContentControl()
        {
            RenderTransform = _translate;
            Focusable = false;
            IsTabStop = false;
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);

            if (oldContent == null || !IsLoaded)
            {
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation(0, 1, AnimationDuration) { EasingFunction = ease };
            var slide = new DoubleAnimation(10, 0, AnimationDuration) { EasingFunction = ease };

            BeginAnimation(OpacityProperty, fade);
            _translate.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
}
