using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

/// <summary>One Windows power plan.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>One entry of a choice setting: the value Windows stores and its friendly name.</summary>
public sealed record PowerChoice(uint Index, string Name);

/// <summary>
/// One setting of a plan as Windows describes it: a number (<see cref="Choices"/> empty; <see cref="Units"/> as Windows names them, such as
/// "Seconds", "Minutes", "Percentage") with its limits, or a choice among <see cref="Choices"/>.
/// </summary>
public sealed record PowerSetting(Guid Id, string Name, string Units, uint Minimum, uint Maximum, uint Increment, IReadOnlyList<PowerChoice> Choices)
{
    public bool IsChoice => Choices.Count > 0;
}

/// <summary>A subgroup of settings (Display, Sleep, Processor power management...) and its visible settings.</summary>
public sealed record PowerSubgroup(Guid Id, string Name, IReadOnlyList<PowerSetting> Settings);

/// <summary>The value of one setting on mains power and on battery.</summary>
public readonly record struct PowerValue(uint PluggedIn, uint OnBattery);

/// <summary>What the power plan editor needs from Windows; the App talks to this, never to powrprof.</summary>
public interface IPowerPlans
{
    bool HasBattery { get; }
    IReadOnlyList<PowerPlan> Plans();
    IReadOnlyList<PowerSubgroup> Describe(Guid plan);

    /// <summary>Makes the plan the active one; throws <see cref="Win32Exception"/> when Windows refuses.</summary>
    void Activate(Guid plan);
    PowerValue Read(Guid plan, Guid subgroup, Guid setting);

    /// <summary>Writes the value (and re-applies the plan when it is the active one so it takes effect); throws <see cref="Win32Exception"/> when Windows refuses.</summary>
    void Write(Guid plan, Guid subgroup, Guid setting, bool onBattery, uint value);

    /// <summary>True for the plans Windows ships (Windows keeps their defaults; a plan someone made has none to go back to).</summary>
    bool CanRestoreDefaults(Guid plan);

    /// <summary>Puts every visible setting of the plan back to Windows' default; throws <see cref="Win32Exception"/> when Windows refuses.</summary>
    void RestoreDefaults(Guid plan);
}

/// <summary>Lists power plans and reads and writes their settings through powrprof, the way Windows' own power options dialog does (hidden settings are left out).</summary>
public sealed class PowerPlanService : IPowerPlans
{
    // The three plans Windows ships; each is also the "personality" its defaults are read by.
    private static readonly Guid[] Shipped =
    {
        new("381b4222-f694-41f0-9685-ff5bb260df2e"), new("a1841308-3541-4fab-bc81-f71556f20b4a"), new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"),
    };

    public bool HasBattery => PowerState.HasBattery();

    public IReadOnlyList<PowerPlan> Plans()
    {
        var active = PowerPlanNative.ActiveScheme();
        var plans = new List<PowerPlan>();
        for (uint i = 0; PowerPlanNative.EnumerateGuid(IntPtr.Zero, IntPtr.Zero, PowerPlanNative.AccessScheme, i) is { } id; i++)
            plans.Add(new PowerPlan(id, NameOf(id, null, null) ?? id.ToString(), id == active));
        return plans;
    }

    public IReadOnlyList<PowerSubgroup> Describe(Guid plan)
    {
        var groups = new List<PowerSubgroup>();
        using var scheme = new Pinned(plan);
        for (uint g = 0; PowerPlanNative.EnumerateGuid(scheme.Pointer, IntPtr.Zero, PowerPlanNative.AccessSubgroup, g) is { } group; g++)
        {
            var settings = new List<PowerSetting>();
            using var sub = new Pinned(group);
            for (uint s = 0; PowerPlanNative.EnumerateGuid(scheme.Pointer, sub.Pointer, PowerPlanNative.AccessIndividualSetting, s) is { } id; s++)
                if (!PowerPlanNative.IsHidden(group, id) && Describe(plan, group, id) is { } setting) settings.Add(setting);
            if (settings.Count > 0) groups.Add(new PowerSubgroup(group, NameOf(plan, group, null) ?? group.ToString(), settings));
        }
        return groups;
    }

    public void Activate(Guid plan) => Check(PowerPlanNative.PowerSetActiveScheme(IntPtr.Zero, plan));

    public PowerValue Read(Guid plan, Guid subgroup, Guid setting)
    {
        Check(PowerPlanNative.PowerReadACValueIndex(IntPtr.Zero, plan, subgroup, setting, out var ac));
        Check(PowerPlanNative.PowerReadDCValueIndex(IntPtr.Zero, plan, subgroup, setting, out var dc));
        return new PowerValue(ac, dc);
    }

