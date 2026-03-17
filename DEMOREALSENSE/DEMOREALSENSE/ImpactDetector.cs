using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecteur de rebond — vecteur vitesse + inversion brusque.
    ///
    /// CORRECTIONS vs version précédente :
    ///   1. avgFallVel calculé sur les frames AVANT le dy courant (i=1..N)
    ///      pour ne pas biaiser avec la remontée
    ///   2. Balle immobile : si avgFallVel < MinFallVelocity → jamais de rebond
    ///   3. La position LastBounceY est le _maxY de la descente (bas réel de la balle)
    ///
    /// ALGORITHME — 4 conditions simultanées :
    ///   1. prevDy > 0 (descendait la frame d'avant)
    ///   2. dy < -RiseThreshPx (remonte franchement maintenant)
    ///   3. avgFallVel >= MinFallVelocity (vitesse de descente moyenne suffisante)
    ///   4. dx/avgFallVel <= HorizMaxRatio (pas principalement horizontal)
    /// </summary>
    public sealed class ImpactDetector
    {
        public float MinFallVelocity { get; set; } = 3.5f;  // px/frame descente moyenne min
        public float RiseThreshPx { get; set; } = 2.5f;  // remontée min sur 1 frame
        public float BrutalChangePx { get; set; } = 7.0f;  // |prevDy|+|dy| min
        public int VelocityWindow { get; set; } = 5;     // frames pour vitesse moyenne
        public float HorizMaxRatio { get; set; } = 2.5f;  // dx/avgFallVel max
        public int CooldownMs { get; set; } = 350;

        private readonly float[] _dyBuf = new float[16];
        private int _dyHead = 0;
        private int _dyCount = 0;

        private float _prevY = float.NaN;
        private float _prevX = float.NaN;
        private float _prevDy = 0f;
        private float _maxY = float.NaN;
        private long _lastFireTicks = 0;

        public float LastBounceY { get; private set; } = 0f;

        public void Reset()
        {
            _dyHead = 0;
            _dyCount = 0;
            _prevY = float.NaN;
            _prevX = float.NaN;
            _prevDy = 0f;
            _maxY = float.NaN;
            _lastFireTicks = 0;
            LastBounceY = 0f;
            Array.Clear(_dyBuf, 0, _dyBuf.Length);
        }

        public void SetSmoothMode(bool smooth) { }
        public bool Update(bool a, bool b, long t) => false;
        public bool UpdateAirToGround(bool a, long t) => false;
        public bool Update(float contactY, long nowTicks)
            => UpdateBounce(float.IsNaN(_prevX) ? 0f : _prevX, contactY, nowTicks);
        public bool Update(float contactY, float yGround, long nowTicks)
            => UpdateBounce(float.IsNaN(_prevX) ? 0f : _prevX, contactY, nowTicks);

        public bool UpdateBounce(float ballX, float contactY, long nowTicks)
        {
            if (float.IsNaN(_prevY))
            {
                _prevX = ballX;
                _prevY = contactY;
                _prevDy = 0f;
                return false;
            }

            float dy = contactY - _prevY;
            float dx = Math.Abs(ballX - _prevX);
            float prevDy = _prevDy;

            _prevX = ballX;
            _prevY = contactY;
            _prevDy = dy;

            // Enregistre dy AVANT de tester (on exclura dy courant du calcul de vitesse)
            _dyBuf[_dyHead % _dyBuf.Length] = dy;
            _dyHead++;
            if (_dyCount < _dyBuf.Length) _dyCount++;

            // Met à jour _maxY pendant la descente (dy > 0)
            if (dy > 0)
            {
                if (float.IsNaN(_maxY) || contactY > _maxY) _maxY = contactY;
            }
            else if (dy < -RiseThreshPx * 3f)
            {
                // Remontée forte sans descente préalable → reset _maxY
                // (balle lancée vers le haut depuis immobile)
                if (float.IsNaN(_maxY)) { /* rien */ }
            }

            // ── Condition 1 : inversion — la frame d'avant descendait ──────
            if (prevDy <= 0f) return false;

            // ── Condition 2 : remontée franche maintenant ─────────────────
            if (dy >= -RiseThreshPx) return false;

            // ── Condition 3 : changement brutal ───────────────────────────
            float totalChange = Math.Abs(prevDy) + Math.Abs(dy);
            if (totalChange < BrutalChangePx) return false;

            // ── Condition 4 : vitesse de descente moyenne AVANT ce frame ──
            // On calcule sur les frames i=1..N (on exclut i=0 = dy courant négatif)
            int n = Math.Min(_dyCount - 1, VelocityWindow);
            if (n < 2) return false;

            float avgFallVel = 0f;
            for (int i = 1; i <= n; i++)
            {
                int idx = (_dyHead - 1 - i + _dyBuf.Length * 2) % _dyBuf.Length;
                avgFallVel += _dyBuf[idx];
            }
            avgFallVel /= n;

            // La descente moyenne doit être significative
            if (avgFallVel < MinFallVelocity) return false;

            // ── Condition 5 : filtre horizontal ───────────────────────────
            if (avgFallVel > 0f && dx > HorizMaxRatio * avgFallVel) return false;

            // ── REBOND VALIDÉ ──────────────────────────────────────────────
            LastBounceY = float.IsNaN(_maxY) ? contactY : _maxY;
            _maxY = float.NaN;

            return TryFire(nowTicks);
        }

        private bool TryFire(long nowTicks)
        {
            long cd = CooldownMs * TimeSpan.TicksPerMillisecond;
            if (_lastFireTicks != 0 && (nowTicks - _lastFireTicks) < cd)
                return false;
            _lastFireTicks = nowTicks;
            return true;
        }
    }
}