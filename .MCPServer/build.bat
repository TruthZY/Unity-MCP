@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

echo ==========================================
echo Unity MCP Server Build Script
echo ==========================================
echo.

REM Get script directory
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

REM Enter McpServer directory where package.json is located
cd McpServer
if errorlevel 1 (
    echo [ERROR] McpServer directory not found
    pause
    exit /b 1
)

echo [1/5] Checking Node.js...
node --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Node.js not found in PATH
    echo Please install Node.js from https://nodejs.org/
    pause
    exit /b 1
)
for /f "tokens=*" %%a in ('node --version') do echo   Found: %%a

echo.
echo [2/5] Cleaning old build...
if exist "dist" (
    rmdir /s /q "dist"
    echo   Cleaned dist folder
)

echo.
echo [3/5] Installing dependencies...
call npm install
if errorlevel 1 (
    echo [ERROR] npm install failed
    pause
    exit /b 1
)

echo.
echo [4/5] Compiling TypeScript...
call npm run build
if errorlevel 1 (
    echo [ERROR] Build failed
    pause
    exit /b 1
)

echo.
echo [5/5] Verifying build...
if not exist "dist\index.js" (
    echo [ERROR] Output not found: dist\index.js
    pause
    exit /b 1
)
echo   OK: dist\index.js generated

echo.
echo ==========================================
echo Build Success!
echo ==========================================
echo.
echo Output: %SCRIPT_DIR%McpServer\dist\index.js
echo.
echo To configure Claude Desktop, add this to:
echo %%APPDATA%%\Claude\claude_desktop_config.json
echo.
echo {
echo   "mcpServers": {
echo     "unity": {
echo       "command": "node",
echo       "args": ["%SCRIPT_DIR%McpServer\dist\index.js"]
echo     }
echo   }
echo }
echo.
pause
