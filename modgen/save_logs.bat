@echo off
rem Collects every log a bug report needs into ONE zip on your Desktop. Run it right after a play session (or a crash), BEFORE relaunching Kenshi:
rem the game and our Lua mod overwrite their logs at launch (ours keeps the previous two as lw_log_prev.txt and lw_log_prev2.txt).
rem Edit KENSHI below if your game is somewhere else.
set KENSHI=C:\Program Files (x86)\Steam\steamapps\common\Kenshi
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmm"') do set STAMP=%%i
set OUT=%USERPROFILE%\Desktop\LW_logs_%STAMP%
mkdir "%OUT%" >nul 2>&1
copy "%KENSHI%\RE_Kenshi_log.txt" "%OUT%\" >nul 2>&1
copy "%KENSHI%\kenshi_info.log" "%OUT%\" >nul 2>&1
copy "%KENSHI%\kenshi.log" "%OUT%\" >nul 2>&1
copy "%KENSHI%\*.dmp" "%OUT%\" >nul 2>&1
copy "%KENSHI%\*kenshilua*.txt" "%OUT%\" >nul 2>&1
copy "%KENSHI%\*kenshilua*.log" "%OUT%\" >nul 2>&1
copy "%KENSHI%\mods\KenshiLua\*.txt" "%OUT%\" >nul 2>&1
copy "%KENSHI%\mods\KenshiLua\*.log" "%OUT%\" >nul 2>&1
copy "%KENSHI%\mods\LivingWastelandLua\lw_log*.txt" "%OUT%\" >nul 2>&1
copy "%LOCALAPPDATA%\kenshi\kenshi_info.log" "%OUT%\kenshi_info_localappdata.log" >nul 2>&1
copy "%LOCALAPPDATA%\kenshi\kenshi.log" "%OUT%\kenshi_localappdata.log" >nul 2>&1
copy "%KENSHI%\mods.cfg" "%OUT%\" >nul 2>&1
copy "%KENSHI%\data\mods.cfg" "%OUT%\data_mods.cfg" >nul 2>&1
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%.zip' -Force"
echo.
echo Saved: %OUT%.zip   (send me that one file)
dir /b "%OUT%"
pause
