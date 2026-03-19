from ultralytics import YOLO
from pathlib import Path
import shutil

BALL_PT = Path(r"C:\Users\louis\Desktop\runs_ball\ball_full_cpu\weights\best.pt")
LINE_PT = Path(r"C:\Users\louis\Desktop\runs_line\line_full_cpu\weights\best.pt")
OUT_DIR = Path(r"C:\wamp64\www\PROJET-REALSENSE\datasets\models_onnx")

OUT_DIR.mkdir(parents=True, exist_ok=True)

if not BALL_PT.exists():
    print(f"ERREUR : {BALL_PT}"); exit(1)
if not LINE_PT.exists():
    print(f"ERREUR : {LINE_PT}"); exit(1)

print("=== EXPORT BALLE ===")
model_ball = YOLO(str(BALL_PT))
model_ball.export(format="onnx", imgsz=640, opset=12, simplify=False, dynamic=False)
shutil.copy2(BALL_PT.parent / "best.onnx", OUT_DIR / "ball_detect.onnx")
print(f"OK -> {OUT_DIR / 'ball_detect.onnx'}")

print("\n=== EXPORT LIGNE ===")
model_line = YOLO(str(LINE_PT))
model_line.export(format="onnx", imgsz=640, opset=12, simplify=False, dynamic=False)
shutil.copy2(LINE_PT.parent / "best.onnx", OUT_DIR / "line_seg.onnx")
print(f"OK -> {OUT_DIR / 'line_seg.onnx'}")

print("\n=== TERMINÉ ===")
for f in OUT_DIR.iterdir():
    print(f"  {f.name}  ({f.stat().st_size/1024/1024:.1f} MB)")