# Quota Bar for Windows

AI 코딩 플랫폼의 사용량을 Windows에서 실시간으로 모니터링하는 경량 애플리케이션입니다.

> macOS용 [quota-bar](https://github.com/uwseoul/quota-bar)의 Windows 포트 버전입니다.

## 스크린샷

![Quota Bar](assets/screenshot.png)

## 주요 기능

- **실시간 사용량 모니터링**: GLM, MiniMax, Codex, OpenCode Go 사용량을 한눈에 확인
- **심플 모드**: 플랫폼명 + 색상 점 + 퍼센트만 표시하는 미니멀 UI
- **디테일 모드**: 각 플랫폼별 상세 사용량, 진행 바, 리셋 타이머 표시
- **프레임리스 윈도우**: 테두리 없는 깔끔한 UI, 드래그로 이동 가능
- **항상 위에 표시**: 다른 창 위에 항상 떠 있어 실시간 확인 가능
- **자동 리프레시**: 설정한 간격(기본 5분)으로 자동 갱신
- **그라데이션 배경**: 다크 블루 톤의 세련된 디자인

## 지원 플랫폼

| 플랫폼 | 사용량 정보 |
|--------|------------|
| **GLM** | z.ai / bigmodel.cn API 사용량 |
| **MiniMax** | Token Plan + Coding Plan 사용량 |
| **OpenAI Codex** | Primary/Secondary 윈도우 사용량 |
| **OpenCode Go** | 워크스페이스 사용량 |

## 설치 방법

### 요구사항

- Windows 10 이상
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### 실행 파일 다운로드

1. [Releases](https://github.com/uwseoul/quota-bar-win/releases) 페이지에서 최신 버전 다운로드
2. `QuotaBar.Win.exe` 실행

### 소스에서 빌드

```bash
# 저장소 클론
git clone https://github.com/uwseoul/quota-bar-win.git
cd quota-bar-win

# 빌드
dotnet build src/QuotaBar.sln --configuration Release

# 실행
src/QuotaBar.Win/bin/Release/net8.0-windows/QuotaBar.Win.exe
```

## 설정 방법

1. 실행 후 **Settings...** 버튼 클릭
2. 각 플랫폼별 API 키/인증 정보 입력

### GLM

- **Platform**: z.ai 또는 bigmodel.cn 선택
- **API Key**: [z.ai](https://z.ai) 또는 [bigmodel.cn](https://bigmodel.cn)에서 발급

### MiniMax

- **API Key**: [MiniMax](https://www.minimaxi.com)에서 발급

### OpenAI Codex

- **Auth Token** (선택): `~/.codex/auth.json`의 `tokens.access_token`
- **Account ID** (선택): `~/.codex/auth.json`의 `tokens.account_id`
- 입력하지 않으면 자동으로 `~/.codex/auth.json`을 읽습니다

### OpenCode Go

- **Workspace ID**: OpenCode Go 워크스페이스 ID
- **Auth Cookie**: 브라우저 개발자 도구에서 복사한 쿠키 문자열

## 사용 방법

| 기능 | 방법 |
|------|------|
| **새로고침** | 🔄 버튼 클릭 또는 자동 갱신 |
| **심플/디테일 전환** | 🚥 버튼 클릭 |
| **설정** | Settings... 버튼 클릭 |
| **종료** | Quit 버튼 또는 ✕ 버튼 클릭 |
| **창 이동** | 타이틀 바 영역을 드래그 |

## 설정 옵션

### 리프레시 간격

- Settings → Refresh → Refresh Interval (seconds)
- 기본값: 300초 (5분)
- 최소: 10초
- 너무 짧은 간격은 API rate limit에 걸릴 수 있으니 주의

### 표시 모드

- **Menu Bar Mode**: Highest / Selected
- **Display Style**: Percent / Absolute / Both

### 테마

- Auto / Light / Dark

## 기술 스택

- **언어**: C# 12
- **프레임워크**: .NET 8
- **UI**: WPF (Windows Presentation Foundation)
- **HTTP 클라이언트**: `System.Net.Http.HttpClient`
- **설정 저장**: JSON 파일 (`%LOCALAPPDATA%\QuotaBar\settings.json`)

## 프로젝트 구조

```
quota-bar-win/
├── src/
│   ├── QuotaBar.Core/          # 핵심 라이브러리
│   │   ├── Models/             # 데이터 모델 (QuotaEntry, AppSettings)
│   │   └── Services/           # API fetcher, 설정 서비스
│   │       ├── GlmFetcher.cs
│   │       ├── MiniMaxFetcher.cs
│   │       ├── CodexFetcher.cs
│   │       ├── OpenCodeGoFetcher.cs
│   │       ├── UsageService.cs
│   │       └── SettingsService.cs
│   └── QuotaBar.Win/           # WPF 애플리케이션
│       ├── Views/
│       │   └── UsageCardView.xaml
│       ├── MainWindow.xaml     # 메인 UI
│       ├── MainWindow.xaml.cs  # 메인 로직
│       ├── SettingsWindow.xaml # 설정 UI
│       └── App.xaml
└── README.md
```

## FAQ

### API 호출 시 토큰이 소모되나요?

아닙니다. 사용량을 조회하는 API이므로 AI 모델 추론과 달리 토큰이 소모되지 않습니다. 다만 서버 측 rate limit이 있을 수 있으니 너무 짧은 간격(1초 등)으로 설정하지 마세요.

### 왜 Codex가 "Error"로 표시되나요?

`~/.codex/auth.json` 파일이 없거나 토큰이 만료된 경우입니다. Settings에서 직접 Codex Auth Token과 Account ID를 입력하세요.

### 창이 다른 창 뒤로 가요

Settings → Window → Always on Top를 체크하세요. 기본적으로 켜져 있어야 합니다.

## 라이선스

MIT License

## 크레딧

- 원작: [quota-bar (macOS)](https://github.com/uwseoul/quota-bar)
- Windows 포트: [@union](https://github.com/uwseoul)
