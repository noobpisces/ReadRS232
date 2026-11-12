@echo off
rem Build AppDoGiaLap.exe (.NET Framework, khong can cai gi them tren may chay)
rem Can csc.exe Roslyn (di kem Visual Studio / Build Tools). csc cu cua .NET 4 (C# 5) KHONG dich duoc.
setlocal
set "CSC="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
  for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\Roslyn\csc.exe`) do if not defined CSC set "CSC=%%i"
)
if not defined CSC for %%v in (18 2022 2019) do for %%e in (Enterprise Professional Community BuildTools) do (
  if not defined CSC if exist "%ProgramFiles%\Microsoft Visual Studio\%%v\%%e\MSBuild\Current\Bin\Roslyn\csc.exe" set "CSC=%ProgramFiles%\Microsoft Visual Studio\%%v\%%e\MSBuild\Current\Bin\Roslyn\csc.exe"
)
if not defined CSC (
  echo [LOI] Khong tim thay csc.exe Roslyn. Cai Visual Studio hoac Build Tools for Visual Studio.
  pause
  exit /b 1
)
echo Dung compiler: %CSC%
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%~dp0bin\AppDoGiaLap.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0AppDoGiaLap.cs"
if errorlevel 1 (
  echo [LOI] Build that bai, xem loi o tren.
  pause
  exit /b 1
)
echo OK: %~dp0bin\AppDoGiaLap.exe
