@echo off
rem Collects the Meitou game's log and settings into ONE zip on your Desktop. Run it after you quit Meitou.
rem Meitou 0.19 keeps log.txt beside the game, in its version folder (%LocalAppData%\Meitou\versions\<version>); older versions kept it in
rem %LocalAppData%\Meitou. Both are collected (one log_<version>.txt per installed version).
rem Edit KENSHI below if your game is somewhere else.
set KENSHI=C:\Program Files (x86)\Steam\steamapps\common\Kenshi
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmm"') do set STAMP=%%i
set OUT=%USERPROFILE%\Desktop\LW_meitou_logs_%STAMP%
mkdir "%OUT%" >nul 2>&1
copy "%LOCALAPPDATA%\Meitou\log.txt" "%OUT%\" >nul 2>&1
powershell -NoProfile -Command "Get-ChildItem -Path \"$env:LOCALAPPDATA\Meitou\versions\" -Recurse -Filter log.txt -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName (Join-Path '%OUT%' ('log_' + $_.Directory.Name + '.txt')) }"
copy "%LOCALAPPDATA%\Meitou\launcher.json" "%OUT%\" >nul 2>&1
copy "%LOCALAPPDATA%\Meitou\meitou.user.json" "%OUT%\" >nul 2>&1
copy "%KENSHI%\mods.cfg" "%OUT%\" >nul 2>&1
copy "%KENSHI%\data\mods.cfg" "%OUT%\data_mods.cfg" >nul 2>&1
dir /b /s "%LOCALAPPDATA%\Meitou\versions\*.dmp" > "%OUT%\dump_list.txt" 2>nul
dir /b "%LOCALAPPDATA%\Meitou\versions" > "%OUT%\installed_versions.txt" 2>nul
dir /b /s "%LOCALAPPDATA%\Meitou\versions\Meitou.Modding.dll" > "%OUT%\modding_dll_path.txt" 2>nul
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%.zip' -Force"
echo.
echo Saved: %OUT%.zip   (send me that one file)
dir /b "%OUT%"
pause
