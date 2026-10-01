@echo off
setlocal
set "E_EXE=%~dp0tests\FrameShift.UiSamples\bin\Debug\net8.0-windows\FrameShift.UiSamples.exe"
if not exist "%E_EXE%" (
  echo Le lanceur de tests doit etre compile avant utilisation.
  pause
  exit /b 1
)
start "" "%E_EXE%" --e
