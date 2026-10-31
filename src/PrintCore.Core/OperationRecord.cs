using System;
using PrintCore.Core;

namespace PrintCore.Models
{
    /// <summary>Запись журнала операций.</summary>
    public class OperationRecord
    {
        public long Id { get; set; }
        public OperationKind Kind { get; set; }
        public OperationResult Result { get; set; } = OperationResult.Info;
        public string Title { get; set; }
        public string Details { get; set; }
        public string TargetDevice { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public bool IsError
        {
            get { return Kind == OperationKind.Error || Result == OperationResult.Error; }
        }

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case OperationKind.PrinterDiscovered: return "Обнаружение";
                    case OperationKind.DriverSearch: return "Поиск драйвера";
                    case OperationKind.DriverDownload: return "Загрузка";
                    case OperationKind.DriverInstall: return "Установка";
                    default: return "Ошибка";
                }
            }
        }

        public string ResultText
        {
            get
            {
                switch (Result)
                {
                    case OperationResult.Success: return "Успешно";
                    case OperationResult.Warning: return "Предупреждение";
                    case OperationResult.Error: return "Ошибка";
                    case OperationResult.Cancelled: return "Отменено";
                    default: return "Сведения";
                }
            }
        }

        public StatusTone ResultTone
        {
            get
            {
                switch (Result)
                {
                    case OperationResult.Success: return StatusTone.Success;
                    case OperationResult.Warning: return StatusTone.Warning;
                    case OperationResult.Error: return StatusTone.Danger;
                    case OperationResult.Cancelled: return StatusTone.Neutral;
                    default: return StatusTone.Accent;
                }
            }
        }

        public string TimeText
        {
            get
            {
                return Timestamp.Date == DateTime.Today
                    ? Timestamp.ToString("HH:mm:ss")
                    : Timestamp.ToString("dd.MM.yyyy HH:mm");
            }
        }

        public string FullTimeText
        {
            get { return Timestamp.ToString("dd.MM.yyyy HH:mm:ss"); }
        }
    }
}
