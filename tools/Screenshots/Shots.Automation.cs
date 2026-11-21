using OmniControl.App.Features.Automation;
using OmniControl.App.Features.Automation.Pages;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The Automation page with a few sample rules, a block and custom actions. No trigger here can fire while the tool runs in a way that
    /// matters: the service's run path is <see cref="NoRunner"/> and no step is a built-in mouse action (those would act directly); no rule
    /// wakes the PC (no wake timer). The service tries to register its pause key; while OmniControl runs it holds that key, so it fails.
    private static void Automation(Rig rig, Camera camera)
    {
        Use(ViewTemplate.For<AutomationPageViewModel, AutomationPage>(),
            ViewTemplate.For<AutomationItem, AutomationRuleRow>(),
            ViewTemplate.For<CustomActionItem, CustomActionRow>());

        var settings = new AutomationSettings();
        settings.Customs.Add(new CustomAction { Name = "Open project folder", Kind = CustomKind.Path, Target = @"C:\Projects" });
        settings.Customs.Add(new CustomAction { Name = "Flush DNS", Kind = CustomKind.Command, Target = "Clear-DnsClientCache" });
        settings.Customs.Add(new CustomAction { Name = "Status page", Kind = CustomKind.Url, Target = "https://example.com/status" });
        settings.Scripts.Add(new Script { Name = "Back up notes", Language = ScriptLanguage.PowerShell, Body = "Copy-Item $env:USERPROFILE\\Notes D:\\Backup -Recurse" });
        settings.Rules.Add(Rule("Lock when I walk away", AutomationKind.Do, "idle|min=10", new[] { "plugged" }, "omni:automation.power.lock"));
        settings.Rules.Add(Rule("Battery saver", AutomationKind.Do, "battery", Array.Empty<string>(), "omni:automation.power.efficiency"));
        settings.Rules.Add(Rule("Night backup", AutomationKind.Do, "time|h=23;m=30;d5=0;d6=0", Array.Empty<string>(),
            "omni:" + settings.Scripts[0].ActionId));
        settings.Rules.Add(Rule("Quiet hours", AutomationKind.Block, string.Empty, new[] { "between|fh=22;th=7" },
            "omni:automation.power.restart", "omni:automation.power.shutdown"));
        settings.Save();

        var service = new AutomationService(rig.Actions, rig.Runner, rig.Gate, rig.States, (_, _, _, _) => Task.FromResult(false),
            Array.Empty<string>, startedByWindows: false, Rig.Report, rig.Power, Rig.Ask);
        var page = new AutomationPageViewModel(service, Rig.Ask);
        rig.Pages.Add(page);
        camera.Page(page, "automation");
    }

    private static AutomationRule Rule(string name, AutomationKind kind, string trigger, string[] conditions, params string[] actions)
    {
        var rule = new AutomationRule { Name = name, Kind = kind, On = true, Trigger = trigger };
        foreach (var c in conditions) rule.AddCondition(c);
        foreach (var a in actions) rule.AddAction(a);
        return rule;
    }
}
