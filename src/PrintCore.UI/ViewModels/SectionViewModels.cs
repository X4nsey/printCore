using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PrintCore.Core;
using PrintCore.Infrastructure;
using PrintCore.Models;
using PrintCore.Services;

namespace PrintCore.ViewModels
{
    /// <summary>Описание возможности (используется шаблоном PlannedFeature в дизайн-системе).</summary>
    public class PlannedFeature
    {
        public PlannedFeature(string iconKey, string title, string description)
        {
            IconKey = iconKey;
            Title = title;
            Description = description;
        }

        public string IconKey { get; }
        public string Title { get; }
        public string Description { get; }
    }

    /// <summary>Раздел «История операций»: фильтрация по типу и результату, просмотр подробностей.</summary>
    public class OperationHistoryViewModel : PageViewModel
    {
        private readonly OperationHistoryService _history;
        private string _kindFilter = "All";
        private string _resultFilter = "All";
        private OperationRecord _selectedRecord;

        public OperationHistoryViewModel(OperationHistoryService history)
            : base(PageKey.OperationHistory, "История операций", "Журнал обнаружения, поиска, загрузки и установки драйверов")
        {
            _history = history;
            _history.Records.CollectionChanged += (s, e) => RebuildFiltered();

            SetKindFilterCommand = new RelayCommand(p => KindFilter = p as string ?? "All");
            SetResultFilterCommand = new RelayCommand(p => ResultFilter = p as string ?? "All");
            ClearCommand = new RelayCommand(ClearWithConfirmation, () => _history.Records.Count > 0);

            RebuildFiltered();
        }

        public ObservableCollection<OperationRecord> FilteredRecords { get; } = new ObservableCollection<OperationRecord>();

        public bool HasRecords
        {
            get { return FilteredRecords.Count > 0; }
        }

        public OperationRecord SelectedRecord
        {
            get { return _selectedRecord; }
            set
            {
                if (SetProperty(ref _selectedRecord, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRecord));
                }
            }
        }

        public bool HasSelectedRecord
        {
            get { return _selectedRecord != null; }
        }

        public string KindFilter
        {
            get { return _kindFilter; }
            set
            {
                if (SetProperty(ref _kindFilter, value))
                {
                    OnPropertyChanged(nameof(IsKindAll));
                    OnPropertyChanged(nameof(IsKindDiscovery));
                    OnPropertyChanged(nameof(IsKindSearch));
                    OnPropertyChanged(nameof(IsKindDownload));
                    OnPropertyChanged(nameof(IsKindInstall));
                    OnPropertyChanged(nameof(IsKindError));
                    RebuildFiltered();
                }
            }
        }

        public bool IsKindAll { get { return _kindFilter == "All"; } }
        public bool IsKindDiscovery { get { return _kindFilter == "Discovery"; } }
        public bool IsKindSearch { get { return _kindFilter == "Search"; } }
        public bool IsKindDownload { get { return _kindFilter == "Download"; } }
        public bool IsKindInstall { get { return _kindFilter == "Install"; } }
        public bool IsKindError { get { return _kindFilter == "Error"; } }

        public string ResultFilter
        {
            get { return _resultFilter; }
            set
            {
                if (SetProperty(ref _resultFilter, value))
                {
                    OnPropertyChanged(nameof(IsResultAll));
                    OnPropertyChanged(nameof(IsResultSuccess));
                    OnPropertyChanged(nameof(IsResultWarning));
                    OnPropertyChanged(nameof(IsResultError));
                    RebuildFiltered();
                }
            }
        }

        public bool IsResultAll { get { return _resultFilter == "All"; } }
        public bool IsResultSuccess { get { return _resultFilter == "Success"; } }
        public bool IsResultWarning { get { return _resultFilter == "Warning"; } }
        public bool IsResultError { get { return _resultFilter == "Error"; } }

        public ICommand SetKindFilterCommand { get; }
        public ICommand SetResultFilterCommand { get; }
        public ICommand ClearCommand { get; }

        private void ClearWithConfirmation()
        {
            if (_history.Records.Count == 0) return;
            var res = MessageBox.Show(
                "Очистить весь журнал операций PrintCore?",
                "Подтверждение очистки журнала",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _history.Clear();
                SelectedRecord = null;
            }
        }

        private void RebuildFiltered()
        {
            FilteredRecords.Clear();
            foreach (var r in _history.Records)
            {
                if (_kindFilter == "Discovery" && r.Kind != OperationKind.PrinterDiscovered) continue;
                if (_kindFilter == "Search" && r.Kind != OperationKind.DriverSearch) continue;
                if (_kindFilter == "Download" && r.Kind != OperationKind.DriverDownload) continue;
                if (_kindFilter == "Install" && r.Kind != OperationKind.DriverInstall) continue;
                if (_kindFilter == "Error" && !r.IsError) continue;

                if (_resultFilter == "Success" && r.Result != OperationResult.Success) continue;
                if (_resultFilter == "Warning" && r.Result != OperationResult.Warning) continue;
                if (_resultFilter == "Error" && !r.IsError) continue;

                FilteredRecords.Add(r);
            }

            OnPropertyChanged(nameof(HasRecords));
            RelayCommand.RaiseCanExecuteChanged();

            if (SelectedRecord == null && FilteredRecords.Count > 0)
            {
                SelectedRecord = FilteredRecords.FirstOrDefault();
            }
        }
    }
}
