# Impasto

[🇬🇧 English](readme.md) · 기계 번역입니다 — 수정 사항은 풀 리퀘스트로 보내주세요.

Impasto는 Linux, Windows, macOS용 페인팅 및 이미지 편집 애플리케이션입니다.

Impasto는 빠른 그림 작업, 이미지 편집, 자르기, 크기 조정, 레이어 기반 편집에 적합합니다.
텍스트 도구로 글자를 그리고, 도형 도구로 자유 곡선을 그리고, 선택 도구로 이미지 레이어의 일부를 복사하고 붙여넣을 수 있습니다.
아래 스크린샷을 참고하세요.

![편집 가능한 객체로 다루는 도형과 텍스트, 객체별 작업 내역 포함](docs/screenshots/impasto-object-layers.png)

![텍스트 도구와 함께 열려 있는 UI 환경 설정 대화 상자](docs/screenshots/impasto-text-tool.png)

![캔버스 중심선에 붙어 있는 도형 제어점과 드래그 중 표시되는 안내선](docs/screenshots/impasto-snap-to-grid.png)
주요 기능 이미지: 옮기는 대상과 그리는 대상이 캔버스 격자, 눈금자 단위, 캔버스 가장자리나 중심선에 맞춰 붙습니다.

## 다운로드

Linux, Windows, macOS용 빌드는
[릴리스 페이지](https://github.com/zbcoding/ImpastoPaint/releases/latest)에 있습니다. Windows 및
macOS 빌드는 서명되지 않았으므로 SmartScreen과 Gatekeeper가 경고를 표시합니다.

Impasto는 GTK 4.18 이상이 필요하며, 이 요건이 Linux에서 어떤 방식이 동작하는지를 결정합니다:

- **Flatpak**, [FlatPark](https://flatpark.org/apps/com.github.zbcoding.Impasto/) 또는
  릴리스 페이지에서 받을 수 있습니다. 자체 GTK를 포함하고 있어 flatpak이 동작하는 모든 환경에서 실행되며
  Ubuntu 22.04와 24.04도 포함됩니다. 어떤 것을 골라야 할지 모르겠다면 이것을 사용하세요.
- **AppImage** (`Impasto-x86_64.AppImage`). GTK를 함께 포함하므로 시스템 GTK가 필요하지 않지만,
  이 번들은 **glibc 2.43** 기준으로 빌드되어 그보다 오래된 환경에서는 실행되지 않습니다. 즉
  Ubuntu 26.04, Fedora 44, Arch에서는 동작하지만 Ubuntu 24.04, Ubuntu 22.04, Fedora 43, Debian 13에서는
  동작하지 않습니다 - 이 경우 Flatpak을 사용하세요.
- **Zip** (`Impasto-linux-dotnet-*.zip`). 배포판이 제공하는 GTK를 사용하므로
  GTK 4.18 이상이 이미 설치되어 있어야 합니다.

Impasto는 MIT 라이선스로 배포됩니다(`license-mit.txt` 참고). 서드파티
저작자 표시, 고지 사항, 라이선스 전문은 `THIRD-PARTY-NOTICES.md`에 있습니다.

## Pinta와의 관계

Impasto는 새롭고 독립적인 프로젝트입니다. MIT 라이선스로 공개된
[Pinta](https://github.com/PintaProject/Pinta)의 소스에서 출발했으며, Pinta 자체도
[Paint.NET™](https://www.getpaint.net/)에서 영감을 받은 GTK 애플리케이션입니다.
Pinta의 기여자 목록은 Impasto 애플리케이션 안에 표시됩니다.
Impasto는 Pinta 프로젝트나 동일한 기여자들이 관리하지 않습니다.

<details>
<summary><h2>Windows에서 빌드하기</h2></summary>

먼저 필요한 GTK 관련 의존성을 설치합니다:
- [MSYS2](https://www.msys2.org)를 설치합니다
- CLANG64 터미널에서 `pacman -S mingw-w64-clang-x86_64-libadwaita mingw-w64-clang-x86_64-webp-pixbuf-loader`를 실행합니다.
  - ARM64 Windows에서는 `CLANGARM64` 터미널을 사용하고 `clang-x86_64`를 `clang-aarch64`로 바꿉니다.

그런 다음 [Visual Studio](https://visualstudio.microsoft.com/)에서 `Pinta.sln`을 열어 애플리케이션을 빌드할 수 있습니다.
Visual Studio 설치 관리자를 통해 .NET 10이 설치되어 있는지 확인하세요.

명령줄에서 빌드하려면:
- [.NET 10 SDK를 설치합니다](https://dotnet.microsoft.com/).
- 빌드:
  - `dotnet build`
- 실행:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>macOS에서 빌드하기</h2></summary>

- .NET 10과 GTK4를 설치합니다
  - `brew install dotnet-sdk libadwaita adwaita-icon-theme gettext webp-pixbuf-loader`
  - Apple Silicon에서는 애플리케이션이 GTK 라이브러리를 불러올 수 있도록 환경 변수에 `DYLD_LIBRARY_PATH=/opt/homebrew/lib`를 설정합니다
  - Intel에서는 애플리케이션이 GTK 라이브러리를 불러올 수 있도록 환경 변수에 `DYLD_LIBRARY_PATH=/usr/local/lib`를 설정합니다
- 빌드:
  - `dotnet build`
- 실행:
  - `dotnet run --project Impasto`

</details>

<details>
<summary><h2>Linux에서 빌드하기</h2></summary>

- 사용하는 Linux 배포판의 안내에 따라 [.NET 10](https://dotnet.microsoft.com/)을 설치합니다.
- 나머지 의존성을 설치합니다(안내는 Ubuntu 22.10 기준이지만 다른 배포판에서도 비슷합니다):
  - `sudo apt install autotools-dev autoconf-archive gettext intltool libadwaita-1-dev`
  - 최소 라이브러리 버전: `gtk` >= 4.18, `libadwaita` >= 1.8
  - 선택적 의존성: `webp-pixbuf-loader`
- 빌드(방법 1, 개발 및 테스트용):
  - `dotnet build`
  - `dotnet run --project Impasto`
- 빌드(방법 2, 설치용):
  - `./autogen.sh`
    - tarball에서 빌드하는 경우에는 대신 `./configure`를 실행합니다.
    - `/usr/local` 이외의 디렉터리에 설치하려면 `--prefix=<install directory>` 인수를 추가합니다.
  - `make install`

</details>

## 도움 받기 / 기여하기:

기여를 환영합니다. 요약하면 다음과 같습니다:

- **코드** — 가장 빠르게 기여하는 방법은 이슈를 열어 제안이나 요청을
  설명하는 것입니다. 코드 조각, 파일 이름, 관련 맥락을 함께 적어 주세요.
  PR을 보내셔도 됩니다. 무료 에디션에 PR로 전달된 기여는
  Impasto의 프리미엄 에디션이나 MIT 라이선스를 따르는 다른 소프트웨어에서도 사용될 수 있습니다.
- **AI 코딩 도구** — 환영합니다. 다만 제출한 코드를 직접 설명할 수 있어야 하고
  프로젝트의 아키텍처에 맞게 다듬어야 합니다. AI가 작성한 코드는 병합되기 전에
  추가 검토를 받을 수 있습니다.
- **번역** — 풀 리퀘스트로 기여합니다: 새 `.po` 파일에 AI로 초안을 만든 뒤
  해당 언어 사용자가 수정하고 교정합니다.

기여하기 전에 git/PR 작업 흐름을 포함한 전체 안내를 `CONTRIBUTING.md`에서 읽어 주세요.

- [버그/이슈](https://github.com/zbcoding/ImpastoPaint/issues)를 제보할 수 있습니다.
- 각 릴리스의 주요 변경 사항은 `CHANGELOG.md`에 기록됩니다.
