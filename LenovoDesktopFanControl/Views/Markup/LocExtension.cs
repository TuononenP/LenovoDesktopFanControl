using System.Windows.Data;
using System.Windows.Markup;
using LenovoDesktopFanControl.Services;
using WpfBinding = System.Windows.Data.Binding;

namespace LenovoDesktopFanControl.Views.Markup;

[MarkupExtensionReturnType(typeof(object))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new WpfBinding
        {
            Source = Loc.Instance,
            Path = new System.Windows.PropertyPath($"[{Key}]"),
            Mode = BindingMode.OneWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };
        return binding.ProvideValue(serviceProvider);
    }
}