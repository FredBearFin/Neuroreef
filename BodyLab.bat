@echo off
rem The BODY LAB: evolves bodies from one soft blob by physics alone (the body-plan prototype, docs/BODYPLAN.md)
rem in open water, on the sea floor and on land, and shows them in 3D in your browser. It does not touch your world.
rem Options (see RUN.txt): add e.g. --env sea-floor --seeds 3 --generations 800 to the line below.
cd /d "%~dp0"
Neuroreef.exe --bodylab
pause
