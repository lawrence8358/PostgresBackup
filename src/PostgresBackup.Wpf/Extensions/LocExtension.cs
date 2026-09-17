using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.Extensions;

/// <summary>
/// XAML 多語系標記擴充
/// 用法：Text="{l:Loc Settings_Title}"
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is DependencyObject)
        {
            return new Binding($"[{Key}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay
            }.ProvideValue(serviceProvider);
        }

        return LocalizationService.Instance[Key];
    }
}
