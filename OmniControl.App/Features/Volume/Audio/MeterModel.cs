namespace OmniControl.App.Features.Volume.Audio;

/// What one level meter shows: the left and right level (smoothed so it falls gently), a peak-hold mark and a clip light.
/// Same ballistics idea as TabForge's meter model. No UI here: <c>LevelMeter</c> draws it, the Volume page feeds it.
public sealed class MeterModel
{
    public const double FloorDb = -60, WarmDb = -12;
    /// A move smaller than this is under a pixel of the bar: it is kept but not repainted until it adds up to one.
    private const double HoldMs = 1200, FallDbPerSecond = 28, ClipMs = 2500, MaxFrameMs = 250, MsPerSecond = 1000, RedrawDb = 0.5;

    private double _lastMs;
    private double _holdLeftAt, _holdRightAt, _clipAt;
    private double _left = FloorDb, _right = FloorDb, _holdLeft = FloorDb, _holdRight = FloorDb;
    private double _drawnLeft = FloorDb, _drawnRight = FloorDb, _drawnHoldLeft = FloorDb, _drawnHoldRight = FloorDb;

    public double LeftDb => _left;
    public double RightDb => _right;
    public double HoldLeftDb => _holdLeft;
    public double HoldRightDb => _holdRight;
    public bool ClipLit { get; private set; }

    /// Nothing to see: both levels and holds at the floor and the clip light off.
    public bool IsSilent => _left <= FloorDb && _right <= FloorDb && _holdLeft <= FloorDb && _holdRight <= FloorDb && !ClipLit;

    /// Raised when the picture changed (the control repaints).
    public event Action? Changed;

    /// 0 at the floor, 1 at 0 dBFS.
    public static double Fraction(double db) => Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);

    private static double ToDb(float peak) => peak <= 0.000001f ? FloorDb : Math.Max(FloorDb, 20 * Math.Log10(peak));

    /// Takes the newest peak values (0 to 1; both 0 when there is no sound or no reading).
    public void Feed(float left, float right, double nowMs)
    {
        var dt = _lastMs == 0 ? 0 : Math.Min(MaxFrameMs, nowMs - _lastMs) / MsPerSecond;
        _lastMs = nowMs;
        Move(ToDb(left), dt, nowMs, ref _holdLeftAt, ref _left, ref _holdLeft); // by ref: nothing is allocated per frame
        Move(ToDb(right), dt, nowMs, ref _holdRightAt, ref _right, ref _holdRight);
        var changed = Math.Abs(_left - _drawnLeft) > RedrawDb || Math.Abs(_right - _drawnRight) > RedrawDb
            || Math.Abs(_holdLeft - _drawnHoldLeft) > RedrawDb || Math.Abs(_holdRight - _drawnHoldRight) > RedrawDb;
        if (changed) (_drawnLeft, _drawnRight, _drawnHoldLeft, _drawnHoldRight) = (_left, _right, _holdLeft, _holdRight);

        var clip = left >= 0.999f || right >= 0.999f;
        if (clip) _clipAt = nowMs;
        var lit = clip || (ClipLit && nowMs - _clipAt < ClipMs);
        if (lit != ClipLit) { ClipLit = lit; changed = true; }

        if (changed) Changed?.Invoke();
    }

    /// Back to silence (the row went away or the page is hidden).
    public void Clear()
    {
        _left = _right = _holdLeft = _holdRight = _drawnLeft = _drawnRight = _drawnHoldLeft = _drawnHoldRight = FloorDb;
        ClipLit = false;
        _lastMs = 0;
        Changed?.Invoke();
    }

    private static void Move(double db, double dt, double now, ref double holdAt, ref double shown, ref double hold)
    {
        var newShown = db >= shown ? db : Math.Max(db, shown - FallDbPerSecond * dt);
        var newHold = hold;
        if (newShown >= hold) { newHold = newShown; holdAt = now; }
        else if (now - holdAt > HoldMs) newHold = Math.Max(newShown, hold - FallDbPerSecond * dt);
        shown = newShown;
        hold = newHold;
    }
}
