using PrintCore.Infrastructure;

namespace PrintCore.ViewModels
{
    /// <summary>Карточка статистики на главной странице.</summary>
    public class StatCardViewModel : ObservableObject
    {
        private string _value;
        private bool _hasValue;
        private bool _isLoading;
        private string _caption;

        public StatCardViewModel(string title, string iconKey)
        {
            Title = title;
            IconKey = iconKey;
        }

        public string Title { get; }

        public string IconKey { get; }

        /// <summary>Отображаемое значение (только реальные данные).</summary>
        public string Value
        {
            get { return _value; }
            private set { SetProperty(ref _value, value); }
        }

        /// <summary>false — данных нет, вместо числа показывается «Нет данных».</summary>
        public bool HasValue
        {
            get { return _hasValue; }
            private set { SetProperty(ref _hasValue, value); }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public string Caption
        {
            get { return _caption; }
            private set { SetProperty(ref _caption, value); }
        }

        public void SetValue(int value, string caption)
        {
            Value = value.ToString("N0");
            HasValue = true;
            IsLoading = false;
            Caption = caption;
        }

        public void SetNoData(string caption)
        {
            Value = "Нет данных";
            HasValue = false;
            IsLoading = false;
            Caption = caption;
        }

        public void SetLoading(string caption)
        {
            Value = "Загрузка…";
            HasValue = false;
            IsLoading = true;
            Caption = caption;
        }
    }
}
