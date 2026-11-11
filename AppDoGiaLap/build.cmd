@echo off
rem Build AppDoGiaLap.exe (.NET Framework, khong can cai gi them tren may chay)
setlocal
set CSC=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%~dp0bin\AppDoGiaLap.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0AppDoGiaLap.cs"
