"""
Entraînement YOLO11n pour la détection de balle.
"""
from ultralytics import YOLO
from pathlib import Path

DATA_YAML = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\ball_detect\yolo\data.yaml")
OUT_DIR   = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\runs_ball")

model = YOLO("yolo11n.pt")

model.train(
    data      = str(DATA_YAML),
    epochs    = 100,
    imgsz     = 640,
    batch     = 8,
    device    = "cpu",
    project   = str(OUT_DIR),
    name      = "ball_v2",
    patience  = 20,
    workers   = 0,
    cache     = False,
)

print("\n=== ENTRAÎNEMENT BALLE TERMINÉ ===")
print(f"Modèle : {OUT_DIR}/ball_v2/weights/best.pt")
