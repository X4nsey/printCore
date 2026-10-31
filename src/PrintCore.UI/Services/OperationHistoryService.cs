using System;
using System.Collections.ObjectModel;
using PrintCore.Core;
using PrintCore.Data;
using PrintCore.Infrastructure;
using PrintCore.Logging;
using PrintCore.Models;

namespace PrintCore.Services
{
    /// <summary>
    /// Журнал операций приложения с сохранением в таблицу OperationHistory SQLite.
    /// Новые записи помещаются в начало списка.
    /// </summary>
    public class OperationHistoryService : ObservableObject
    {
        private readonly PrintCoreDatabase _db;
        private readonly IAppLogger _logger;

        public OperationHistoryService(PrintCoreDatabase db = null, IAppLogger logger = null)
        {
            _db = db;
            _logger = logger;
            LoadFromDatabase();
        }

        public ObservableCollection<OperationRecord> Records { get; } = new ObservableCollection<OperationRecord>();

        public void LoadFromDatabase()
        {
            if (_db == null) return;
            try
            {
                var list = _db.GetOperationHistory(250);
                Records.Clear();
                foreach (var item in list)
                {
                    Records.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn("History", "Не удалось загрузить историю операций из БД: " + ex.Message);
            }
        }

        public void Add(OperationRecord record)
        {
            if (record == null) return;
            try
            {
                _db?.InsertOperation(record);
            }
            catch (Exception ex)
            {
                _logger?.Warn("History", "Не удалось записать операцию в SQLite: " + ex.Message);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => Records.Insert(0, record));
            }
            else
            {
                Records.Insert(0, record);
            }
        }

        public void Log(OperationKind kind, OperationResult result, string title, string details = null, string targetDevice = null)
        {
            Add(new OperationRecord
            {
                Kind = kind,
                Result = result,
                Title = title,
                Details = details,
                TargetDevice = targetDevice,
                Timestamp = DateTime.Now
            });
        }

        public void Clear()
        {
            try
            {
                _db?.ClearOperationHistory();
            }
            catch (Exception ex)
            {
                _logger?.Warn("History", "Не удалось очистить таблицу OperationHistory: " + ex.Message);
            }

            Records.Clear();
        }
    }
}
