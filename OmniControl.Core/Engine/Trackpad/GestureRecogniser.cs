namespace OmniControl.Core.Engine.Trackpad;

/// One finger of a touchpad frame: its place as a share of the pad's longer side (so both axes use the same unit), and whether it touches.
public struct TouchContact
{
    public float X, Y;
    public bool Touching;
}

/// Turns touchpad frames into gestures of <see cref="TrackpadGestures"/>. Pure (no Windows calls) and allocation-free: called for every
/// frame while fingers are on the pad. One gesture per touch: once one is recognised, nothing more until every finger has lifted;
/// a cooldown keeps a bouncing contact from counting as a new touch. When more fingers land, the start is taken again with all of
/// them; when fingers lift, nothing more is recognised for that touch (lifting is not a swipe).
/// A touch that stays put and lifts quickly is a tap; a second tap with the same fingers soon after is a double tap (it replaces that
/// second tap).
public sealed class GestureRecogniser
{
    /// How far the fingers' average must travel for a swipe, as a share of the pad's longer side.
    public const float SwipeDistance = 0.12f;

    /// A swipe must go this many times further along its axis than across it (a diagonal is not a swipe).
    public const float SwipeDominance = 1.5f;

    /// A pinch changes the distance between the two fingers by at least this share (in: to 1 / 1.25 of it, out: to 1.25 times).
    public const float PinchChange = 0.25f;

    /// Two fingers closer than this at the start (share of the longer side) are too close to measure a pinch from.
    public const float PinchMinSpread = 0.04f;

    /// A tap's fingers may drift this far (share of the pad's longer side), and two fingers may change their distance this much.
    public const float TapMoveDistance = 0.03f;

    private readonly long _cooldownMs;
    private bool _tapSpoiled;    // this touch moved, landed slowly or became a swipe or pinch: not a tap
    private long _touchStartMs, _lastTapMs = long.MinValue / 2;
    private int _lastTapFingers;
    private int _fingers;        // fingers of this touch (the most seen so far)
    private bool _done;          // a gesture was recognised, or fingers began to lift
    private float _startX, _startY, _startSpread;
    private long _lastFrameMs = long.MinValue / 2, _lastGestureMs = long.MinValue / 2;

    public GestureRecogniser(TimeSpan cooldown) => _cooldownMs = (long)cooldown.TotalMilliseconds;

    /// Takes one complete frame (every contact the pad reported for one moment); returns the gesture it completes, or null.
    public TrackpadGesture? Feed(ReadOnlySpan<TouchContact> contacts, long nowMs)
    {
        if (nowMs - _lastFrameMs > CoreTiming.TrackpadTouchGapMs) Reset();
        _lastFrameMs = nowMs;

        var (fingers, x, y, spread) = Measure(contacts);
        if (fingers == 0)
        {
            var tap = TapEnded(nowMs);
            Reset();
            return tap;
        }
        if (fingers > _fingers)
        {
            if (_fingers == 0) (_touchStartMs, _tapSpoiled) = (nowMs, false);
            else if (nowMs - _touchStartMs > CoreTiming.TrackpadTapLandMs) _tapSpoiled = true;
            _fingers = fingers;
            (_startX, _startY, _startSpread) = (x, y, spread);
            return null;
        }
        if (fingers < _fingers) _done = true; // lifting
        else if (Drifted(x, y, spread)) _tapSpoiled = true;
        if (_done) return null;

        var motion = _fingers switch
        {
            3 or 4 => Swipe(x - _startX, y - _startY),
            2 => Pinch(spread),
            _ => null,
        };
        if (motion is not { } m || TrackpadGestures.Find(_fingers, m) is not { } gesture) return null;

        _done = _tapSpoiled = true;
        if (nowMs - _lastGestureMs < _cooldownMs) return null;
        _lastGestureMs = nowMs;
        return gesture;
    }

    private bool Drifted(float x, float y, float spread) =>
        MathF.Sqrt((x - _startX) * (x - _startX) + (y - _startY) * (y - _startY)) > TapMoveDistance
        || (_fingers == 2 && MathF.Abs(spread - _startSpread) > TapMoveDistance);

    /// All fingers have lifted: the tap (or double tap) this touch was, or null. Taps skip the cooldown: their own timing paces them.
    private TrackpadGesture? TapEnded(long nowMs)
    {
        if (_fingers == 0 || _tapSpoiled || nowMs - _touchStartMs > CoreTiming.TrackpadTapMaxMs) return null;
        var isDouble = _fingers == _lastTapFingers && nowMs - _lastTapMs <= CoreTiming.TrackpadDoubleTapMs;
        (_lastTapFingers, _lastTapMs) = isDouble ? (0, long.MinValue / 2) : (_fingers, nowMs); // a double tap uses up both taps
        return TrackpadGestures.Find(_fingers, isDouble ? GestureMotion.DoubleTap : GestureMotion.Tap);
    }

    private void Reset()
    {
        _fingers = 0;
        _done = false;
    }

    /// The touching fingers: how many, their average place, and (for exactly two) the distance between them.
    private static (int Fingers, float X, float Y, float Spread) Measure(ReadOnlySpan<TouchContact> contacts)
    {
        int fingers = 0;
        float sumX = 0, sumY = 0, firstX = 0, firstY = 0, spread = 0;
        foreach (var c in contacts)
        {
            if (!c.Touching) continue;
            if (fingers == 0) (firstX, firstY) = (c.X, c.Y);
            else if (fingers == 1) spread = MathF.Sqrt((c.X - firstX) * (c.X - firstX) + (c.Y - firstY) * (c.Y - firstY));
            fingers++;
            sumX += c.X;
            sumY += c.Y;
        }
        return fingers == 0 ? (0, 0, 0, 0) : (fingers, sumX / fingers, sumY / fingers, fingers == 2 ? spread : 0);
    }

    /// The swipe's direction once it has gone far enough along one axis; a touchpad's Y grows downwards.
    private static GestureMotion? Swipe(float dx, float dy)
    {
        float ax = MathF.Abs(dx), ay = MathF.Abs(dy);
        if (ax >= SwipeDistance && ax >= ay * SwipeDominance) return dx > 0 ? GestureMotion.Right : GestureMotion.Left;
        if (ay >= SwipeDistance && ay >= ax * SwipeDominance) return dy > 0 ? GestureMotion.Down : GestureMotion.Up;
        return null;
    }

    private GestureMotion? Pinch(float spread)
    {
        if (_startSpread < PinchMinSpread) return null;
        var ratio = spread / _startSpread;
        if (ratio >= 1 + PinchChange) return GestureMotion.PinchOut;
        if (ratio <= 1 / (1 + PinchChange)) return GestureMotion.PinchIn;
        return null;
    }
}
