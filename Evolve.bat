@echo off
rem Runs the PREHISTORY: evolves brains from random at full speed (about an hour) and saves evolved.nrbank next to Neuroreef.exe.
rem Afterwards, double-click Neuroreef.exe: it starts from the evolved brains automatically.
cd /d "%~dp0"
Neuroreef.exe --evolve
echo.
echo Finished. Results: evolve-report.txt   Evolved brains: evolved.nrbank
pause
