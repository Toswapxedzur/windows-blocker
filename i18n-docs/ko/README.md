# 윈도우 볼트

Windows Vault는 Vault 제품군의 기본 Windows 구성원입니다. WebView2 편집기, 기본 애플리케이션 인벤토리, 적용 엔진, 사용자 정의 규칙 런타임 및 로컬 웹앱 브리지 허브가 포함된 .NET 8 WPF 애플리케이션입니다.

코드는 제품 계약입니다. 유지 관리되는 앱 내 매뉴얼은 [src/WindowsBlocker/WebAssets/manual/en.md](src/WindowsBlocker/WebAssets/manual/en.md)입니다.

## 현재 기능

- 선택한 Windows 애플리케이션의 기본 그룹과 고급 정책 규칙의 사용자 지정 그룹입니다.
- 즉시, 허용, 카운트다운 모드; 일정; 꼭 매달리게 하다; 선잠; 및 그룹 가져오기/내보내기.
- Windows 애플리케이션 인벤토리 및 창 기반 적용 구성 요소.
- `src/WindowsBlocker/WebAssets/`에서 호스팅되는 WebView2 편집기.
- 구문 검사 및 로그 피드를 통해 제어된 사용자 정의 규칙 실행.
- 명시적으로 연결된 호환 그룹을 위한 루프백 브리지 허브입니다.
- 기본 타이머, 토스트 및 패널 오버레이 창.

## 빌드

체크인된 솔루션 및 프로젝트를 사용하십시오.

```powershell
dotnet build WindowsBlocker.sln
```

애플리케이션 프로젝트는 `net8.0-windows`을 대상으로 하며 WPF와 WebView2를 사용합니다. 필요한 .NET SDK 및 WebView2 런타임을 사용하여 Windows에서 빌드하고 실행하세요.

## 프로젝트 맵

| 면적 | 소스 디렉토리 |
| --- | --- |
| 그룹 모델 및 정책 평가 | `src/WindowsBlocker/Core/` |
| 기본 시행 | `src/WindowsBlocker/Enforcement/` |
| 앱 인벤토리 및 WebView 브리지 | `src/WindowsBlocker/WebUI/` |
| 사용자 정의 규칙 런타임 | `src/WindowsBlocker/Rules/` |
| 브리지 허브 | `src/WindowsBlocker/Bridge/` |
| WPF 창 및 오버레이 | `src/WindowsBlocker/` |

## 문서 및 번역

영어 문서는 표준으로 남아 있습니다. UI 레이블은 `src/WindowsBlocker/WebAssets/translation/`의 전체 JSON 카탈로그를 사용합니다. 번역된 매뉴얼은 `manual/en.md` 옆에 있고 나머지 유지 관리 문서의 번역된 사본은 `i18n-docs/<locale>/` 아래에 있습니다.
