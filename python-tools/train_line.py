"""
Entraînement YOLO11n-seg pour la segmentation de ligne.
"""
from ultralytics import YOLO
from pathlib import Path

DATA_YAML = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\line_seg\yolo\data.yaml")
OUT_DIR   = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\runs_line")

model = YOLO("yolo11n-seg.pt")

model.train(
    data      = str(DATA_YAML),
    epochs    = 100,
    imgsz     = 640,
    batch     = 8,
    device    = "cpu",
    project   = str(OUT_DIR),
    name      = "line_v2",
    patience  = 20,
    workers   = 0,
    cache     = False,
)

print("\n=== ENTRAÎNEMENT LIGNE TERMINÉ ===")
print(f"Modèle : {OUT_DIR}/line_v2/weights/best.pt")
