@echo off
rem The BODY LAB: evolves bodies from one soft blob by physics alone (the body-plan prototype, docs/BODYPLAN.md)
rem in open water, on the sea floor and on land, and shows them in 3D in your browser. It does not touch your world.
rem Options (see RUN.txt): add e.g. --env sea-floor --seeds 3 --generations 800 to the Neuroreef.exe line below.
cd /d "%~dp0"
if not exist Neuroreef.exe (
  echo Neuroreef.exe is not in this folder. Put BodyLab.bat next to Neuroreef.exe ^(unzip the whole download into one folder^).
  pause
  exit /b 1
)
Neuroreef.exe --bodylab
if errorlevel 1 (
  echo.
  echo The Body Lab did not start. If the line above says "unknown option --bodylab", this Neuroreef.exe is older
  echo than the Body Lab: download the newest build and unzip ALL of it into a new, empty folder, then run BodyLab.bat there.
)
echo.
echo This window does not take typing: press any key to close it.
pause >nul
