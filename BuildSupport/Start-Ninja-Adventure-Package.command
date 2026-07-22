#!/bin/zsh
set -euo pipefail

launcher_directory="${0:A:h}"
if [[ -x "$launcher_directory/BuildSupport/Run-Ninja-Adventure.command" ]]; then
  package_root="$launcher_directory"
elif [[ "${launcher_directory:t}" == "BuildSupport" &&
        -x "$launcher_directory/Run-Ninja-Adventure.command" ]]; then
  package_root="${launcher_directory:h}"
else
  print -u2 "Ninja Adventure 실행 구성요소를 찾지 못했습니다. 압축 파일 전체를 다시 풀어 주세요."
  read -k 1 "?아무 키나 누르면 종료합니다."
  exit 1
fi

exec "$package_root/BuildSupport/Run-Ninja-Adventure.command"
