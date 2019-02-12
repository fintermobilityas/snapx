REM THIS IS JUST A TEST

dotnet publish src/Snap.Installer/Snap.Installer.csproj -c release -r win-x64 -f netcoreapp2.2 /p:ShowLinkerSizeComparison=true
snapx rcedit --gui-app -f src/Snap.Installer/bin/release/netcoreapp2.2/win-x64/publish/Snap.Installer.exe
.\tools\warp-packer.exe --arch windows-x64 --exec Snap.Installer.exe --output src/Snap.Installer/bin/release/netcoreapp2.2/win-x64/publish/Setup.exe --input_dir src/Snap.Installer/bin/release/netcoreapp2.2/win-x64/publish
.\tools\upx.exe --ultra-brute src/Snap.Installer/bin/release/netcoreapp2.2/win-x64/publish/Setup.exe