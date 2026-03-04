import sys, json
from ultralytics import YOLO

# usage: python predict.py best.pt image.png
model_path = sys.argv[1]
img_path = sys.argv[2]

model = YOLO(model_path)
res = model.predict(source=img_path, conf=0.25, verbose=False)[0]

out = []
if res.boxes is not None:
    for b in res.boxes:
        cls = int(b.cls[0])
        conf = float(b.conf[0])
        x1, y1, x2, y2 = [float(v) for v in b.xyxy[0]]
        out.append({"cls": cls, "conf": conf, "xyxy": [x1, y1, x2, y2]})

print(json.dumps(out))