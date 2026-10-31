using System.Windows;
using System.Windows.Media;

namespace PrintCore.Controls
{
    /// <summary>
    /// Вложенные свойства дизайн-системы: позволяют задавать иконку и описание
    /// для стандартных элементов (кнопок, пунктов меню) без дублирования шаблонов.
    /// </summary>
    public static class Assist
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
            "Icon", typeof(Geometry), typeof(Assist), new FrameworkPropertyMetadata(null));

        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
            "Description", typeof(string), typeof(Assist), new FrameworkPropertyMetadata(null));

        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
            "CornerRadius", typeof(CornerRadius), typeof(Assist), new FrameworkPropertyMetadata(new CornerRadius(6)));

        public static Geometry GetIcon(DependencyObject element)
        {
            return (Geometry)element.GetValue(IconProperty);
        }

        public static void SetIcon(DependencyObject element, Geometry value)
        {
            element.SetValue(IconProperty, value);
        }

        public static string GetDescription(DependencyObject element)
        {
            return (string)element.GetValue(DescriptionProperty);
        }

        public static void SetDescription(DependencyObject element, string value)
        {
            element.SetValue(DescriptionProperty, value);
        }

        public static CornerRadius GetCornerRadius(DependencyObject element)
        {
            return (CornerRadius)element.GetValue(CornerRadiusProperty);
        }

        public static void SetCornerRadius(DependencyObject element, CornerRadius value)
        {
            element.SetValue(CornerRadiusProperty, value);
        }
    }
}
