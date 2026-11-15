using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace vShell.Ui
{
    /// <summary>
    /// The taskbar's active-window line (and, optionally, the active button's highlight) as one shape that moves from button to button
    /// instead of each button drawing its own. Styles:
    ///   Slide - glides there;  Stretch - the leading end runs ahead, the trailing end catches up;  Fade - fades out, fades in there;
    ///   Shrink and grow - shrinks away under the old button, grows out of the middle of the new one;  Hop - arcs up over the buttons
    ///   in between;  Liquid - stretches and gets thinner, thickening again as it lands;  Off - jumps.
    /// Speed in ms; bounce None / Soft / Springy. With no active window the line shrinks away (and grows back when there is one).
    /// </summary>
    public sealed class TaskSlide
    {
        const double Thick = 2.5;         // the line's normal thickness
        const double HopHeight = 9;       // Hop: how high it arcs

        readonly Canvas _front = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
        readonly Canvas _back = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
        readonly Border _line = new Border { Height = Thick, CornerRadius = new CornerRadius(Thick / 2), Opacity = 0 };
        readonly Border _glow = new Border { Opacity = 0 };

        /// <summary>Over the buttons (the line) and behind them (the highlight).</summary>
        public UIElement Front { get { return _front; } }
        public UIElement Back { get { return _back; } }

        public string Mode = "Slide";
        public int Millis = 220;
        public string Bounce = "Soft";
        public bool Highlight;

        // where things are now / are going (x = left edge, r = right edge)
        double _lx, _lr, _ly, _lo, _lt = Thick;   // line: left, right, top, opacity, thickness
        double _gx, _gr, _gy, _gh, _go;           // highlight: left, right, top, height, opacity
        double _fLx, _fLr, _fLo, _fGx, _fGr, _fGy, _fGh, _fGo;
        double _tLx, _tLr, _tLy, _tLo, _tGx, _tGr, _tGy, _tGh, _tGo;
        bool _placed;
        DateTime _start;
        bool _running;
        bool _rightward;
        bool _moving;                             // going from one button to another (not appearing / disappearing)

        public TaskSlide()
        {
            _front.Children.Add(_line);
            _back.Children.Add(_glow);
        }

        public void SetLook(Brush line, Brush highlight, double corner)
        {
            _line.Background = line;
            _glow.Background = highlight;
            _glow.CornerRadius = new CornerRadius(corner);
            _glow.Visibility = Highlight ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>True while the line is on its way somewhere.</summary>
        public bool Running { get { return _running; } }

        /// <summary>
        /// The active button is now here (coordinates in the canvases' space): line from x to r at top y; highlight the button's box.
        /// has = false: no active button (shrink away). follow = it's the same button as before, it has just moved (the taskbar's own
        /// buttons are sliding about): keep going to the new place without starting over.
        /// </summary>
        public void MoveTo(bool has, double lx, double lr, double ly, Rect box, bool follow = false)
        {
            if (lr < lx) lx = lr = (lx + lr) / 2;
            double tLx, tLr, tLo, tGx, tGr, tGy, tGh, tGo;
            if (has)
            {
                tLx = lx; tLr = lr; tLo = 1;
                tGx = box.Left; tGr = box.Right; tGy = box.Top; tGh = box.Height; tGo = 1;
            }
            else
            {
                // shrink to the middle of where it is, fading
                double mid = (_tLx + _tLr) / 2, gmid = (_tGx + _tGr) / 2;
                tLx = tLr = mid; tLo = 0;
                tGx = tGr = gmid; tGy = _tGy; tGh = _tGh; tGo = 0;
                ly = _tLy;
            }
            if (_placed && Near(tLx, _tLx) && Near(tLr, _tLr) && Near(ly, _tLy) && Near(tLo, _tLo) && Near(tGx, _tGx) && Near(tGr, _tGr) &&
                Near(tGy, _tGy) && Near(tGh, _tGh) && Near(tGo, _tGo)) return;

            if (follow && has && _placed && _tLo > 0.5)
            {
                // same button, somewhere else: just aim at the new spot
                _tLx = tLx; _tLr = tLr; _tLy = ly; _tLo = tLo; _tGx = tGx; _tGr = tGr; _tGy = tGy; _tGh = tGh; _tGo = tGo;
                if (!_running) Jump();
                return;
            }

            bool first = !_placed || (_lo <= 0.01 && _go <= 0.01);
            if (first && has)
            {
                // appearing (nothing shown before): grow out of its own middle
                double mid = (lx + lr) / 2, gmid = (box.Left + box.Right) / 2;
                _lx = _lr = mid; _ly = ly; _lo = 0;
                _gx = _gr = gmid; _gy = box.Top; _gh = box.Height; _go = 0;
            }
            _placed = true;
            _moving = has && !first;
            _tLx = tLx; _tLr = tLr; _tLy = ly; _tLo = tLo; _tGx = tGx; _tGr = tGr; _tGy = tGy; _tGh = tGh; _tGo = tGo;
            _fLx = _lx; _fLr = _lr; _fLo = _lo; _fGx = _gx; _fGr = _gr; _fGy = _gy; _fGh = _gh; _fGo = _go;
            _rightward = (tLx + tLr) >= (_lx + _lr);

            if (Mode == "Off") { Jump(); return; }
            _start = DateTime.UtcNow;
            if (!_running)
            {
                _running = true;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        static bool Near(double a, double b) { return Math.Abs(a - b) < 0.5; }

        void Jump()
        {
            _lx = _tLx; _lr = _tLr; _ly = _tLy; _lo = _tLo; _lt = Thick;
            _gx = _tGx; _gr = _tGr; _gy = _tGy; _gh = _tGh; _go = _tGo;
            Apply();
        }

        IEasingFunction Ease()
        {
            switch (Bounce)
            {
                case "Soft": return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
                case "Springy": return new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 7 };
                default: return new CubicEase { EasingMode = EasingMode.EaseOut };
            }
        }

        static double Lerp(double a, double b, double p) { return a + (b - a) * p; }
        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double OutCubic(double p) { p = 1 - Clamp01(p); return 1 - p * p * p; }
        static double InCubic(double p) { p = Clamp01(p); return p * p * p; }

        /// <summary>The line's ends after el ms of a glide (lag = the back end takes 60 % longer: Stretch / Liquid).</summary>
        void LineAt(double el, bool lag, IEasingFunction ease, out double l, out double r)
        {
            double ms = Math.Max(60, Millis);
            double e1 = ease.Ease(Clamp01(el / ms)), e2 = lag ? ease.Ease(Clamp01(el / (ms * 1.6))) : e1;
            double eL = _rightward ? e2 : e1, eR = _rightward ? e1 : e2;
            l = Lerp(_fLx, _tLx, eL); r = Lerp(_fLr, _tLr, eR);
        }

        /// <summary>The highlight glides (it isn't part of the line's tricks).</summary>
        void GlowAt(double p, IEasingFunction ease)
        {
            double e = ease.Ease(Clamp01(p));
            _gx = Lerp(_fGx, _tGx, e); _gr = Lerp(_fGr, _tGr, e);
            _gy = Lerp(_fGy, _tGy, e); _gh = Lerp(_fGh, _tGh, e);
            _go = Lerp(_fGo, _tGo, Math.Min(1, p * 1.5));
        }

        void OnFrame(object sender, EventArgs e)
        {
            double ms = Math.Max(60, Millis);
            double el = (DateTime.UtcNow - _start).TotalMilliseconds;
            double p = Clamp01(el / ms);
            IEasingFunction ease = Ease();
            string mode = _moving ? Mode : "Slide";      // appearing / disappearing: always a plain grow / shrink
            bool done;
            _lt = Thick;

            switch (mode)
            {
                case "Fade":
                    // first half: fade out where it was; second half: fade in where it goes
                    if (p < 0.5)
                    {
                        double q = p / 0.5;
                        _lo = _fLo * (1 - q); _go = _fGo * (1 - q);
                    }
                    else
                    {
                        double q = (p - 0.5) / 0.5;
                        _lx = _tLx; _lr = _tLr; _ly = _tLy; _gx = _tGx; _gr = _tGr; _gy = _tGy; _gh = _tGh;
                        _lo = _tLo * q; _go = _tGo * q;
                    }
                    done = p >= 1;
                    break;

                case "Shrink and grow":
                    if (p < 0.5)
                    {
                        double q = InCubic(p / 0.5), mid = (_fLx + _fLr) / 2, gmid = (_fGx + _fGr) / 2;
                        _lx = Lerp(_fLx, mid, q); _lr = Lerp(_fLr, mid, q);
                        _gx = Lerp(_fGx, gmid, q); _gr = Lerp(_fGr, gmid, q); _gy = _fGy; _gh = _fGh;
                    }
                    else
                    {
                        double q = ease.Ease((p - 0.5) / 0.5), mid = (_tLx + _tLr) / 2, gmid = (_tGx + _tGr) / 2;
                        _lx = Lerp(mid, _tLx, q); _lr = Lerp(mid, _tLr, q);
                        _gx = Lerp(gmid, _tGx, q); _gr = Lerp(gmid, _tGr, q); _gy = _tGy; _gh = _tGh;
                    }
                    _ly = _tLy; _lo = _tLo; _go = _tGo;
                    done = p >= 1;
                    break;

                default:      // Slide, Stretch, Hop, Liquid
                    {
                        bool lag = mode == "Stretch" || mode == "Liquid";
                        LineAt(el, lag, ease, out _lx, out _lr);
                        _ly = _tLy;
                        _lo = Lerp(_fLo, _tLo, Math.Min(1, p * 1.5));      // fading goes a little quicker than the move
                        GlowAt(p, ease);
                        double pBack = lag ? Clamp01(el / (ms * 1.6)) : p;
                        done = p >= 1 && pBack >= 1;

                        if (mode == "Hop") _ly = _tLy - HopHeight * Math.Sin(Math.PI * p);
                        if (mode == "Liquid")
                        {
                            // thinner while stretched out, a little thicker while squashed (the bounce)
                            double w0 = Math.Max(1, _tLr - _tLx), w = Math.Max(1, Math.Abs(_lr - _lx));
                            _lt = Thick * Math.Max(0.35, Math.Min(1.35, Math.Sqrt(w0 / w)));
                        }
                        break;
                    }
            }
            Apply();
            if (done)
            {
                Jump();
                Stop();
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        void Apply()
        {
            double l = Math.Min(_lx, _lr), w = Math.Max(0, Math.Abs(_lr - _lx));
            double th = Math.Max(0.5, _lt);
            Canvas.SetLeft(_line, l);
            Canvas.SetTop(_line, _ly + (Thick - th));      // thicker / thinner around the same bottom edge
            _line.Width = w;
            _line.Height = th;
            _line.CornerRadius = new CornerRadius(th / 2);
            _line.Opacity = Math.Max(0, Math.Min(1, _lo));
            double gl = Math.Min(_gx, _gr), gw = Math.Max(0, Math.Abs(_gr - _gx));
            Canvas.SetLeft(_glow, gl);
            Canvas.SetTop(_glow, _gy);
            _glow.Width = gw;
            _glow.Height = Math.Max(0, _gh);
            _glow.Opacity = Highlight ? Math.Max(0, Math.Min(1, _go)) : 0;
        }
    }
}
