@echo off
rem One-click upload of this folder to https://github.com/Maxxlus/dinisland_for_win
cd /d "%~dp0"
where git >nul 2>nul
if errorlevel 1 (
  echo Git is not installed. Download it from https://git-scm.com/download/win and run this file again.
  pause
  exit /b 1
)
if not exist .git git init
set HAVE=
for /f "delims=" %%i in ('git config user.email') do set HAVE=%%i
if not defined HAVE (
  git config user.name "Maxxlus"
  git config user.email "Maxxlus@users.noreply.github.com"
)
git remote remove origin >nul 2>nul
git remote add origin https://github.com/Maxxlus/dinisland_for_win.git
git add -A
git commit -m "Dynamic Island for Windows"
git branch -M main
git pull origin main --allow-unrelated-histories --no-edit >nul 2>nul
git push -u origin main
echo.
echo If a browser window asked you to sign in to GitHub - that is normal, sign in and run this file once more.
pause
