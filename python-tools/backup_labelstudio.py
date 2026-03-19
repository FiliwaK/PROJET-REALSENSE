import shutil
from pathlib import Path
from datetime import datetime

DATA_DIR   = Path(r"C:\wamp64\www\PROJET-REALSENSE\label-studio-data")
BACKUP_DIR = Path(r"C:\wamp64\www\PROJET-REALSENSE\label-studio-data\backups")

BACKUP_DIR.mkdir(exist_ok=True)

if not DATA_DIR.exists():
    print(f"ERREUR : {DATA_DIR} introuvable"); exit(1)

ts       = datetime.now().strftime("%Y%m%d_%H%M%S")
zip_name = BACKUP_DIR / f"labelstudio_backup_{ts}"
shutil.make_archive(str(zip_name), "zip", str(DATA_DIR))
size_mb = (zip_name.with_suffix(".zip")).stat().st_size / 1024 / 1024
print(f"Backup : {zip_name}.zip ({size_mb:.1f} MB)")