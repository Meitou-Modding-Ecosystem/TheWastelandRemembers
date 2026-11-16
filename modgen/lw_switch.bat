@echo off
rem Switches your Kenshi mods folder between the KENSHI build and the MEITOU build of Living Wasteland.
rem Use switch_to_meitou.bat and switch_to_kenshi.bat; do not run this file directly.
rem What it does: saves the current state (mods.cfg and our mod folders) into %USERPROFILE%\LW_switch_backup\<state>,
rem then installs the other build. Edit KENSHI below if your game is somewhere else.
setlocal
set KENSHI=C:\Program Files (x86)\Steam\steamapps\common\Kenshi
set BACKUP=%USERPROFILE%\LW_switch_backup
set MODS=LivingWasteland LivingWastelandCannibals LivingWastelandOutlaws LivingWastelandHeat LivingWastelandLua LivingWastelandMeitou LivingWastelandTest
set TARGET=%1

if not exist "%KENSHI%\mods" (
  echo Cannot find "%KENSHI%\mods". Open this file in Notepad and fix the KENSHI line.
  pause
  exit /b 1
)
if /i not "%TARGET%"=="meitou" if /i not "%TARGET%"=="kenshi" (
  echo Run switch_to_meitou.bat or switch_to_kenshi.bat instead.
  pause
  exit /b 1
)

set CURRENT=kenshi
if exist "%BACKUP%\current.txt" set /p CURRENT=<"%BACKUP%\current.txt"
if /i "%CURRENT%"=="%TARGET%" (
  echo You are already on the %TARGET% setup. Nothing to do.
  pause
  exit /b 0
)

echo Saving your current %CURRENT% setup to "%BACKUP%\%CURRENT%" ...
call :save %CURRENT%
if errorlevel 1 goto :failed

if /i "%TARGET%"=="meitou" goto :to_meitou
goto :to_kenshi

:to_meitou
call :clear
echo Building and installing the Meitou build ...
python "%~dp0build_mod.py" "%KENSHI%" --target meitou --install
if errorlevel 1 (
  echo The Meitou build failed. Putting your Kenshi setup back.
  call :restore kenshi
  goto :failed
)
rem bring back the mod list you last used with Meitou, if there is one
if exist "%BACKUP%\meitou\mods.cfg" copy /y "%BACKUP%\meitou\mods.cfg" "%KENSHI%\mods.cfg" >nul
if exist "%BACKUP%\meitou\data_mods.cfg" copy /y "%BACKUP%\meitou\data_mods.cfg" "%KENSHI%\data\mods.cfg" >nul
>"%BACKUP%\current.txt" echo meitou
echo.
echo Done: you are on the MEITOU setup. Open the Meitou launcher and tick the mods (MEITOU_PLAY_GUIDE.md, Step 5).
pause
exit /b 0

:to_kenshi
if exist "%BACKUP%\kenshi" (
  call :restore kenshi
) else (
  echo No saved Kenshi setup found: rebuilding the Kenshi build. Your mod list is NOT restored, so tick the mods again in Kenshi's launcher.
  call :clear
  python "%~dp0build_mod.py" "%KENSHI%" --install --with-lua
  if errorlevel 1 goto :failed
)
>"%BACKUP%\current.txt" echo kenshi
echo.
echo Done: you are on the KENSHI setup. Start Kenshi from Steam as usual.
pause
exit /b 0

:failed
echo.
echo Something went wrong. Your saved copies are in "%BACKUP%". Send me a screenshot of this window.
pause
exit /b 1

:save
rem :save <state> - copy mods.cfg and our mod folders into the backup folder for that state
mkdir "%BACKUP%\%1" >nul 2>&1
if exist "%BACKUP%\%1\mods" rmdir /s /q "%BACKUP%\%1\mods"
del /q "%BACKUP%\%1\mods.cfg" "%BACKUP%\%1\data_mods.cfg" >nul 2>&1
if exist "%KENSHI%\mods.cfg" copy /y "%KENSHI%\mods.cfg" "%BACKUP%\%1\mods.cfg" >nul
if exist "%KENSHI%\data\mods.cfg" copy /y "%KENSHI%\data\mods.cfg" "%BACKUP%\%1\data_mods.cfg" >nul
for %%M in (%MODS%) do (
  if exist "%KENSHI%\mods\%%M" robocopy "%KENSHI%\mods\%%M" "%BACKUP%\%1\mods\%%M" /MIR /NFL /NDL /NJH /NJS /NP >nul
)
exit /b 0

:clear
rem remove our mod folders from the Kenshi mods folder
for %%M in (%MODS%) do (
  if exist "%KENSHI%\mods\%%M" rmdir /s /q "%KENSHI%\mods\%%M"
)
exit /b 0

:restore
rem :restore <state> - put that state's mod folders and mod list back
call :clear
if exist "%BACKUP%\%1\mods" (
  for %%M in (%MODS%) do (
    if exist "%BACKUP%\%1\mods\%%M" robocopy "%BACKUP%\%1\mods\%%M" "%KENSHI%\mods\%%M" /MIR /NFL /NDL /NJH /NJS /NP >nul
  )
)
if exist "%BACKUP%\%1\mods.cfg" copy /y "%BACKUP%\%1\mods.cfg" "%KENSHI%\mods.cfg" >nul
if exist "%BACKUP%\%1\data_mods.cfg" copy /y "%BACKUP%\%1\data_mods.cfg" "%KENSHI%\data\mods.cfg" >nul
exit /b 0
