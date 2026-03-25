from ultralytics import YOLO
from pathlib import Path
import shutil

BALL_PT  = Path(r"C:\Users\louis\Desktop\entrainement_IA\resultats\ball_v2\weights\best.pt")
LINE_PT  = Path(r"C:\Users\louis\Desktop\entrainement_IA\resultats\line_v2\weights\best.pt")
OUT_DIR  = Path(r"C:\wamp64\www\PROJET-REALSENSE\DEMOREALSENSE\DEMOREALSENSE\Models")

print("=== Export BALLE en OpenVINO ===")
model_ball = YOLO(str(BALL_PT))
model_ball.export(format="openvino", imgsz=640, half=False)
ball_dir = BALL_PT.parent / "best_openvino_model"
dst_ball = OUT_DIR / "ball_openvino"
if dst_ball.exists(): shutil.rmtree(dst_ball)
shutil.copytree(ball_dir, dst_ball)
print(f"  -> {dst_ball}")

print("=== Export LIGNE en OpenVINO ===")
model_line = YOLO(str(LINE_PT))
model_line.export(format="openvino", imgsz=640, half=False)
line_dir = LINE_PT.parent / "best_openvino_model"
dst_line = OUT_DIR / "line_openvino"
if dst_line.exists(): shutil.rmtree(dst_line)
shutil.copytree(line_dir, dst_line)
print(f"  -> {dst_line}")

print("\n=== TERMINÉ ===")