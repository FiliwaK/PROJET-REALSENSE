from pathlib import Path
from ultralytics import YOLO

def main():
    # --- Chemins (adapte si besoin) ---
    ROOT = Path(__file__).resolve().parents[1]          # ...\Models\BallLine
    WEIGHTS = ROOT / "best.pt"
    SOURCE_DIR = ROOT / "images_reel_test"              # tes nouvelles images
    OUT_DIR = ROOT / "runs_predict_reel"                # dossier de sortie

    # --- Vérifs ---
    if not WEIGHTS.exists():
        raise FileNotFoundError(f"Introuvable: {WEIGHTS}")
    if not SOURCE_DIR.exists():
        raise FileNotFoundError(f"Introuvable: {SOURCE_DIR}")

    # --- Modèle ---
    model = YOLO(str(WEIGHTS))

    # --- Predict ---
    # conf: seuil de confiance (0.25 = standard, tu peux mettre 0.4 si trop de faux positifs)
    # imgsz: taille d'entrée (640 standard)
    # save=True: sauvegarde les images annotées
    # save_txt=True: sauvegarde les détections en txt (format YOLO)
    # save_conf=True: ajoute la confiance dans les txt
    results = model.predict(
        source=str(SOURCE_DIR),
        imgsz=640,
        conf=0.25,
        iou=0.5,
        save=True,
        save_txt=True,
        save_conf=True,
        project=str(OUT_DIR),
        name="pred",
        exist_ok=True
    )

    print("\n✅ Predict terminé.")
    print(f"📁 Résultats: {OUT_DIR / 'pred'}")
    # Images annotées:  ...\runs_predict_reel\pred\
    # Labels txt:      ...\runs_predict_reel\pred\labels\

if __name__ == "__main__":
    main()