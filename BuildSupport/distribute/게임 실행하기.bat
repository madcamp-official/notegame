@echo off
REM Ninja Adventure 실행기 (Windows)
REM 서버로 가는 안전한 통로(터널)를 연 뒤 게임을 켭니다.
cd /d "%~dp0"
chcp 65001 >nul

set "KEY=tunnel\notegame_tunnel"
set "SERVER_HOST=172.10.5.138"

echo.
echo   Ninja Adventure 준비 중...

REM 0) VPN 확인 (camp-11 SSH 포트 22 에 닿는지)
powershell -NoProfile -Command "if (-not (Test-NetConnection -ComputerName '%SERVER_HOST%' -Port 22 -InformationLevel Quiet -WarningAction SilentlyContinue)) { exit 1 }"
if errorlevel 1 (
  echo.
  echo   [X] 게임 서버에 연결할 수 없습니다.
  echo       KAIST VPN 이 켜져 있는지 확인한 뒤 다시 실행해 주세요.
  echo.
  pause
  exit /b 1
)

REM 1) 키 권한 잠그기 (OpenSSH 가 느슨한 키를 거부함)
icacls "%KEY%" /inheritance:r /grant:r "%USERNAME%:R" >nul 2>&1

REM 2) 터널 열기 (별도 창, 최소화). 이 통로로 localhost:8787 만 이동 가능
start "notegame-tunnel" /min ssh -N -i "%KEY%" -o StrictHostKeyChecking=accept-new -o ExitOnForwardFailure=yes -o ServerAliveInterval=30 -L 8787:127.0.0.1:8787 gametunnel@%SERVER_HOST%

REM 통로가 열릴 때까지 잠시 대기
timeout /t 3 >nul

REM 3) 게임 실행 (종료될 때까지 대기)
echo   게임을 시작합니다.
start /wait "" "Ninja Adventure.exe"

REM 4) 게임 종료 후 통로 정리
taskkill /FI "WINDOWTITLE eq notegame-tunnel*" /F >nul 2>&1
echo   게임을 종료했습니다.
