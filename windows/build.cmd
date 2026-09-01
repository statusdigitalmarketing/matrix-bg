@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

rem --- generate app icon (green binary glyphs on black) ---
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Add-Type -AssemblyName System.Drawing;" ^
  "$b=New-Object System.Drawing.Bitmap 32,32; $g=[System.Drawing.Graphics]::FromImage($b);" ^
  "$g.Clear([System.Drawing.Color]::Black); $g.TextRenderingHint='AntiAliasGridFit';" ^
  "$f=New-Object System.Drawing.Font('Consolas',13,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel);" ^
  "$c=[System.Drawing.Color]::FromArgb(0,255,70); $br=New-Object System.Drawing.SolidBrush $c; $dim=New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120,$c));" ^
  "$g.DrawString('1',$f,$br,2,1);$g.DrawString('0',$f,$dim,12,-2);$g.DrawString('1',$f,$br,21,3);$g.DrawString('0',$f,$dim,2,14);$g.DrawString('1',$f,$br,12,10);$g.DrawString('0',$f,$dim,21,16);$g.DrawString('1',$f,$dim,12,20);" ^
  "$h=$b.GetHicon(); $i=[System.Drawing.Icon]::FromHandle($h); $fs=[IO.File]::Create('app.ico'); $i.Save($fs); $fs.Close()"

"%CSC%" /nologo /target:winexe /optimize+ /unsafe /platform:anycpu /win32icon:app.ico /out:MatrixBG.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll MatrixBG.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built MatrixBG.exe
