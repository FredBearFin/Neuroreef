@echo off
rem LONG PREHISTORY: evolves until you stop it (days or weeks), with no graphics so all the CPU goes to evolution.
rem Its whole state is saved to prehistory.nrck every 30 minutes. If the PC restarts or the window closes,
rem double-click this file again: it resumes from the last save (at most 30 minutes are lost).
rem To stop for good: press Ctrl+C in this window (it saves, writes evolve-report.txt and evolved.nrbank).
rem Then double-click Neuroreef.exe to watch the world that evolved.
cd /d "%~dp0"
powercfg /change standby-timeout-ac 0 >nul 2>&1
powercfg /change hibernate-timeout-ac 0 >nul 2>&1
:again
Neuroreef.exe --evolve --steps 0
rem 0 = stopped with Ctrl+C (saved); 2 = cannot resume (the reason is printed above). Anything else is a crash:
rem wait a little and resume from the last save.
if %errorlevel% equ 0 goto done
if %errorlevel% equ 2 goto done
echo The run stopped unexpectedly (code %errorlevel%). Resuming from the last save in 30 seconds...
timeout /t 30
goto again
:done
echo.
echo Stopped. Results: evolve-report.txt   Evolved brains: evolved.nrbank   State: prehistory.nrck
pause
