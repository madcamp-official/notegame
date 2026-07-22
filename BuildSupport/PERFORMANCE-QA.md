# Ninja Adventure macOS 실플레이 성능 QA

이 harness는 이미 실행 중인 최종 macOS Player에 읽기 전용으로 붙는다. 게임을 대신
조작하거나 상태를 만들지 않으므로, 각 구간은 Computer Use로 실제 화면을 준비하고
입력·화면 결과를 확인하면서 측정한다. Unity Editor 프로세스는 대상이 아니다.

## 계측 소스와 두 패스

각 상태는 CPU/RSS 기준 패스와 프레임·앱 귀속 GPU 패스를 분리해 측정한다.

1. `NINJA_PERF_XCTRACE=0`: `ps`의 누적 process CPU time 차이, RSS/VSZ,
   `ps -M` thread 수, `ioreg` GPU 스냅샷을 기록하는 저오버헤드 패스다.
2. `NINJA_PERF_XCTRACE=required`: Xcode `Game Performance` trace를 붙여 대상 앱의
   CAMetal present 간격, hitch, Metal GPU interval과 Metal 할당량을 기록하는 우선 패스다.

전체 Xcode 또는 attach 권한이 없어 두 번째 패스를 실행할 수 없으면 Apple의 공식
[Metal Performance HUD](https://developer.apple.com/documentation/xcode/monitoring-your-metal-apps-graphics-performance)
로그를 앱 귀속 fallback으로 사용한다. HUD는 앱 시작 때 활성화해야 하며, harness는
`player-log-window.txt`에 측정 시작 이후 추가된 `metal-HUD:` line만 파싱한다. 사용 가능한
xctrace frame 자료가 있으면 항상 xctrace를 선택하고 HUD 결과는 보조 원자료로 남긴다.
측정 중 log가 truncate/rotate되어 시작 byte offset을 보장할 수 없으면
`playerLogWindowExact=false`로 기록하고 그 구간의 HUD line을 프레임 증거로 사용하지 않는다.

두 번째 패스의 CPU 수치에는 Instruments 오버헤드가 섞일 수 있고 HUD 패스에도 overlay와
통계 수집 비용이 있으므로 CPU/RSS 비교는 HUD를 끈 첫 번째 패스를 기준으로 한다. 프레임·앱
귀속 GPU 지표는 xctrace를 우선하고, 사용할 수 없을 때 HUD fallback을 기준으로 한다.
`powermetrics`는 관리자 권한을 요구하므로 자동화하지 않는다. `ioreg`의 Device,
Renderer, Tiler utilization은 시스템 전체 참고값이며 앱 단독 사용량으로 보고하지 않는다.

`top`의 화면용 CPU 열은 순간/이동 평균과 로케일 의존 출력이라 자동 판정에 쓰지 않는다.
대신 `ps`의 누적 process CPU time을 실제 wall-clock sample 간 차분해 같은 구간을 다시
계산할 수 있게 남긴다. Unity 자체 `Player.log`는 측정 구간에 새로 추가된 오류를 보존하지만
release Player에서 managed GC Alloc을 제공하지 않는다. 단, Apple Metal HUD를 명시적으로
활성화한 Player의 `metal-HUD:` line은 공식 frame interval·GPU time 자료로 사용한다.
managed GC Alloc은 별도의 동일 콘텐츠 Development Build를 Unity
Profiler로 캡처해 비교하고, 최종 release Player의 RSS 추세·Metal 할당 결과와 구분해 보고한다.

## Metal HUD fallback Player 실행

Metal HUD 환경 변수는 실행 중인 프로세스에 나중에 주입할 수 없다. fallback 패스용 Player를
다음처럼 별도 실행하고, `-logFile`에는 반드시 harness가 읽을 수 있는 절대 경로를 쓴다.

```bash
MTL_HUD_ENABLED=1 \
MTL_HUD_LOG_ENABLED=1 \
MTL_HUD_ELEMENTS='device,layersize,memory,fps,frameinterval,gputime,thermal' \
"/Users/batiger00/MadCamp3/Builds/NinjaAdventure.app/Contents/MacOS/Ninja Adventure" \
  -screen-fullscreen 0 -screen-width 1600 -screen-height 900 \
  -logFile '/Users/batiger00/MadCamp3/ReleaseArtifacts/Evidence/Performance/metal-hud-player.log'
```

`MTL_HUD_ENCODER_TIMING_ENABLED=1`은 encoder 진단이 필요한 별도 패스에서만 추가한다. Apple은
추가 CPU 비용이 생길 수 있다고 명시한다. `MTL_HUD_REPORT_URL=<app-writable-path>`는 HUD에서
생성한 보고서의 저장 위치를 지정할 때 선택적으로 사용하며, 현재 harness는 console log를
원자료로 사용한다.

현재 Apple schema는 두 번째 field를 graphics memory, 세 번째 field를 process memory로
정의한다. 과거 HUD schema는 같은 두 번째 field를 estimated frame misses로 사용했으므로
측정 시 다음 중 하나를 명시한다.

- `NINJA_PERF_METAL_HUD_SCHEMA=current`: 현재 Apple schema. graphics/process memory를 MB로 집계한다.
- `NINJA_PERF_METAL_HUD_SCHEMA=legacy`: 과거 schema. 두 번째 field를 estimated misses로 보존한다.
- `NINJA_PERF_METAL_HUD_SCHEMA=auto`: 기본 안전 모드. frame interval, GPU time, process memory는
  집계하지만 의미가 충돌하는 두 번째 field는 이름 없는 header metric으로만 보존한다.

## 실행 전 확인

- `Builds/NinjaAdventure.app` 최종 빌드를 직접 실행한다.
- Player가 하나만 실행 중인지 확인한다. 여러 개면 정확한 PID를
  `NINJA_PERF_PID`로 넘긴다. harness는 PID의 실제 executable path와 앱 bundle의
  `CFBundleExecutable`을 대조하며, 다르면 즉시 중단한다.
- 성능에 영향을 주는 화면 해상도, 창/전체화면, 품질 단계, 전원 연결 여부와 다른
  실행 앱을 `NINJA_PERF_NOTES`에 남긴다.
- `NINJA_PERF_XCTRACE=required`에는 전체 Xcode와 Developer Tools 권한이 필요하다.
- Metal HUD fallback에서는 `NINJA_PERF_PLAYER_LOG`와 실제 `-logFile` 절대 경로가 일치하는지,
  측정 전에 새 `metal-HUD:` line이 추가되는지 확인한다.

정적 검증은 빌드나 Player를 실행하지 않는다.

```bash
cd "/Users/batiger00/MadCamp3"
./BuildSupport/Validate-Performance-Harness.command
```

## 표준 실플레이 구간

아래 명령의 `NINJA_PERF_NOTES`는 실제 조건에 맞게 바꾼다. 각 명령은 Player를
시작하거나 종료하지 않는다.

```bash
NINJA_PERF_SCENARIO=menu \
NINJA_PERF_DURATION_SECONDS=60 \
NINJA_PERF_XCTRACE=0 \
NINJA_PERF_PLAYER_LOG='/absolute/path/to/Player.log' \
NINJA_PERF_METAL_HUD_SCHEMA=auto \
NINJA_PERF_NOTES='1600x900 windowed, title menu, no input' \
./BuildSupport/Measure-Ninja-Adventure-Performance.command
```

같은 방식으로 다음 구간을 각각 독립 측정한다.

| scenario | Computer Use로 준비할 실제 상태 | 저오버헤드 권장 구간 | frame 패스 권장 구간 |
|---|---|---:|---:|
| `menu` | 타이틀 메뉴, 입력 없음 | 60초 | 30초 |
| `dialogue` | 장문 대화와 선택지가 보이는 상태 | 60초 | 30초 |
| `movement` | WASD 연속 이동과 카메라 추적 | 60초 | 30초 |
| `combat` | 공격·피격·회복·효과가 반복되는 전투 | 60초 | 30초 |
| `animation` | 전환 또는 주요 애니메이션 반복 | 60초 | 30초 |
| `llm-wait` | 자유입력 제출 직전 시작해 처리 표시부터 응답까지 | 실제 응답+30초 | 30~60초 |
| `idle` | 메뉴나 대화의 완전 무입력 상태 | 최소 30분 | 별도 30초 |
| `continuous-play` | 실제 이동·대화·전투를 이어가는 상태 | 최소 60분 | 대표 구간 30초 |

정밀 패스 예시는 다음과 같다.

```bash
NINJA_PERF_SCENARIO=movement-trace \
NINJA_PERF_DURATION_SECONDS=30 \
NINJA_PERF_XCTRACE=required \
NINJA_PERF_NOTES='1600x900 windowed, continuous WASD and camera follow' \
./BuildSupport/Measure-Ninja-Adventure-Performance.command
```

xctrace를 사용할 수 없는 같은 30초 구간의 HUD fallback 예시는 다음과 같다. Player 자체가
위 환경 변수로 이미 실행 중이어야 한다.

```bash
NINJA_PERF_PID=12345 \
NINJA_PERF_PLAYER_LOG='/absolute/path/to/metal-hud-player.log' \
NINJA_PERF_METAL_HUD_SCHEMA=current \
NINJA_PERF_SCENARIO=movement-metal-hud \
NINJA_PERF_DURATION_SECONDS=30 \
NINJA_PERF_XCTRACE=0 \
NINJA_PERF_NOTES='1600x900 windowed, continuous WASD, Metal HUD enabled' \
./BuildSupport/Measure-Ninja-Adventure-Performance.command
```

장시간 유휴 패스에서는 trace 크기와 계측 부하를 피한다.

```bash
NINJA_PERF_SCENARIO=idle-30m \
NINJA_PERF_DURATION_SECONDS=1800 \
NINJA_PERF_INTERVAL_MILLISECONDS=10000 \
NINJA_PERF_XCTRACE=0 \
NINJA_PERF_NOTES='960x540 windowed low quality, title menu, untouched for 30m' \
./BuildSupport/Measure-Ninja-Adventure-Performance.command
```

여러 Player가 떠 있을 때는 먼저 `ps -axo pid=,comm=`으로 전체 executable path를
확인한 뒤, 해당 숫자만 명시한다.

```bash
NINJA_PERF_PID=12345 \
NINJA_PERF_SCENARIO=combat \
NINJA_PERF_XCTRACE=0 \
./BuildSupport/Measure-Ninja-Adventure-Performance.command
```

스택 샘플은 병목 진단용 별도 패스에서만 `NINJA_PERF_CAPTURE_STACKS=1`로 켠다.
CPU 기준 측정과 동시에 켜지 않는다.

## 결과와 판정

각 실행은 기존 자료를 덮어쓰지 않고
`ReleaseArtifacts/Evidence/Performance/<UTC>-<scenario>-pid<PID>/`에 다음을 만든다.

- `samples.csv`: UTC, elapsed, interval CPU%, RSS, VSZ, thread 수, 시스템 GPU 값
- `metadata.json`: 앱·PID·OS·하드웨어·해상도/플레이 메모
- `thermal-start.txt`, `thermal-end.txt`: root 권한이 필요 없는 `pmset -g therm` 전후 상태
- `summary.md`, `metrics.json`: 통계와 판정
- `player-log-window.txt`: 측정 중 새로 기록된 Player.log 부분
- `game-performance.trace`, `xctrace-export/`: 정밀 패스의 원본과 추출 XML
- 상위 `Performance-Index.md`: 모든 구간 비교표

`metrics.json`에는 원시 `trace`, 원시 `metalHud`, 그리고 우선순위 적용 결과인 `graphics`를
각각 남긴다. `graphics.source`는 `xctrace`, `metal-hud-log`, `none` 중 하나이고
`graphics.status`/`frameStatus`가 `PASS`, `FAIL`, `INSUFFICIENT_DATA`, `NOT_MEASURED`를 기록한다.
HUD 결과에는 frame 수, FPS, frame interval p50/p95/p99/max, 50ms 초과 hitch 수·비율,
GPU time p50/p95/max, process memory와 schema가 허용하는 graphics memory가 포함된다.
HUD가 겹치는 rolling window를 log에 반복할 수 있으므로 `frameCount`와 모든 percentile/FPS는
reported frame number별 한 sample만 사용한다. 같은 frame number가 여러 line에 있으면 captured
log에서 뒤에 기록된 line을 최신 관측으로 채택한다. `rawFrameSampleCount`, 전체 overlap,
값이 완전히 같은 `identicalOverlapCount`, 값이 다른 `conflictingOverlapCount`, 그리고
`dataQualityStatus`를 함께 남겨 중복 제거와 충돌 해결을 감사할 수 있게 한다.

30fps PASS는 최소 2초 분량의 xctrace CAMetal present 또는 Metal HUD frame interval 자료에서
다음을 모두 만족해야 한다.

- 관측 FPS 27~33fps
- frame interval p95 41.67ms 이하
- 50ms 초과 interval 비율 1% 이하

정지 화면에서 의도적으로 present를 생략하는 최적화가 확인되면 낮은 present FPS만으로
실패라 단정하지 않는다. 그 경우 CPU/GPU 감소, 입력 직후 즉시 30fps 복귀, 화면 상태
일치를 함께 기록한다. 메뉴·대화·LLM 대기·유휴 구간은 CPU 평균과 GPU 앱 귀속 busy가
이동/전투보다 실제로 낮은지 비교한다. 장시간 패스는 RSS 절대 변화와 MiB/min 변화율,
thread 최대치, Player.log의 오류 증가 여부로 누수·구독 중복·재시도 폭주를 판정한다.

`Player.log` 위치가 표준 경로와 다르면 정확한 파일을 지정한다.

```bash
NINJA_PERF_PLAYER_LOG='/absolute/path/to/Player.log' \
NINJA_PERF_SCENARIO=dialogue \
NINJA_PERF_XCTRACE=0 \
./BuildSupport/Measure-Ninja-Adventure-Performance.command
```
