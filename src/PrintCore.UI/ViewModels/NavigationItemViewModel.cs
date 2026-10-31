using System;
using PrintCore.Infrastructure;
using PrintCore.Models;

namespace PrintCore.ViewModels
{
    /// <summary>Пункт бокового меню.</summary>
    public class NavigationItemViewModel : ObservableObject
    {
        private readonly Action<NavigationItemViewModel> _onSelected;
        private bool _isSelected;

        public NavigationItemViewModel(PageKey key, string title, string iconKey, Action<NavigationItemViewModel> onSelected)
        {
            Key = key;
            Title = title;
            IconKey = iconKey;
            _onSelected = onSelected;
        }

        public PageKey Key { get; }

        public string Title { get; }

        /// <summary>Ключ геометрии иконки в словаре Icons.xaml.</summary>
        public string IconKey { get; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (SetProperty(ref _isSelected, value) && value)
                {
                    _onSelected?.Invoke(this);
                }
            }
        }

        /// <summary>Устанавливает выделение без инициирования навигации.</summary>
        internal void SetSelectedSilently(bool value)
        {
            SetProperty(ref _isSelected, value, nameof(IsSelected));
        }
    }
}
