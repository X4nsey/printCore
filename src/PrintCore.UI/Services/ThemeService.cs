using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>
    /// Переключение светлой/тёмной темы путём замены словаря ресурсов цветов.
    /// Все элементы интерфейса используют DynamicResource, поэтому обновляются мгновенно.
    /// </summary>
    public class ThemeService
    {
        private const string ThemesFolderMarker = "/Themes/";
        private readonly SettingsService _settings;

        public ThemeService(SettingsService settings)
        {
            _settings = settings;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public event EventHandler ThemeChanged;

        /// <summary>Выбранный пользователем режим (включая «Как в системе»).</summary>
        public AppTheme SelectedTheme { get; private set; } = AppTheme.Light;

        /// <summary>Фактически применённая тема (только Light или Dark).</summary>
        public AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

        public bool IsDark
        {
            get { return EffectiveTheme == AppTheme.Dark; }
        }

        /// <summary>Применяет тему без сохранения (используется при запуске).</summary>
        public void Apply(AppTheme theme)
        {
            SelectedTheme = theme;
            var effective = theme == AppTheme.System ? GetSystemTheme() : theme;
            EffectiveTheme = effective;

            var uri = new Uri(
                effective == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
                UriKind.Relative);

            var merged = Application.Current.Resources.MergedDictionaries;
            var existing = merged.FirstOrDefault(d =>
                d.Source != null && d.Source.OriginalString.Replace('\\', '/').Contains(ThemesFolderMarker.TrimStart('/')));

            var dictionary = new ResourceDictionary { Source = uri };
            if (existing != null)
            {
                var index = merged.IndexOf(existing);
                merged[index] = dictionary;
            }
            else
            {
                merged.Insert(0, dictionary);
            }

            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Применяет и сохраняет выбранную тему.</summary>
        public void SetTheme(AppTheme theme)
        {
            Apply(theme);
            _settings.Current.Theme = theme;
            _settings.Save();
        }

        /// <summary>Быстрое переключение светлая ↔ тёмная.</summary>
        public void Toggle()
        {
            SetTheme(IsDark ? AppTheme.Light : AppTheme.Dark);
        }

        private static AppTheme GetSystemTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        return intValue == 0 ? AppTheme.Dark : AppTheme.Light;
                    }
                }
            }
            catch
            {
                // Нет доступа к реестру — используем светлую тему.
            }

            return AppTheme.Light;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (SelectedTheme != AppTheme.System || e.Category != UserPreferenceCategory.General)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(new Action(() =>
            {
                if (GetSystemTheme() != EffectiveTheme)
                {
                    Apply(AppTheme.System);
                }
            }));
        }
    }
}
