namespace OmniControl.Core.Engine.Trackpad;

/// <summary>One finger of a touchpad frame: its place as a share of the pad's longer side (so both axes use the same unit), and whether it touches.</summary>
public struct TouchContact
{
    public float X, Y;
    public bool Touching;
}

/// <summary>
/// Turns touchpad frames into gestures of <see cref="TrackpadGestures"/>. Pure (no Windows calls) and allocation-free: called for every
/// frame while fingers are on the pad. One gesture per touch: once one is recognised, nothing more until every finger has lifted;
/// a cooldown keeps a bouncing contact from counting as a new touch. When more fingers land, the start is taken again with all of
/// them; when fingers lift, nothing more is recognised for that touch (lifting is not a swipe).
/// </summary>
public sealed class GestureRecogniser
{
    /// <summary>How far the fingers' average must travel for a swipe, as a share of the pad's longer side.</summary>
    public const float SwipeDistance = 0.12f;

    /// <summary>A swipe must go this many times further along its axis than across it (a diagonal is not a swipe).</summary>
    public const float SwipeDominance = 1.5f;

    /// <summary>A pinch changes the distance between the two fingers by at least this share (in: to 1 / 1.25 of it, out: to 1.25 times).</summary>
    public const float PinchChange = 0.25f;

    /// <summary>Two fingers closer than this at the start (share of the longer side) are too close to measure a pinch from.</summary>
    public const float PinchMinSpread = 0.04f;

    private readonly long _cooldownMs;
    private int _fingers;        // fingers of this touch (the most seen so far)
    private bool _done;          // a gesture was recognised, or fingers began to lift
    private float _startX, _startY, _startSpread;
    private long _lastFrameMs = long.MinValue / 2, _lastGestureMs = long.MinValue / 2;

    public GestureRecogniser(TimeSpan cooldown) => _cooldownMs = (long)cooldown.TotalMilliseconds;

    /// <summary>Takes one complete frame (every contact the pad reported for one moment); returns the gesture it completes, or null.</summary>
    public TrackpadGesture? Feed(ReadOnlySpan<TouchContact> contacts, long nowMs)
    {
        if (nowMs - _lastFrameMs > CoreTiming.TrackpadTouchGapMs) Reset();
        _lastFrameMs = nowMs;

        var (fingers, x, y, spread) = Measure(contacts);
        if (fingers == 0)
        {
            Reset();
            return null;
        }
        if (fingers > _fingers)
        {
            _fingers = fingers;
            (_startX, _startY, _startSpread) = (x, y, spread);
            return null;
        }
        if (fingers < _fingers) _done = true; // lifting
        if (_done) return null;

        var motion = _fingers switch
        {
            3 or 4 => Swipe(x - _startX, y - _startY),
            2 => Pinch(spread),
            _ => null,
        };
        if (motion is not { } m || TrackpadGestures.Find(_fingers, m) is not { } gesture) return null;

        _done = true;
        if (nowMs - _lastGestureMs < _cooldownMs) return null;
        _lastGestureMs = nowMs;
        return gesture;
    }

    private void Reset()
    {
        _fingers = 0;
        _done = false;
    }

    /// <summary>The touching fingers: how many, their average place, and (for exactly two) the distance between them.</summary>
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

    /// <summary>The swipe's direction once it has gone far enough along one axis; a touchpad's Y grows downwards.</summary>
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
