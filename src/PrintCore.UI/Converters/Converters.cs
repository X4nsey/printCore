using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PrintCore.Models;

namespace PrintCore.Converters
{
    /// <summary>Ключ ресурса → объект ресурса (например, геометрия иконки).</summary>
    public class ResourceKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = value as string;
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return Application.Current.TryFindResource(key) ?? DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>bool → Visibility. Параметр "Invert" инвертирует значение.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;
            if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Строка → Visibility (пустая строка скрывает элемент).</summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Состояние драйвера → текст.</summary>
    public class DriverStatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value is DriverStatus s ? s : DriverStatus.Unknown)
            {
                case DriverStatus.Searching: return "Поиск…";
                case DriverStatus.NotFound: return "Не найден";
                case DriverStatus.Available: return "Доступен";
                case DriverStatus.Downloaded: return "Загружен";
                case DriverStatus.Installed: return "Установлен";
                case DriverStatus.Error: return "Ошибка";
                default: return "Не проверен";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Состояние драйвера → тон бейджа.</summary>
    public class DriverStatusToToneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value is DriverStatus s ? s : DriverStatus.Unknown)
            {
                case DriverStatus.Searching:
                case DriverStatus.Available:
                case DriverStatus.Downloaded:
                    return StatusTone.Accent;
                case DriverStatus.Installed:
                    return StatusTone.Success;
                case DriverStatus.NotFound:
                    return StatusTone.Warning;
                case DriverStatus.Error:
                    return StatusTone.Danger;
                default:
                    return StatusTone.Neutral;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Тип операции → геометрия иконки.</summary>
    public class OperationKindToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key;
            switch (value is OperationKind k ? k : OperationKind.Error)
            {
                case OperationKind.PrinterDiscovered: key = "Icon.Printer"; break;
                case OperationKind.DriverSearch: key = "Icon.Search"; break;
                case OperationKind.DriverDownload: key = "Icon.Download"; break;
                case OperationKind.DriverInstall: key = "Icon.CheckCircle"; break;
                default: key = "Icon.Alert"; break;
            }

            return Application.Current.TryFindResource(key) as Geometry;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
