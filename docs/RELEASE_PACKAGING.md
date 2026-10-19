# 로컬 Release 패키징

.NET 10 SDK와 Python 3이 필요하다. 저장소 루트에서 실행한다. Windows 앱 실행은 Windows에서만 가능하며 macOS에서는 Windows 대상으로 교차 publish한다.

```sh
python3 tools/package_release.py --dotnet /path/to/dotnet --version 4.0.0
```

PATH에 SDK가 있으면 `--dotnet`을 생략한다. Windows에서는 `python tools/package_release.py --version 4.0.0`도 가능하다. 테스트의 디렉터리 심볼릭 링크 검증은 Windows 개발자 모드 또는 링크 생성 권한이 필요하다.

스크립트는 C# 검사와 Python 샘플 검사를 먼저 실행한다. 이후 Release·win-x64·self-contained·single-file로 publish하며 ReadyToRun=false를 명시해 호스트별 crossgen 차이를 피한다. trimming은 비활성 상태를 유지한다. Visual Studio FolderProfile은 Windows 전용 ReadyToRun 설정이므로 이 스크립트와 별도다.

결과는 git에서 제외된 `artifacts/`에 생성된다.

- `where-am-i-버전-win-x64.zip`: 실행 파일·사용 가이드·이미지·변경 내용·Windows 검증표·manifest.
- 같은 이름의 `.zip.sha256`: ZIP 해시.
- 같은 이름의 `.manifest.json`: 파일별 SHA-256·버전·빌드 설정·미검증 항목.

원본 로그·테스트 샘플·사용자 설정·이메일·디버그 심볼은 패키지에 포함하지 않는다. 패키지 스크립트는 앱 실행·운영 API 호출·서명·외부 업로드·공개 배포를 수행하지 않는다. 동일 버전으로 실행하면 로컬 결과를 교체한다. 실제 배포에는 새 버전을 지정하고 [Windows 검증표](WINDOWS_VALIDATION.md)를 확인한다.

같은 소스를 이번 작업에서 이미 검증했다면 `--skip-tests`로 테스트 반복을 생략할 수 있다. 기본 실행은 테스트를 생략하지 않는다. manifest에 패키징 명령의 테스트 실행 여부를 기록한다.
