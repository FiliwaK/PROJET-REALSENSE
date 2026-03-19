using System;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Détecteur de rebond par PIC LOCAL SUR Y.
    ///
    /// PRINCIPE (inspiré des algorithmes de détection de pic en signal processing) :
    ///   On stocke une fenêtre glissante de N positions Y.
    ///   Un rebond = le Y central est strictement supérieur à TOUS ses voisins
    ///   d'au moins MinDeltaPx pixels des deux côtés.
    ///
    ///   En coordonnées image : Y augmente vers le bas.
    ///   Rebond = balle au point le plus bas = Y maximum local.
    ///
    /// AVANTAGES vs approche vitesse :
    ///   - Pas de seuil de vitesse → fonctionne balle lente et rapide
    ///   - Pas de filtre horizontal → pas de faux positifs sur mouvement X
    ///   - Simple et déterministe → pas d'état complexe
    ///   - Détection ~HalfWindow frames après le vrai rebond (délai fixe et prévisible)
    ///
    /// PARAMÈTRES :
    ///   HalfWindow  — nb frames de chaque côté du pic (total = 2*HalfWindow+1)
    ///                 ex: 3 → fenêtre de 7 frames, délai = 3 frames (~100ms à 30fps)
    ///   MinDeltaPx  — le pic doit dépasser tous ses voisins d'au moins MinDeltaPx
    ///                 élimine les micro-oscillations du tracker
    ///   CooldownMs  — délai min entre deux rebonds détectés
    /// </summary>
    public sealed class ImpactDetector
    {
        public int HalfWindow { get; set; } = 3;    // 3 frames de chaque côté
        public float MinDeltaPx { get; set; } = 5f;   // pic doit dépasser voisins de 5px min
        public int CooldownMs { get; set; } = 400;

        // Fenêtre circulaire de positions Y
        private readonly float[] _win = new float[32];
        private int _head = 0;
        private int _count = 0;
        private long _lastFireTicks = 0;

        // Y exact au moment du pic (pour placer la croix)
        public float LastBounceY { get; private set; } = 0f;

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _lastFireTicks = 0;
            LastBounceY = 0f;
            Array.Clear(_win, 0, _win.Length);
        }

        // Compat no-ops
        public void SetSmoothMode(bool smooth) { }
        public bool Update(bool a, bool b, long t) => false;
        public bool UpdateAirToGround(bool a, long t) => false;
        public bool Update(float y, float yGround, long t) => Update(y, t);
        public bool UpdateBounce(float x, float y, long t) => Update(y, t);

        /// <summary>
        /// Appeler chaque frame avec le Y centre balle (pixels).
        /// Retourne true quand un rebond est détecté.
        /// LastBounceY contient le Y exact du pic.
        /// </summary>
        public bool Update(float ballY, long nowTicks)
        {
            // Enregistre dans la fenêtre circulaire
            _win[_head % _win.Length] = ballY;
            _head++;
            if (_count < _win.Length) _count++;

            int ws = HalfWindow * 2 + 1; // taille totale fenêtre
            if (_count < ws) return false;

            // Lit le centre de la fenêtre
            // Index 0 = plus récent, index ws-1 = plus ancien
            // Le centre est à HalfWindow depuis le plus récent
            float yCenter = Get(HalfWindow);

            // Vérifie que le centre est un pic strict
            // = strictement supérieur à tous les voisins d'au moins MinDeltaPx
            for (int k = 1; k <= HalfWindow; k++)
            {
                if (Get(HalfWindow - k) >= yCenter - MinDeltaPx) return false; // voisin récent trop proche
                if (Get(HalfWindow + k) >= yCenter - MinDeltaPx) return false; // voisin ancien trop proche
            }

            // Pic local confirmé
            LastBounceY = yCenter;
            return TryFire(nowTicks);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Get(0) = frame la plus récente
        /// Get(n) = n frames en arrière
        /// </summary>
        private float Get(int backIndex)
        {
            int i = (_head - 1 - backIndex + _win.Length * 2) % _win.Length;
            return _win[i];
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