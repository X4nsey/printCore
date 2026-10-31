using PrintCore.Infrastructure;
using PrintCore.Models;

namespace PrintCore.ViewModels
{
    /// <summary>Базовая модель представления раздела приложения.</summary>
    public abstract class PageViewModel : ObservableObject
    {
        protected PageViewModel(PageKey key, string title, string subtitle)
        {
            Key = key;
            Title = title;
            Subtitle = subtitle;
        }

        public PageKey Key { get; }

        /// <summary>Заголовок раздела в верхней панели.</summary>
        public string Title { get; }

        /// <summary>Краткое описание раздела под заголовком.</summary>
        public string Subtitle { get; }

        /// <summary>Вызывается при каждом переходе в раздел.</summary>
        public virtual void OnNavigatedTo()
        {
        }
    }
}
