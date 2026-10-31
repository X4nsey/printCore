using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>Раздел «Установленные принтеры»: список очередей Windows и панель подробных сведений.</summary>
    public class InstalledPrintersViewModel : PageViewModel
    {
        private readonly InstalledPrinterService _service;
        private InstalledPrinter _selectedPrinter;

        public InstalledPrintersViewModel(InstalledPrinterService service)
            : base(PageKey.InstalledPrinters, "Установленные принтеры", "Очереди печати, зарегистрированные в Windows")
        {
            _service = service;
            _service.PropertyChanged += OnServiceChanged;
            _service.Printers.CollectionChanged += (s, e) =>
            {
                if (SelectedPrinter == null && _service.Printers.Count > 0)
                {
                    SelectedPrinter = _service.Printers[0];
                }

                RaiseStateChanged();
            };

            RefreshCommand = new RelayCommand(async () => await _service.RefreshAsync(), () => !_service.IsLoading);
        }

        public ObservableCollection<InstalledPrinter> Printers
        {
            get { return _service.Printers; }
        }

        public InstalledPrinter SelectedPrinter
        {
            get { return _selectedPrinter; }
            set
            {
                if (SetProperty(ref _selectedPrinter, value))
                {
                    OnPropertyChanged(nameof(HasSelectedPrinter));
                }
            }
        }

        public bool HasSelectedPrinter
        {
            get { return _selectedPrinter != null; }
        }

        public bool IsLoading
        {
            get { return _service.IsLoading; }
        }

        public bool HasError
        {
            get { return _service.HasError; }
        }

        public string ErrorMessage
        {
            get { return _service.ErrorMessage; }
        }

        public bool HasPrinters
        {
            get { return _service.Printers.Count > 0; }
        }

        public bool IsEmpty
        {
            get { return _service.HasLoaded && !_service.IsLoading && _service.Printers.Count == 0; }
        }

        public string StatusText
        {
            get
            {
                if (_service.IsLoading) return "Обновление списка…";
                if (_service.LastUpdated.HasValue)
                {
                    return "Принтеров: " + _service.Printers.Count + " · обновлено в " + _service.LastUpdated.Value.ToString("HH:mm:ss");
                }

                return "Список не загружен";
            }
        }

        public ICommand RefreshCommand { get; }

        public override void OnNavigatedTo()
        {
            if (!_service.HasLoaded && !_service.IsLoading)
            {
                var _ = _service.RefreshAsync();
            }
            else if (SelectedPrinter == null && _service.Printers.Count > 0)
            {
                SelectedPrinter = _service.Printers.FirstOrDefault();
            }
        }

        private void OnServiceChanged(object sender, PropertyChangedEventArgs e)
        {
            RaiseStateChanged();
            RelayCommand.RaiseCanExecuteChanged();
        }

        private void RaiseStateChanged()
        {
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasPrinters));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(StatusText));
        }
    }
}
