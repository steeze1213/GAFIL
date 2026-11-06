@echo off
REM 이 파일은 cmd가 한글을 읽을 수 있도록 CP949(ANSI)로 저장함
REM GAFIL 설치: GAFIL.exe를 %LOCALAPPDATA%\Programs\GAFIL\ 에 복사하고 실행
REM 실행하면 GAFIL이 스스로 Windows 시작 시 자동 실행(HKCU Run)에 등록함
REM 먼저 빌드: dotnet publish -c Release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
setlocal
REM exe 찾기: 빌드한 경우 dist\GAFIL.exe, Releases에서 받은 경우 이 파일 옆의 GAFIL.exe
set "SRC=%~dp0dist\GAFIL.exe"
if not exist "%SRC%" set "SRC=%~dp0GAFIL.exe"
set "DEST=%LOCALAPPDATA%\Programs\GAFIL"

if not exist "%SRC%" (
  echo [GAFIL] GAFIL.exe 를 찾지 못했어요. install.cmd 옆에 GAFIL.exe 를 두거나, dotnet publish 로 dist 폴더에 빌드하세요.
  pause
  exit /b 1
)

REM 실행 중인 GAFIL을 끄고 (파일이 잠겨 있으면 복사가 안 됨) 잠깐 기다림
taskkill /im GAFIL.exe /f >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%" "%DEST%\GAFIL.exe" >nul || (
  echo [GAFIL] 복사 실패
  pause
  exit /b 1
)

start "" "%DEST%\GAFIL.exe"
echo [GAFIL] 설치 완료: %DEST%\GAFIL.exe
endlocal
