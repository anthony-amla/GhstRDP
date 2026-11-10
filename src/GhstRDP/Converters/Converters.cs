using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GhstRDP.ViewModels;

namespace GhstRDP.Converters;

/// <summary>true → Visible. Parâmetro "invert" inverte. <see cref="UseHidden"/> usa Hidden no lugar de Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool UseHidden { get; set; }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>número × fator (ConverterParameter). Usado para a prévia manter a proporção da largura do card.</summary>
public sealed class RatioConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var factor = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1.0;
        return value is double d && d > 0 ? Math.Round(d * factor) : 120.0;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible só quando todos os valores são true.</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>int == 0 → true.</summary>
public sealed class IsZeroConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value is 0;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Enum ↔ bool para RadioButtons. ConverterParameter = nome do valor.</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && parameter is string p && value.ToString() == p;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string p)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return Enum.Parse(enumType, p);
        }
        return Binding.DoNothing;
    }
}

/// <summary>Enum → Visibility. ConverterParameter = nome do valor (aceita "A|B").</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public bool UseHidden { get; set; }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var match = value != null && parameter is string p && p.Split('|').Contains(value.ToString());
        return match ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>int → Visible quando > 0 (ou == 0 com "invert").</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var any = value is int n && n > 0;
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase)) any = !any;
        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>string não vazia → Visible.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusToBrushConverter : IValueConverter
{
    static readonly Brush Online = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)));
    static readonly Brush Offline = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
    static readonly Brush Checking = Freeze(new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x6C)));
    static readonly Brush Unknown = Freeze(new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x6C)));
    static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)));

    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ServerStatus.Online => Online,
        ServerStatus.Offline => Offline,
        ServerStatus.Verificando => Checking,
        SessionState.Conectado => Online,
        SessionState.Conectando => Accent,
        SessionState.Reconectando => Checking,
        SessionState.Erro => Offline,
        SessionState.Desconectado => Unknown,
        _ => Unknown,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
