# Windows 개발 환경 설정 및 시작 가이드

이 문서는 macOS용 `quota-bar`를 Windows용으로 새롭게 개발하기 위한 전체 가이드입니다.

---

## 1. 사전 요구사항

| 도구 | 버전 | 용도 |
|------|------|------|
| **Visual Studio 2022** | 최신 Community 버전 | IDE, WPF 디자이너, 디버거 |
| **.NET 8 SDK** | 8.0.x | WPF 앱 런타임 및 빌드 |
| **Git** | 최신 | 버전 관리 |

### 설치 체크리스트
1. Visual Studio Installer 실행
2. **`.NET 데스크톱 개발`** 워크로드 선택
3. **`.NET 8 런타임`** 및 **`.NET 8 SDK`** 확인

---

## 2. 프로젝트 생성

### 2.1 솔루션 생성
```bash
cd src
dotnet new sln -n QuotaBar
```

### 2.2 프로젝트 생성
```bash
# 공통 라이브러리 (모델, API 클라이언트)
dotnet new classlib -n QuotaBar.Core -f net8.0

# WPF 앱 (UI, 트레이 아이콘)
dotnet new wpf -n QuotaBar.Win -f net8.0-windows

# 솔루션에 추가
dotnet sln add QuotaBar.Core/QuotaBar.Core.csproj
dotnet sln add QuotaBar.Win/QuotaBar.Win.csproj

# 프로젝트 참조 추가 (Win -> Core)
cd QuotaBar.Win
dotnet add reference ../QuotaBar.Core/QuotaBar.Core.csproj
```

---

## 3. 프로젝트 구조

```
src/
├── QuotaBar.Core/
│   ├── Models/
│   │   ├── UsageEntry.cs          # 토큰 사용량 항목
│   │   ├── PlatformConfig.cs      # 플랫폼 설정 (API 키 등)
│   │   └── AppSettings.cs         # 전체 앱 설정
│   ├── Services/
│   │   ├── IUsageFetcher.cs       # fetcher 인터페이스
│   │   ├── GlmFetcher.cs          # GLM API
│   │   ├── MiniMaxFetcher.cs      # MiniMax API
│   │   ├── CodexFetcher.cs        # Codex API (로컬 auth.json)
│   │   ├── OpenCodeGoFetcher.cs   # OpenCode Go API
│   │   └── SettingsService.cs     # 설정 JSON 저장/불러오기
│   └── QuotaBar.Core.csproj
│
└── QuotaBar.Win/
    ├── App.xaml / App.xaml.cs     # 진입점, NotifyIcon 초기화
    ├── MainWindow.xaml            # 트레이 팝업 UI
    ├── Views/
    │   ├── SettingsView.xaml      # 설정 화면
    │   └── UsageCardView.xaml     # 플랫폼별 사용량 카드
    ├── Services/
    │   └── TrayIconRenderer.cs    # 동적 트레이 아이콘 생성 (GDI+)
    └── QuotaBar.Win.csproj
```

---

## 4. 핵심 구현 포인트

### 4.1 시스템 트레이 아이콘 (`NotifyIcon`)

`System.Windows.Forms.NotifyIcon`을 사용합니다. WPF 프로젝트에서 `UseWindowsForms`를 활성화하세요.

```xml
<!-- QuotaBar.Win.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

```csharp
// App.xaml.cs
var icon = new System.Windows.Forms.NotifyIcon
{
    Visible = true,
    Icon = System.Drawing.SystemIcons.Application,
    Text = "Quota Bar"
};

icon.MouseClick += (s, e) =>
{
    if (e.Button == MouseButtons.Left) ShowPopup();
    if (e.Button == MouseButtons.Right) ShowSettings();
};
```

### 4.2 팝업 UI (`Window`)

macOS의 `NSPopover` 대신, 테두리 없는 WPF `Window`를 사용합니다.

```xml
<Window x:Class="QuotaBar.Win.MainWindow"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        SizeToContent="WidthAndHeight"
        ShowInTaskbar="False"
        Topmost="True">
    <!-- 카드형 UI -->
