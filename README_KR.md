# Quota Bar for Windows

AI 코딩 플랫폼의 토큰 소비량을 Windows 시스템 트레이에서 실시간으로 모니터링하는 경량 애플리케이션입니다.

> 이 저장소는 macOS용 [quota-bar](https://github.com/uwseoul/quota-bar)의 Windows 버전을 새롭게 개발하기 위한 공간입니다.

## 기술 스택

- **언어**: C#
- **UI 프레임워크**: WPF (.NET 8)
- **트레이 아이콘**: `System.Windows.Forms.NotifyIcon` + WPF 통합
- **네트워크**: `System.Net.Http.HttpClient`
- **설정 저장**: 로컬 JSON 파일

## 프로젝트 구조

```
quota-bar-win/
├── src/
│   ├── QuotaBar.Win/          # WPF 앱 (트레이 아이콘, 팝업 UI)
│   └── QuotaBar.Core/         # 공통 라이브러리 (API, 모델, 설정)
├── assets/                      # 아이콘, 이미지 리소스
├── scripts/                     # 빌드 및 배포 스크립트
└── docs/
    └── WINDOWS_SETUP.md         # 상세 개발 가이드
```

## 빠른 시작

1. [개발 환경 설정 가이드](docs/WINDOWS_SETUP.md)를 참고하세요.
2. `src/QuotaBar.Win/`에서 WPF 프로젝트를 생성합니다.
3. `src/QuotaBar.Core/`에서 공통 로직(모델, API 클라이언트)을 구현합니다.

## 지원 플랫폼

- GLM (z.ai / bigmodel.cn)
- MiniMax TokenPlan
- OpenAI Codex
- OpenCode Go

## 라이선스

MIT License
