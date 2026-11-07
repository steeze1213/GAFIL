# Steam 출시 준비물

Steam에는 **API 키 전용 배포판**만 올립니다 (Anthropic 정책: 다른 사람에게 배포하는 앱은 구독 로그인 대신 API 키). 무료 배포.

## 이 폴더에 있는 것

| 파일 | 쓰는 곳 |
|---|---|
| `store_ko.md`, `store_en.md` | 스토어 페이지 글: 짧은 설명, 게임 정보(BBCode), 지원 언어, 시스템 요구 사항, 태그 |
| `ai_disclosure.md` | 콘텐츠 설문의 AI 콘텐츠 공개 (개발 중 사용 + 실시간 생성, 안전장치) |
| `privacy.md` | 개인정보 안내. 개인정보 처리방침 주소가 필요하면 GitHub의 이 파일 주소를 쓰면 됨 |
| `art/capsules/` | 스토어 캡슐 이미지 4종 |
| `art/library/` | 라이브러리 이미지 4종 (캡슐, 헤더, 히어로, 로고) |
| `art/screenshots/en`, `art/screenshots/ko` | 스크린샷 6장씩 (1920x1080). 영어판을 기본으로, 한국어판은 한국어 스토어용 |
| `art/page_background_1438x810.png` | 스토어 배경 (선택) |
| `art/community_icon_184x184.jpg` | 커뮤니티 아이콘 |
| `steampipe/app_build.vdf` | 빌드 업로드 설정 (APP_ID, DEPOT_ID만 바꾸면 됨) |
| `steampipe/build_steam.ps1` | 배포판을 만들고(`content/`) steamcmd로 올림 |

이미지는 전부 앱의 실제 고양이 그림 데이터와 직접 만든 픽셀 글꼴로 만들었습니다. 말풍선 스크린샷은 실제 앱 화면을 앱 스스로 그려 낸 것이고, 바로 답하기 결과도 실제 계산값입니다.
앱 아이콘은 저장소의 `gafil.ico`를 쓰면 됩니다.

## 직접 해야 하는 것 (계정·결제·제출이라 대신할 수 없음)

1. **Steamworks 가입**: https://partner.steamgames.com → Steam Direct 등록비 **100달러** 결제, 세금 정보(W-8BEN, 한국 거주자), 은행 정보, 본인 확인. 표시되는 개발자 이름은 실명 대신 닉네임을 써도 됩니다.
2. **앱 만들기**: 앱 하나를 만들면 **App ID**와 **Depot ID**가 나옵니다. 유형은 데스크톱 펫들처럼 "게임"을 추천합니다.
3. **스토어 페이지 채우기**
   - 글: `store_en.md`(기본 언어) → 언어를 한국어로 바꿔 `store_ko.md`.
   - 이미지: 아래 표대로 올리기.
   - 가격: **무료**. 지원 언어: 인터페이스 한국어·영어 (음성 없음).
   - Steam Deck 호환: "지원 안 됨"(Windows 바탕화면용), 컨트롤러: 없음.
4. **콘텐츠 설문**: 성인 콘텐츠 없음. AI 공개는 `ai_disclosure.md`의 영어 글을 붙여 넣기 (개발 중 사용 + 실시간 생성 둘 다 "예").
5. **빌드 올리기**
   - `steampipe/app_build.vdf`의 `APP_ID`, `DEPOT_ID`를 받은 숫자로 바꾸기.
   - [Steamworks SDK](https://partner.steamgames.com/doc/sdk)의 `steamcmd`를 받아 두기.
   - PowerShell에서: `.\steam\steampipe\build_steam.ps1 -Upload -SteamUser <Steam 계정>` (비밀번호와 Steam Guard는 steamcmd가 직접 물어봄).
   - Steamworks → 빌드에서 방금 올린 빌드를 **default** 브랜치로 지정.
   - 설치 → 실행 옵션: 실행 파일 `GAFIL.exe`, 운영체제 Windows, 64비트.
6. **검토 제출**: 스토어 페이지와 빌드를 각각 검토에 제출. 출시하려면 등록비 결제 후 30일이 지나야 하고, "곧 출시" 페이지를 2주 이상 공개해야 합니다.
7. **출시 전 한 번 확인 (권장)**: Steam으로 받은 GAFIL에 본인 API 키를 넣고 질문 하나 해 보기.

### 이미지 올리는 곳

| Steamworks 칸 | 파일 |
|---|---|
| 헤더 캡슐 (Header Capsule) | `art/capsules/header_capsule_920x430.png` |
| 작은 캡슐 (Small Capsule) | `art/capsules/small_capsule_462x174.png` |
| 메인 캡슐 (Main Capsule) | `art/capsules/main_capsule_1232x706.png` |
| 세로 캡슐 (Vertical Capsule) | `art/capsules/vertical_capsule_748x896.png` |
| 스크린샷 | `art/screenshots/en/*.png` (6장) |
| 페이지 배경 (선택) | `art/page_background_1438x810.png` |
| 라이브러리 캡슐 | `art/library/library_capsule_600x900.png` |
| 라이브러리 헤더 | `art/library/library_header_920x430.png` |
| 라이브러리 히어로 | `art/library/library_hero_3840x1240.png` |
| 라이브러리 로고 | `art/library/library_logo_1280x720.png` (투명 배경) |
| 커뮤니티 아이콘 | `art/community_icon_184x184.jpg` |
| 클라이언트 아이콘 (.ico) | 저장소의 `gafil.ico` |

## Steam 배포판이 기본판과 다른 점

- 구독 로그인이 없고 API 키로만 연결됩니다. 키가 없으면 Claude를 실행하지 않습니다.
- GitHub 새 버전 알림이 꺼져 있습니다 (업데이트는 Steam이 맡음).
- Windows 시작 시 자동 실행은 **꺼진 채로** 시작하고, 사용자가 메뉴에서 켜면 Steam 설치 폴더 기준으로 등록됩니다.
- 설정과 기록은 기본판과 같은 `%LOCALAPPDATA%\ClaudeDesk`를 씁니다.

## 이미지 다시 만들기

말풍선 스크린샷은 Debug 빌드의 데모 모드로 앱이 직접 그린 그림입니다 (`MainWindow.Demo.cs`, Debug에서만 들어감):

```powershell
$env:GAFIL_LANG="en"; $env:GAFIL_DEMO="chat"; $env:GAFIL_DEMO_OUT="$PWD\win_en_chat.png"; .\bin\Debug\net8.0-windows\win-x64\GAFIL.exe
```

`GAFIL_DEMO`는 `chat`, `quick`, `churu`, `intro` 중 하나입니다. 언어 두 개 × 장면 네 개의 `win_<언어>_<장면>.png`를 `art_tools/` 폴더에 두고:

```powershell
python steamrt_tools\make_art.py . steamrt
```
