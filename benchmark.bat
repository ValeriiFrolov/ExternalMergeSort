@echo off
setlocal EnableDelayedExpansion

:: --- PATHS & SETTINGS ---
set DATA_FILE=data.txt
set SUMMARY_FILE=benchmark_summary.txt

:: DLL Paths
set GEN_DLL=.\TestFileGenerator\bin\Release\net8.0\TestFileGenerator.dll
set SORT_DLL=.\FileSorter\bin\Release\net8.0\FileSorter.dll

:: Test Settings
set TEST_SIZE_GB=10

echo [INFO] Initializing Benchmark Suite...

:: --- 1. FORCE REBUILD ---
echo.
echo [INFO] Building Solution (Release mode)...
echo --------------------------------------------------------
dotnet build -c Release
if !errorlevel! neq 0 (
    echo [ERROR] Build failed. Aborting benchmarks.
    pause
    exit /b
)
echo [INFO] Build successful.

:: --- 2. DATA GENERATION ---
if exist "%DATA_FILE%" (
    echo [INFO] Test file '%DATA_FILE%' found. Skipping generation.
) else (
    echo [INFO] Generating %TEST_SIZE_GB%GB test file...
    echo --------------------------------------------------------
    dotnet "%GEN_DLL%" --output "%DATA_FILE%" --size %TEST_SIZE_GB% --cores %NUMBER_OF_PROCESSORS%
    if !errorlevel! neq 0 (
        echo [ERROR] Data generation failed.
        pause
        exit /b
    )
)

:: --- PREPARE SUMMARY ---
if exist "%SUMMARY_FILE%" del "%SUMMARY_FILE%"
echo ======================================================== > "%SUMMARY_FILE%"
echo  BENCHMARK SUMMARY (Target: %TEST_SIZE_GB% GB) >> "%SUMMARY_FILE%"
echo ======================================================== >> "%SUMMARY_FILE%"

echo.
echo ========================================================
echo  STARTING BENCHMARKS
echo ========================================================

:: --- TEST SCENARIOS ---

::call :RunTest "Config A (Low RAM)" 50 true 4 4
::if !errorlevel! neq 0 exit /b

call :RunTest "Config B (Balanced)" 200 true 4 2
if !errorlevel! neq 0 exit /b

call :RunTest "Config B (Balanced)" 200 true 4 2
if !errorlevel! neq 0 exit /b

::call :RunTest "Config C (SSD)" 200 false 8 8
::if !errorlevel! neq 0 exit /b

:: call :RunTest "Config C (High RAM)" 400 true 4 2
:: if !errorlevel! neq 0 exit /b

:: --- FINAL REPORT (NO CLS) ---
echo.
echo.
echo ========================================================
echo  ALL TESTS COMPLETED
echo ========================================================
type "%SUMMARY_FILE%"
echo.
pause
exit /b

:: --- FUNCTION: RUN TEST ---
:RunTest
set TEST_NAME=%~1
set CHUNK_SIZE=%~2
set HDD_MODE=%~3
set CORES=%~4
set CHANNELS=%~5

echo.
echo --------------------------------------------------------
echo Running: %TEST_NAME%
echo [Params: Chunk=%CHUNK_SIZE%MB, HDD_Mode=%HDD_MODE%, #Cores=%CORES%, #Channels=%CHANNELS%]
echo --------------------------------------------------------

:: 1. Cleanup
if exist "result.txt" del "result.txt"
if exist "temp_chunks" rmdir /s /q "temp_chunks"
if exist "last_run_stats.txt" del "last_run_stats.txt"

:: 2. Cooldown
timeout /t 2 /nobreak >nul

:: 3. Run Sorter (DIRECT output to console for live progress)
dotnet "%SORT_DLL%" --input "%DATA_FILE%" --output "result.txt" --chunk-size %CHUNK_SIZE% --hdd-mode %HDD_MODE% --temp "temp_chunks" --cores %CORES% --channels %CHANNELS%

if !errorlevel! neq 0 (
    echo [ERROR] Test failed!
    exit /b 1
)

:: 4. Capture Stats from File
:: Format expected: TIME;RAM;SPEED
if exist "last_run_stats.txt" (
    for /f "tokens=1,2,3 delims=;" %%a in (last_run_stats.txt) do (
        set RUN_TIME=%%a
        set RUN_RAM=%%b
        set RUN_SPEED=%%c
    )
) else (
    set RUN_TIME=Error
    set RUN_RAM=0
    set RUN_SPEED=0
)

:: 5. Add to Summary
echo %TEST_NAME%: >> "%SUMMARY_FILE%"
echo   - Time:  !RUN_TIME! >> "%SUMMARY_FILE%"
echo   - RAM:   !RUN_RAM! MB >> "%SUMMARY_FILE%"
echo   - Speed: !RUN_SPEED! MB/s >> "%SUMMARY_FILE%"
echo. >> "%SUMMARY_FILE%"

exit /b 0