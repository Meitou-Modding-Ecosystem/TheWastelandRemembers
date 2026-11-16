@echo off
rem Builds the Living Wasteland data mods for the MEITOU engine (not Kenshi + RE_Kenshi) and installs them into your Kenshi folder.
rem WARNING: this writes the same mod folders as install_all.bat. Install ONE target at a time; running this replaces the Kenshi build.
rem The C# code mod (LivingWastelandMeitou, its DLL ships ready-built in modgen\meitou) is installed too.
rem Edit KENSHI below if your game is somewhere else.
set KENSHI=C:\Program Files (x86)\Steam\steamapps\common\Kenshi
python "%~dp0build_mod.py" "%KENSHI%" --target meitou --install
echo.
echo If you see ERROR or INVALID lines above, nothing was installed: report them in the mod's thread on the Meitou Discord.
pause
