using System.Windows.Data;
using System.Windows.Markup;

namespace AionDPS.Ui;

/// <summary>
/// XAML usage: `Header="{loc:Loc MenuFile}"` (with `xmlns:loc="clr-namespace:AionDPS.Ui"` on
/// the window root). Resolves to a live one-way binding against
/// <see cref="LocalizationManager.Instance"/>'s indexer, not a one-shot string lookup -- so
/// changing the language in Settings repaints this element immediately, the same way any other
/// data-bound value would, with no extra code in the window behind it.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
