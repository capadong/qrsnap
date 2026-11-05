@echo off
rem QrSnap C++ build script (MSVC x64, static CRT, single-file exe)
rem NOTE: pure ASCII only -- cmd.exe reads this file in the OEM codepage.
setlocal

set "MSVC=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231"
set "SDK=C:\Program Files (x86)\Windows Kits\10"
set "SDKVER=10.0.26100.0"

set "PATH=%MSVC%\bin\Hostx64\x64;%SDK%\bin\%SDKVER%\x64;%PATH%"
set "INCLUDE=%MSVC%\include;%SDK%\Include\%SDKVER%\ucrt;%SDK%\Include\%SDKVER%\um;%SDK%\Include\%SDKVER%\shared;%SDK%\Include\%SDKVER%\winrt"
set "LIB=%MSVC%\lib\x64;%SDK%\Lib\%SDKVER%\ucrt\x64;%SDK%\Lib\%SDKVER%\um\x64"

cd /d "%~dp0"

rc /nologo /fo app.res app.rc
if errorlevel 1 (echo [ERROR] rc failed & exit /b 1)

cl /nologo /utf-8 /std:c++17 /O2 /MT /EHsc /W3 /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 src\*.cpp quirc\*.c app.res /Fe:QrSnap.exe /link /SUBSYSTEM:WINDOWS
if errorlevel 1 (echo [ERROR] cl failed & exit /b 1)

echo.
echo Build OK
dir QrSnap.exe | find "QrSnap.exe"
endlocal
