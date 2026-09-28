@echo off
setlocal
set "D1_EXE=%~dp0tests\FrameShift.UiSamples\bin\Debug\net8.0-windows\FrameShift.UiSamples.exe"
if not exist "%D1_EXE%" (
  echo Le lanceur de tests doit etre compile avant utilisation.
  pause
  exit /b 1
)
start "" "%D1_EXE%" --d1
