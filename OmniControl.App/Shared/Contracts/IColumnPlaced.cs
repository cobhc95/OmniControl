namespace OmniControl.App.Shared.Contracts;

/// What a block says about where it sits: the fixed column it always belongs to (the panel reads it from the block's data context).
public interface IColumnPlaced
{
    int Column { get; }
}
