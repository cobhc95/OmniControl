namespace OmniControl.Core.Models;

/// <summary>Success or failure of a call into Windows, with a message to show when it failed.</summary>
public sealed class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public static OperationResult Ok(string msg = "") => new() { Success = true, Message = msg };
    public static OperationResult Fail(string msg) => new() { Success = false, Message = msg };
}
