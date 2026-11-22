namespace OmniControl.App.Features.OtherPcs;

/// How one check of a checklist stands: not known yet (an earlier one failed, or no PC is picked; also a plain reminder), fine, or failing.
public enum CheckState { Pending, Ok, Failed }

/// One line of a checklist (Tailscale's, or "Can't reach it on this network?"): what it asks, how it stands, a short fact for its right side
/// (the version, this PC's address), and the one fix (words empty when there is none; a pill when <paramref name="PillFix"/>, else a link).
public sealed record CheckRow(string Id, string Words, CheckState State, string Tag, string TagTip, string Sentence, string Fix, bool PillFix = false);
