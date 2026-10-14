using System.Windows.Controls;
using GoodieBag.Config;

namespace GoodieBag.Automations;

internal abstract class PluginTrigger
{
    public abstract string Id { get; }

    public abstract string Label { get; }

    public abstract string Help { get; }

    public virtual int DefaultThreshold => 0;

    public virtual bool IsOn => true;

    public virtual string OffNote => "Its plugin's feature is switched off.";

    public abstract DateTime? NextDue(AutomationRule rule, DateTime from);

    public abstract string Summary(AutomationRule rule);

    public virtual string NothingDue => " · not due again";

    public virtual void BuildEditor(AutomationRule rule, Panel into) { }

    public virtual void ClearOptions(AutomationRule rule) { }

    public virtual void SetInUse(bool inUse) { }
}