</Window>
```

위치 계산: `NotifyIcon`의 화면 좌표를 가져와 `Window`의 `Left`/`Top`을 설정합니다.

### 4.3 동적 트레이 아이콘 생성

macOS의 `CoreGraphics` 대신 `System.Drawing` (GDI+)를 사용합니다.

```csharp
using System.Drawing;

public static Icon RenderTrayIcon(List<UsageEntry> entries, bool isDarkMode)
{
    using var bitmap = new Bitmap(16, 16);
    using var g = Graphics.FromImage(bitmap);

    // 배경 및 텍스트 색상 설정
    var bgColor = isDarkMode ? Color.Black : Color.White;
    var textColor = isDarkMode ? Color.White : Color.Black;

    g.Clear(bgColor);

    // 퍼센트에 따라 원/막대 그리기
    // ...

    return Icon.FromHandle(bitmap.GetHicon());
}
```

### 4.4 API Fetcher

`HttpClient`를 사용하여 macOS 버전과 동일한 엔드포인트를 호출합니다.

```csharp
public class GlmFetcher : IUsageFetcher
{
    private readonly HttpClient _client;

    public async Task<UsageResult> FetchAsync(PlatformConfig config)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, config.ApiUrl);
        request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        // JSON 파싱 -> UsageEntry 리스트 반환
    }
}
```

### 4.5 설정 저장

`System.Text.Json`으로 로컬 JSON 파일(`settings.json`)에 저장합니다.

```csharp
public class SettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuotaBar", "settings.json"
    );

    public AppSettings Load() { /* 파일 읽기 */ }
    public void Save(AppSettings settings) { /* 파일 쓰기 */ }
}
```

### 4.6 다크모드 감지

Windows 테마를 감지하여 아이콘 및 UI 색상을 변경합니다.

```csharp
using Microsoft.Win32;

public static bool IsDarkModeEnabled()
{
    const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    var value = Registry.GetValue(key, "AppsUseLightTheme", 1);
    return value is int intValue && intValue == 0;
}
```

---

## 5. macOS → Windows 매핑

| macOS (Swift) | Windows (C#/WPF) | 설명 |
|---------------|------------------|------|
| `AppDelegate` | `App.xaml.cs` | 앱 생명주기 관리 |
| `NSStatusBar` / `NSStatusItem` | `NotifyIcon` | 시스템 트레이 아이콘 |
| `NSPopover` | `Window` (`WindowStyle=None`) | 팝업 UI |
| `SwiftUI.Views` | WPF `UserControl` / `Window` | UI 화면 |
| `MenuBarRenderer` (CoreGraphics) | `TrayIconRenderer` (GDI+) | 동적 아이콘 렌더링 |
| `UserDefaults` | 로컬 JSON 파일 | 설정 저장 |
| `Timer` | `System.Timers.Timer` | 주기적 갱신 (5분) |
| `Combine` | `INotifyPropertyChanged` | 데이터 바인딩 |

---

## 6. 빌드 및 배포

### 6.1 단일 실행 파일 빌드
```bash
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

### 6.2 설치 프로그램
- **Inno Setup**: 가벼운 `.exe` 설치 파일 생성
- **MSIX**: Microsoft Store 배포용

---

## 7. GitHub Actions CI/CD 예시

`.github/workflows/build.yml`:

```yaml
name: Build Windows

on: [push, pull_request]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore src/QuotaBar.sln
      - run: dotnet build src/QuotaBar.sln --configuration Release --no-restore
      - run: dotnet test src/QuotaBar.sln --no-build --verbosity normal
```

---

## 8. 참고 자료

- [WPF 공식 문서](https://learn.microsoft.com/ko-kr/dotnet/desktop/wpf/)
- [NotifyIcon in WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/app-development/dialog-boxes-overview)
- [HttpClient 가이드](https://learn.microsoft.com/ko-kr/dotnet/fundamentals/networking/http/httpclient)
- [macOS 원본 저장소](https://github.com/uwseoul/quota-bar)
