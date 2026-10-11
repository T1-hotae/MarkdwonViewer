# MdViewer

마크다운(.md) 파일을 더블클릭하면 바로 열리는 Windows용 뷰어·편집기.

- 목차 사이드바, 표·코드 하이라이트·체크박스, 다크 모드
- 문서 안 `.md` 링크를 같은 창에서 이어서 열기, 파일이 바뀌면 자동 새로고침
- 편집 모드: 왼쪽 미리보기 + 오른쪽 편집기, 원본 줄바꿈·인코딩 유지하며 저장
- 노션용 복사: 마크다운 원문을 복사해서 노션에 붙여넣으면 블록으로 변환됨

## 설치

`MdViewer-Setup.exe`를 실행한다 (관리자 권한 불필요, 현재 사용자에게만 설치).
설치 후에도 .md가 다른 프로그램으로 열리면: 파일 우클릭 → 연결 프로그램 → 다른 앱 선택 → MdViewer → 항상.

필요 환경: Windows 10/11, .NET Framework 4.x와 WebView2 런타임 (Windows 11에는 기본 포함).

## 단축키

| 키 | 기능 |
| --- | --- |
| `Ctrl+E` | 편집 / 보기 전환 |
| `Ctrl+S` | 저장 |
| `Ctrl+Shift+C` | 노션용 전체 복사 |
| `Ctrl+O` | 다른 파일 열기 |
| `Ctrl+B` | 목차 숨기기 / 보이기 |
| `Ctrl+P` | 인쇄 / PDF 저장 |
| `F5` | 새로고침 |
| `Alt+←` | 이전 문서 |
| `Ctrl+W` | 닫기 |

## 빌드

Windows에 기본으로 들어 있는 .NET Framework 컴파일러(csc.exe)를 쓰므로 따로 설치할 것이 없다.

```powershell
powershell -ExecutionPolicy Bypass -File src\build.ps1
```

결과물:
- `MdViewer.exe` + WebView2 DLL 3개 — 이 4개 파일만 있으면 실행된다
- `dist\MdViewer-Setup.exe` — 위 파일을 품은 설치 프로그램 하나

## 구조

| 파일 | 내용 |
| --- | --- |
| `src/MdViewer.cs` | 창, WebView2 설정, 파일 제공(`https://md.local/<드라이브>/<경로>`), 저장, 파일 변경 감지 |
| `src/viewer.html` | 마크다운 렌더링, 목차, 편집 모드, 노션용 복사 |
| `src/viewer.css` | 화면 스타일 |
| `src/Setup.cs` | 설치·제거 프로그램 (연결 프로그램, 시작 메뉴, 앱 목록 등록) |
| `src/build.ps1` | 빌드 스크립트 |
| `src/lib/` | 함께 배포하는 외부 라이브러리 |

## 외부 라이브러리

- [markdown-it](https://github.com/markdown-it/markdown-it) 14.1.0 — MIT
- [highlight.js](https://github.com/highlightjs/highlight.js) 11.10.0 — BSD-3-Clause
- [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) 1.0.2903.40 — Microsoft 라이선스 (재배포 허용)
