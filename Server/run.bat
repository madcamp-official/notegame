@echo off
REM Windows 더블클릭 실행용. 실제 로직은 run.ps1 에 있음.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
echo.
echo (서버가 종료되었습니다. 창을 닫으려면 아무 키나 누르세요.)
pause >nul
