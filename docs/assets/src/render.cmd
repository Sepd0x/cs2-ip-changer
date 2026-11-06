@echo off
rem Rebuilds every README image. Needs Node and Google Chrome. Run after changing anything in this folder.
rem   components.mjs  -> download.svg, how-it-works-*.svg
rem   banner.html     -> banner.png (2x for sharp text)
rem   social-preview.html -> social-preview.png (upload it by hand: Settings > General > Social preview)
rem   the logo (logo.svg, icon.png, app.ico, favicons) comes from node tools/logo.mjs, not from here
node "%~dp0components.mjs"
set CHROME="%ProgramFiles%\Google\Chrome\Application\chrome.exe"
set OPTS=--headless=new --hide-scrollbars --disable-lcd-text --allow-file-access-from-files
%CHROME% %OPTS% --force-device-scale-factor=2 --default-background-color=00000000 --window-size=1280,260 --screenshot="%~dp0..\banner.png" "file:///%~dp0banner.html"
%CHROME% %OPTS% --force-device-scale-factor=1 --window-size=1280,640 --screenshot="%~dp0..\social-preview.png" "file:///%~dp0social-preview.html"
