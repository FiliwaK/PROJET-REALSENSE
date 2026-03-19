import shutil, zipfile
from pathlib import Path
import datetime

DATA_DIR   = Path(r"C:\wamp64\www\PROJET-REALSENSE\label-studio-data")
BACKUP_DIR = Path(r"C:\wamp64\www\PROJET-REALSENSE\label-studio-data\backups")

zips = sorted(BACKUP_DIR.glob("labelstudio_backup_*.zip"), reverse=True)
if not zips:
    print("Aucun backup trouvé dans", BACKUP_DIR); exit(1)

latest = zips[0]
print(f"Backup le plus récent : {latest.name}")
if input("Restaurer ? (o/n) : ").lower() != "o":
    print("Annulé."); exit(0)

if DATA_DIR.exists():
    ts  = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    bak = DATA_DIR.parent / f"label_studio_data_avant_restore_{ts}"
    DATA_DIR.rename(bak)

DATA_DIR.mkdir()
with zipfile.ZipFile(latest, "r") as z:
    z.extractall(DATA_DIR)
print(f"Restauration terminée depuis {latest.name}")