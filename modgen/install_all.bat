@echo off
rem Builds, checks and installs all Living Wasteland mod files into your Kenshi folder.
rem Edit KENSHI below if your game is somewhere else.
set KENSHI=C:\Program Files (x86)\Steam\steamapps\common\Kenshi
python "%~dp0build_mod.py" "%KENSHI%" --install
echo.
echo If you see ERROR or INVALID lines above, nothing was installed: report them in the mod's thread on the Meitou Discord.
echo Add --skip LivingWastelandHeat to the python line if you do not want the Heat mod.
pause