    public void Write(Guid plan, Guid subgroup, Guid setting, bool onBattery, uint value)
    {
        Check(onBattery
            ? PowerPlanNative.PowerWriteDCValueIndex(IntPtr.Zero, plan, subgroup, setting, value)
            : PowerPlanNative.PowerWriteACValueIndex(IntPtr.Zero, plan, subgroup, setting, value));
        Apply(plan);
    }

    public bool CanRestoreDefaults(Guid plan) => Shipped.Contains(plan);

    public void RestoreDefaults(Guid plan)
    {
        if (!CanRestoreDefaults(plan)) throw new InvalidOperationException("Windows keeps no defaults for this plan.");
        foreach (var group in Describe(plan))
            foreach (var setting in group.Settings)
            {
                if (PowerPlanNative.PowerReadACDefaultIndex(IntPtr.Zero, plan, group.Id, setting.Id, out var ac) == 0)
                    Check(PowerPlanNative.PowerWriteACValueIndex(IntPtr.Zero, plan, group.Id, setting.Id, ac));
                if (PowerPlanNative.PowerReadDCDefaultIndex(IntPtr.Zero, plan, group.Id, setting.Id, out var dc) == 0)
                    Check(PowerPlanNative.PowerWriteDCValueIndex(IntPtr.Zero, plan, group.Id, setting.Id, dc));
            }
        Apply(plan);
    }

    // A write only takes effect once the active plan is set again.
    private static void Apply(Guid plan)
    {
        if (PowerPlanNative.ActiveScheme() == plan) Check(PowerPlanNative.PowerSetActiveScheme(IntPtr.Zero, plan));
    }

    private static PowerSetting? Describe(Guid plan, Guid group, Guid id)
    {
        var name = NameOf(plan, group, id);
        if (name is null) return null; // Windows gives it no name: it is not shown in its own dialog either
        var choices = new List<PowerChoice>();
        for (uint i = 0; ; i++)
        {
            uint size = 4, type;
            var value = new byte[4];
            if (PowerPlanNative.PowerReadPossibleValue(IntPtr.Zero, group, id, out type, i, value, ref size) != 0) break;
            var label = PowerPlanNative.ReadText((byte[]? b, ref uint n) => PowerPlanNative.PowerReadPossibleFriendlyName(IntPtr.Zero, group, id, i, b, ref n));
            choices.Add(new PowerChoice(BitConverter.ToUInt32(value, 0), label ?? BitConverter.ToUInt32(value, 0).ToString()));
        }
        if (choices.Count > 0) return new PowerSetting(id, name, string.Empty, 0, 0, 1, choices);
        if (PowerPlanNative.PowerReadValueMin(IntPtr.Zero, group, id, out var min) != 0 || PowerPlanNative.PowerReadValueMax(IntPtr.Zero, group, id, out var max) != 0) return null;
        if (PowerPlanNative.PowerReadValueIncrement(IntPtr.Zero, group, id, out var step) != 0 || step == 0) step = 1;
        var units = PowerPlanNative.ReadText((byte[]? b, ref uint n) => PowerPlanNative.PowerReadValueUnitsSpecifier(IntPtr.Zero, group, id, b, ref n)) ?? string.Empty;
        return new PowerSetting(id, name, units, min, max, step, Array.Empty<PowerChoice>());
    }

    /// <summary>The friendly name of a plan (no subgroup), a subgroup (no setting) or a setting.</summary>
    private static string? NameOf(Guid plan, Guid? group, Guid? setting)
    {
        using var p = new Pinned(plan);
        using var g = new Pinned(group);
        using var s = new Pinned(setting);
        return PowerPlanNative.ReadText((byte[]? b, ref uint n) => PowerPlanNative.PowerReadFriendlyName(IntPtr.Zero, p.Pointer, g.Pointer, s.Pointer, b, ref n));
    }

    private static void Check(uint code)
    {
        if (code != 0) throw new Win32Exception((int)code);
    }

    /// <summary>A GUID in unmanaged memory for the calls that take an optional GUID pointer (zero when there is none).</summary>
    private sealed class Pinned : IDisposable
    {
        public IntPtr Pointer { get; }

        public Pinned(Guid? value)
        {
            if (value is not { } guid) return;
            Pointer = Marshal.AllocHGlobal(16);
            Marshal.StructureToPtr(guid, Pointer, false);
        }

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer);
        }
    }
}
