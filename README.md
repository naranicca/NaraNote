# NaraNote

Windows 10 22H2 이상에서 실행되는 .NET 8 WPF 포스트잇 애플리케이션입니다. 각 노트는 독립된 프레임리스 창이며 상태는 로컬 JSON에 자동 저장됩니다. 네트워크, 텔레메트리, 자동 업데이트는 사용하지 않습니다.

## 프로젝트 구조

- `NaraNote.Core`: 직렬화 모델, undo/redo, scribble 인식, 파일 분류 및 기하/대비 계산
- `NaraNote.Infrastructure`: atomic JSON 저장과 백업 복구, 개인정보를 기록하지 않는 파일 로거
- `NaraNote.App`: WPF 창, 트레이, 클립보드, 파일 드롭, InkCanvas
- `NaraNote.Core.Tests`: Core 및 저장소 단위 테스트

WPF 객체는 저장하지 않습니다. 텍스트는 노트 모델에, 이미지·첨부·stroke는 다형 `NoteElement` DTO에 저장합니다. 이미지는 `%LocalAppData%\NaraNote\images`에 PNG로 복사하고, 일반 첨부는 원본 경로만 참조합니다.

## 빌드 및 실행

.NET 8 SDK가 필요합니다.

```powershell
dotnet restore NaraNote.sln
dotnet build NaraNote.sln --configuration Release
dotnet test NaraNote.sln --configuration Release
dotnet run --project src/NaraNote.App/NaraNote.App.csproj --configuration Release
```

이 작업공간에는 검증용 SDK가 `.dotnet`에 설치되어 있으므로 시스템 SDK가 없다면 `dotnet` 대신 `.\.dotnet\dotnet.exe`를 사용할 수 있습니다.

Self-contained 단일 파일 게시(`publish\NaraNote.exe` 한 파일 생성):

```powershell
dotnet publish src/NaraNote.App/NaraNote.App.csproj -c Release -o publish
```

프로젝트에 `win-x64`, self-contained, single-file 및 압축 설정이 지정되어 있어 게시된 `NaraNote.exe`만 다른 Windows x64 PC에 복사해 실행할 수 있습니다. 대상 PC에 .NET 런타임을 별도로 설치할 필요가 없습니다. 게시에는 약 200MB 이상의 임시 디스크 공간이 필요합니다.

## 동작

- 최초 실행 시 360×320 노란 노트 생성, 상단 빈 영역 이동, 8px 네이티브 가장자리/모서리 리사이즈
- `+`/`Ctrl+N` 새 노트, `X`는 현재 노트만 닫음
- 텍스트, Unicode, 폰트/크기/색상 설정 및 500ms debounce 자동 저장
- `Ctrl++`, `Ctrl+-`, `Ctrl+0`; 이미지 우선 `Ctrl+V`
- 이미지/텍스트/기타 파일 드롭, 첨부 더블클릭 실행, 누락 첨부 경고
- 이미지와 첨부의 단일 선택·드래그 이동, 이미지 모서리 비율 리사이즈와 접근 가능 영역 제한
- 이미지 더블클릭 inline 캡션 편집(Enter 확정, Escape 취소, 포커스 이탈 확정)
- 객체 추가·삭제·이동·리사이즈·캡션과 stroke/scribble 그룹의 제한형 undo/redo
- InkCanvas 선택/펜/지우개 모드, 7개 선 색상과 5단계 굵기
- 수평 왕복 scribble 판정과 겹친 stroke 단위 삭제 및 한 번에 undo
- 트레이에서 새 노트, 표시/숨김, 종료; atomic replace와 백업 복구
- `RegisterHotKey` 전역 새 노트/표시·숨김 단축키와 충돌 기록
- HKCU 현재 사용자 시작프로그램 등록 및 실제 레지스트리 상태 동기화
- 모니터 제거 또는 화면 밖 저장 위치의 가장 가까운 work area 보정

## 현재 노트 파일 저장

