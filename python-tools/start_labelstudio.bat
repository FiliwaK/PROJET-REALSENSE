@echo off
:: Lance Label Studio avec données persistantes
:: Données sauvegardées dans : C:\wamp64\www\PROJET-REALSENSE\label-studio-data
set DATA_DIR=C:\wamp64\www\PROJET-REALSENSE\label-studio-data

if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"

call C:\Users\louis\Desktop\yolo_export\venv\Scripts\activate.bat

set LABEL_STUDIO_LOCAL_FILES_SERVING_ENABLED=true
label-studio start --data-dir "%DATA_DIR%" --port 8080

pause