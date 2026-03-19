from pathlib import Path
import shutil
from PIL import Image
import imagehash

SRC_DIR   = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\raw_images")
OUT_DIR   = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\split_output")

VAL_RATIO     = 0.20
MIN_INDEX_GAP = 3
VALID_EXTS    = {".png", ".jpg", ".jpeg", ".bmp"}

def get_image_files(folder):
    files = [p for p in folder.iterdir() if p.suffix.lower() in VALID_EXTS]
    files.sort(key=lambda p: p.name)
    return files

def compute_hash(img_path):
    try:
        with Image.open(img_path) as img:
            return imagehash.phash(img)
    except Exception as e:
        print(f"Erreur {img_path.name}: {e}")
        return None

def select_distinct_val(images, target):
    hashes  = [compute_hash(p) for p in images]
    indexed = [(i, p, hashes[i]) for i, p in enumerate(images) if hashes[i] is not None]
    if len(indexed) < target:
        raise RuntimeError("Pas assez d'images.")

    step     = max(1, len(indexed) // target)
    seeds    = list(range(0, len(indexed), step))
    selected = []

    for idx in seeds:
        if len(selected) >= target: break
        real_i, p, h = indexed[idx]
        if any(abs(real_i - s[0]) < MIN_INDEX_GAP for s in selected): continue
        if not selected or min(h - s[2] for s in selected) >= 6:
            selected.append((real_i, p, h))

    while len(selected) < target:
        best, best_score = None, -1
        for real_i, p, h in indexed:
            if any(real_i == s[0] for s in selected): continue
            if any(abs(real_i - s[0]) < MIN_INDEX_GAP for s in selected): continue
            score = min((h - s[2] for s in selected), default=999)
            if score > best_score: best_score = score; best = (real_i, p, h)
        if best is None: break
        selected.append(best)

    selected.sort(key=lambda x: x[0])
    return [x[1] for x in selected[:target]]

def main():
    images = get_image_files(SRC_DIR)
    if not images:
        raise FileNotFoundError(f"Aucune image dans {SRC_DIR}")

    total     = len(images)
    val_count = max(1, round(total * VAL_RATIO))
    val_files = select_distinct_val(images, val_count)
    val_set   = set(val_files)
    train_files = [p for p in images if p not in val_set]

    for split, files in [("train", train_files), ("val", val_files)]:
        dest = OUT_DIR / "images" / split
        dest.mkdir(parents=True, exist_ok=True)
        for f in files:
            shutil.copy2(f, dest / f.name)

    print("=== SPLIT TERMINÉ ===")
    print(f"Total : {total} | Train : {len(train_files)} | Val : {len(val_files)}")
    print(f"Sortie : {OUT_DIR}")
    print("\nVal :")
    for p in val_files: print(f"  - {p.name}")

if __name__ == "__main__":
    main()