- `Ctrl+S` 또는 컨텍스트 메뉴의 `현재 노트 저장`으로 현재 노트를 독립 파일로 저장합니다.
- 텍스트만 있는 노트는 기본적으로 UTF-8 `.txt`로 저장합니다.
- 이미지, 첨부 파일 또는 드로잉이 있으면 `.naranote` 문서로 저장합니다.
- `.naranote`는 ZIP 기반 패키지이며 `manifest.json`, `images/`, `attachments/`를 포함합니다.
- 존재하는 이미지와 첨부 파일은 패키지 안에 복사하고, 찾을 수 없는 첨부 파일은 원본 경로 정보만 기록합니다.
- 한 번 저장한 노트에서 `Ctrl+S`를 누르면 같은 파일을 갱신하며, `Ctrl+Shift+S`는 다른 이름으로 저장합니다.
- 내보내기 파일도 임시 파일 작성 후 교체하는 atomic save 방식을 사용합니다.
- 앱 시작 시 현재 사용자 범위에서 `.naranote`를 `NaraNote 문서`로 등록하고 앱 아이콘과 열기 명령을 연결합니다. 관리자 권한은 필요하지 않습니다.

## 데이터와 개인정보

데이터 위치는 `%LocalAppData%\NaraNote`입니다.

```text
app-state.json
app-state.backup.json
images/
attachments/
logs/
```

앱을 제거한 뒤 이 폴더를 삭제하면 모든 데이터가 제거됩니다. 로그에는 노트 본문이나 클립보드 내용이 기록되지 않습니다.

## NuGet 패키지

런타임 애플리케이션에는 외부 NuGet 패키지가 없습니다. 테스트 프로젝트에만 다음 패키지를 사용합니다.

- `Microsoft.NET.Test.Sdk`: `dotnet test` 호스트. 기본 런타임에는 테스트 호스트가 없습니다.
- `xunit`: 단위 테스트 프레임워크. .NET 기본 API에는 테스트 실행/검증 프레임워크가 없습니다.
- `xunit.runner.visualstudio`: VSTest 및 IDE에서 xUnit 테스트를 발견합니다.

## 수동 QA

1. 최초 실행에서 노란 노트 하나와 프레임리스 외형을 확인합니다.
2. 모든 변/모서리 리사이즈와 상단 빈 영역 이동을 확인합니다.
3. 노트 3개를 만들고 각기 텍스트·색상을 변경한 뒤 재실행하여 복원을 확인합니다.
4. 텍스트와 스크린샷을 각각 붙여넣고, txt/png/pdf/zip을 드롭합니다.
5. 이미지 더블클릭으로 캡션을 저장하고 재실행 후 복원을 확인합니다.
6. 우클릭으로 펜 모드에 들어가 선을 그리고, 기존 선 위를 빠르게 좌우 3회 이상 왕복해 삭제합니다.
7. 트레이 표시/숨김과 마지막 노트를 닫아도 프로세스가 유지되는지 확인합니다.
8. 보조 모니터 및 100/125/150/200% DPI에서 이동과 리사이즈 hit-test를 확인합니다.

## 현재 제한사항

다음 고급 UX는 후속 작업입니다.

- 임의 color picker(현재 7개 프리셋 제공)
- 설정 화면에서 전역 단축키 등록 실패를 inline 경고로 표시(현재 로그 기록)
- 선택 상태의 완전한 PowerPoint형 외곽선과 변 중앙 resize handle(현재 모서리 4개)
- 첨부 파일의 자체 아이콘 추출과 이동 undo의 시각적 전환 애니메이션
- 모니터 간 이동 도중 `WM_DPICHANGED` 기반 실시간 크기 보정
- 시스템 종료 세션 이벤트와 설정 변경 직후 트레이 아이콘 재구성

이 항목들은 모델과 순수 로직 확장 지점이 마련되어 있으나 완료 조건으로 주장하지 않습니다.
