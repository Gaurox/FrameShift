@echo off
setlocal
set "D3_EXE=%~dp0tests\FrameShift.UiSamples\bin\Debug\net8.0-windows\FrameShift.UiSamples.exe"
if not exist "%D3_EXE%" (
  echo Le lanceur de tests doit etre compile avant utilisation.
  pause
  exit /b 1
)
start "" "%D3_EXE%" --d3
