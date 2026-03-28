using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Behaviors;

/// <summary>
/// Selects the appropriate <see cref="DataTemplate"/> for a <see cref="ParameterViewModel"/>
/// based on the parameter's runtime type.
/// </summary>
public sealed class ParameterTemplateSelector : DataTemplateSelector
{
    public DataTemplate? NumericTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? BoolTemplate { get; set; }
    public DataTemplate? EnumTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ParameterViewModel param)
            return base.SelectTemplate(item, container);

        if (param.ParameterType == typeof(bool))
            return BoolTemplate ?? base.SelectTemplate(item, container);

        if (param.ParameterType.IsEnum)
            return EnumTemplate ?? base.SelectTemplate(item, container);

        if (IsNumeric(param.ParameterType))
            return NumericTemplate ?? base.SelectTemplate(item, container);

        return TextTemplate ?? base.SelectTemplate(item, container);
    }

    private static bool IsNumeric(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(int)
            || underlying == typeof(long)
            || underlying == typeof(double)
            || underlying == typeof(float)
            || underlying == typeof(decimal)
            || underlying == typeof(short)
            || underlying == typeof(byte);
    }
}
