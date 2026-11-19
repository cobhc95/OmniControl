namespace OmniControl.App.Features.Volume.Pages;

/// A step a button runs (Install Equalizer APO, Choose devices, Install VB-CABLE...); <c>Main</c> marks the one that does the job.
public sealed record CardAction(string Label, string Tip, IRelayCommand Command, bool Main = false);
