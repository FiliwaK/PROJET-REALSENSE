"""
1. Split 80/20 des images annotées ligne
2. Conversion COCO → YOLO seg
Tout en un seul script.
"""
import json, shutil
from pathlib import Path
from PIL import Image
import imagehash

COCO_JSON  = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\line_seg\result.json")
IMAGES_DIR = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\line_seg\images")
OUT_DIR    = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\line_seg\yolo")

VAL_RATIO = 0.20
MIN_GAP   = 3

def phash(p):
    try:
        with Image.open(p) as img:
            return imagehash.phash(img)
    except:
        return None

def smart_split(images, val_ratio, min_gap):
    images = sorted(images, key=lambda p: p.name)
    n      = len(images)
    target = max(1, round(n * val_ratio))
    hashes = [phash(p) for p in images]
    indexed = [(i, p, h) for i, (p, h) in enumerate(zip(images, hashes)) if h is not None]

    step     = max(1, n // target)
    selected = []

    for idx in range(0, len(indexed), step):
        if len(selected) >= target: break
        ri, p, h = indexed[idx]
        if any(abs(ri - s[0]) < min_gap for s in selected): continue
        if not selected or min(h - s[2] for s in selected) >= 6:
            selected.append((ri, p, h))

    while len(selected) < target:
        best, best_score = None, -1
        for ri, p, h in indexed:
            if any(ri == s[0] for s in selected): continue
            if any(abs(ri - s[0]) < min_gap for s in selected): continue
            score = min((h - s[2] for s in selected), default=999)
            if score > best_score: best_score = score; best = (ri, p, h)
        if best is None: break
        selected.append(best)

    val_set = {x[1] for x in selected[:target]}
    return [p for p in images if p not in val_set], list(val_set)

def convert():
    with open(COCO_JSON, encoding="utf-8") as f:
        coco = json.load(f)

    id2img  = {img["id"]: img for img in coco["images"]}
    cat_map = {cat["id"]: 0 for cat in coco["categories"] if cat["name"] == "line"}

    all_images = list(IMAGES_DIR.rglob("*.png")) + \
                 list(IMAGES_DIR.rglob("*.jpg")) + \
                 list(IMAGES_DIR.rglob("*.jpeg"))

    train_imgs, val_imgs = smart_split(all_images, VAL_RATIO, MIN_GAP)
    val_names = {p.name for p in val_imgs}

    print(f"Split : {len(train_imgs)} train / {len(val_imgs)} val")

    for split in ["train", "val"]:
        (OUT_DIR / "images" / split).mkdir(parents=True, exist_ok=True)
        (OUT_DIR / "labels" / split).mkdir(parents=True, exist_ok=True)

    ann_by_img = {}
    for ann in coco["annotations"]:
        ann_by_img.setdefault(ann["image_id"], []).append(ann)

    stats = {"train": 0, "val": 0, "skip": 0}

    for img_id, img_info in id2img.items():
        fname   = Path(img_info["file_name"]).name
        matches = [p for p in all_images if p.name == fname]
        if not matches:
            stats["skip"] += 1
            continue
        src_img = matches[0]

        split = "val" if fname in val_names else "train"
        W, H  = img_info["width"], img_info["height"]

        shutil.copy2(src_img, OUT_DIR / "images" / split / fname)

        anns  = ann_by_img.get(img_id, [])
        lines = []
        for ann in anns:
            if ann["category_id"] not in cat_map: continue
            seg = ann.get("segmentation", [])
            if not seg: continue
            poly = max(seg, key=len)
            if len(poly) < 6: continue
            pts = []
            for i in range(0, len(poly), 2):
                pts.append(f"{poly[i]/W:.6f} {poly[i+1]/H:.6f}")
            lines.append(f"0 {' '.join(pts)}")

        (OUT_DIR / "labels" / split / (Path(fname).stem + ".txt")).write_text("\n".join(lines))
        stats[split] += 1

    (OUT_DIR / "data.yaml").write_text(
        f"path: {OUT_DIR}\ntrain: images/train\nval: images/val\nnc: 1\nnames: ['line']\n"
    )

    print("=== LIGNE TERMINÉ ===")
    print(f"Train: {stats['train']} | Val: {stats['val']} | Skip: {stats['skip']}")
    print(f"Sortie: {OUT_DIR}")

if __name__ == "__main__":
    convert()
