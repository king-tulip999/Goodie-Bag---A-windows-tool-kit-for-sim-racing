using System.Drawing;
using System.Runtime.InteropServices;

namespace GoodieBag.Overlay;

internal sealed class PresentGuard
{
    private byte[]? _last, _now;
    private Point _at;
    private byte _opacity;
    private bool _valid;

    public void Reset() => _valid = false;

    public void Release()
    {
        _valid = false;
        _last = _now = null;
    }

    public bool Same(IntPtr bits, int length, Point at, byte opacity)
    {
        if (bits == IntPtr.Zero || length <= 0)
        {
            _valid = false;
            return false;
        }
        if (_now?.Length != length) _now = new byte[length];
        if (_last?.Length != length)
        {
            _last = new byte[length];
            _valid = false;
        }
        Marshal.Copy(bits, _now, 0, length);
        if (_valid && at == _at && opacity == _opacity && _now.AsSpan().SequenceEqual(_last)) return true;
        (_last, _now) = (_now, _last);
        _at = at;
        _opacity = opacity;
        _valid = true;
        return false;
    }
}
