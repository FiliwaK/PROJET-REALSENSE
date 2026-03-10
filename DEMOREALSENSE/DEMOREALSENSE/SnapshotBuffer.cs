using System;
using System.Drawing;

namespace DEMOREALSENSE
{
    /// <summary>
    /// Stocke une copie de la dernière image affichée (thread-safe)
    /// pour mapping click->pixel et snapshot photo.
    /// </summary>
    public sealed class SnapshotBuffer : IDisposable
    {
        private readonly object _lock = new();
        private Bitmap? _last;

        public void Update(Bitmap shown)
        {
            lock (_lock)
            {
                _last?.Dispose();
                _last = (Bitmap)shown.Clone();
            }
        }

        public Bitmap? TryClone()
        {
            lock (_lock)
            {
                return _last == null ? null : (Bitmap)_last.Clone();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _last?.Dispose();
                _last = null;
            }
        }

        public void Dispose() => Clear();
    }
}