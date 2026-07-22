# 게임 서버 원클릭 실행 스크립트 (Windows / PowerShell)
#
# 하는 일:  run.sh 의 Windows 버전
#   1) camp-11(vLLM)로 가는 SSH 터널이 안 떠 있으면 연다 (127.0.0.1:8000 -> camp-11:8000)
#   2) .env 가 없으면 .env.example 을 복사해준다 (VLLM_API_KEY 는 직접 채워야 함)
#   3) npm install (main 갱신 시 새 패키지 자동 반영)
#   4) 서버(npm start, 포트 8787)를 띄운다
#
# 사용법:  Server 폴더에서 run.bat 더블클릭  (또는)
#          powershell -ExecutionPolicy Bypass -File run.ps1

$ErrorActionPreference = "Stop"

# ── 설정 (환경변수로 덮어쓰기 가능) ─────────────────────────────────
$RemoteHost = if ($env:QWEN_REMOTE)      { $env:QWEN_REMOTE }      else { "root@172.10.5.138" }
$SshKey     = if ($env:QWEN_SSH_KEY)     { $env:QWEN_SSH_KEY }     else { "$env:USERPROFILE\.ssh\qwen_key" }
$LocalPort  = if ($env:VLLM_LOCAL_PORT)  { $env:VLLM_LOCAL_PORT }  else { 8000 }
$RemotePort = if ($env:VLLM_REMOTE_PORT) { $env:VLLM_REMOTE_PORT } else { 8000 }

Set-Location $PSScriptRoot

Write-Host "▶ notegame 서버 실행 준비..."

# ── 1) SSH 터널 확인 & 열기 ────────────────────────────────────────
$listening = Get-NetTCPConnection -LocalPort $LocalPort -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    Write-Host "  ✓ 터널 이미 떠 있음 (localhost:$LocalPort)"
} else {
    if (-not (Test-Path $SshKey)) {
        Write-Host "  ✗ SSH 키가 없습니다: $SshKey"
        Write-Host "    팀장에게 qwen_key 를 받아 위 경로에 두세요."
        exit 1
    }
    Write-Host "  … 터널 여는 중: localhost:$LocalPort -> $RemoteHost`:$RemotePort"
    Start-Process -FilePath "ssh" -WindowStyle Hidden -ArgumentList @(
        "-N",
        "-o", "ExitOnForwardFailure=yes",
        "-o", "ServerAliveInterval=30",
        "-i", "`"$SshKey`"",
        "-L", "${LocalPort}:127.0.0.1:${RemotePort}",
        "$RemoteHost"
    )
    Start-Sleep -Seconds 2
    Write-Host "  ✓ 터널 열림 (백그라운드)."
}

# ── 2) .env 준비 ──────────────────────────────────────────────────
if (-not (Test-Path .env)) {
    Copy-Item .env.example .env
    Write-Host "  ! .env 를 새로 만들었습니다. 열어서 VLLM_API_KEY 를 채운 뒤 다시 실행하세요."
    exit 1
}
if (-not (Select-String -Path .env -Pattern '^VLLM_API_KEY=.+' -Quiet)) {
    Write-Host "  ✗ .env 의 VLLM_API_KEY 가 비어 있습니다. 값을 채운 뒤 다시 실행하세요."
    exit 1
}

# ── 3) 의존성 (main 갱신 시 자동 반영) ─────────────────────────────
Write-Host "  … npm install (최신 의존성 반영)"
npm install --no-audit --no-fund

# ── 4) 서버 실행 ──────────────────────────────────────────────────
Write-Host "▶ 서버 시작 (http://127.0.0.1:8787).  Ctrl+C 로 종료."
npm start
