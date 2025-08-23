# Snapx Development Instructions

Snapx is a powerful cross-platform .NET application deployment tool with built-in support for delta updates, release channels (test, staging, production), and automatic deployment using GitHub Actions. It includes both .NET C# components and native C++ components.

**Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.**

## Working Effectively

### Bootstrap and Build Process
Follow these steps in exact order:

1. **Install Dependencies (Linux)**:
   - Docker: `which docker` (should show /usr/bin/docker)
   - GitVersion: `dotnet tool update gitversion.tool -g`
   - PowerShell v7: `pwsh --version` (should show 7.x)
   - .NET 9.0 SDK: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version latest --channel 9.0`
   - Add to PATH: `export PATH="$HOME/.dotnet:$PATH"`
   - Native tools: cmake, make, gcc, g++ (usually pre-installed)

2. **Initialize Repository**:
   - Unshallow git repo: `git fetch --unshallow` (required for GitVersion)
   - Initialize submodules: `git submodule update --init --recursive` (required for native dependencies)

3. **Build Native Components** (takes ~25 seconds - NEVER CANCEL, set timeout to 60+ seconds):
   ```bash
   export PATH="$HOME/.dotnet:$PATH"
   pwsh ./bootstrap.ps1 -Target Native -Configuration Debug -NetCoreAppVersion net9.0 -Version "1.0.0-dev" -Rid linux-x64
   ```

4. **Build .NET Components** (takes ~15 seconds - NEVER CANCEL, set timeout to 30+ seconds):
   ```bash
   export PATH="$HOME/.dotnet:$PATH"
   cd src && dotnet build --configuration Debug
   ```

5. **Build Individual Components**:
   - Snap.Installer: `pwsh ./bootstrap.ps1 -Target Snap-Installer -Configuration Debug -NetCoreAppVersion net9.0 -Version "1.0.0-dev" -Rid linux-x64` (~30 seconds)
   - Snap: `pwsh ./bootstrap.ps1 -Target Snap -Configuration Debug -NetCoreAppVersion net9.0 -Version "1.0.0-dev" -Rid linux-x64` (~5 seconds)
   - Snapx (partial): `dotnet build Snapx/Snapx.csproj --configuration Debug /p:SnapBootstrap=true` (~3 seconds)

### Run Unit Tests
- Command: `pwsh ./bootstrap.ps1 -Target Run-Dotnet-UnitTests -Configuration Debug -NetCoreAppVersion net9.0 -Version "1.0.0-dev" -Rid linux-x64`
- Expected time: ~35 seconds - NEVER CANCEL, set timeout to 60+ seconds
- Test results: 213 total tests, expect 5 network-related failures in sandboxed environments (normal)

### Run the Application
- Build with SnapBootstrap flag: `dotnet build src/Snapx/Snapx.csproj --configuration Debug /p:SnapBootstrap=true`
- Run: `dotnet src/Snapx/bin/Debug/net9.0/snapx.dll --help`
- Available commands: demote, promote, pack, sha256, rcedit, list, restore, lock, help, version

## Validation

### Manual Validation Steps
After making changes, always perform these validation steps:

1. **Build Validation**: Run the complete build process and ensure all components build without errors
2. **Test Execution**: Run unit tests to verify no regressions in core functionality
3. **CLI Validation**: Run `dotnet src/Snapx/bin/Debug/net9.0/snapx.dll --help` and verify tool functionality
4. **Code Style**: The codebase uses standard C# and C++ conventions - follow existing patterns

### Cross-Platform Build Considerations
- **Linux x64**: Fully supported with native builds working
- **Linux arm64**: Requires cross-compilation or arm64 environment
- **Windows**: Requires Visual Studio 2022 Community Edition with C++ workload
- **Docker**: Available but requires network access for package downloads

## Critical Build Information

### Timing Expectations and Timeout Values
- **Native build**: 25 seconds (set timeout to 60+ seconds)
- **Full .NET build**: 15 seconds (set timeout to 30+ seconds)
- **Unit tests**: 35 seconds (set timeout to 60+ seconds)
- **Submodule init**: 5 seconds (set timeout to 30+ seconds)
- **Individual component builds**: 3-30 seconds depending on component

### Build Failures and Solutions
- **Missing native dependencies**: Run `git submodule update --init --recursive`
- **GitVersion errors**: Run `git fetch --unshallow` to get full git history
- **C++ compilation errors**: Ensure all vendor submodules are initialized
- **Missing .NET 9.0**: Install with Microsoft's dotnet-install script
- **Cross-platform installer issues**: Use `/p:SnapBootstrap=true` for partial builds

### Known Limitations
- Full Snapx tool requires all platform installers (win-x86, win-x64, linux-x64, linux-arm64)
- Network-dependent tests fail in offline/sandboxed environments
- Docker builds may fail due to network restrictions
- GitVersion requires full git history (not shallow clones)

## Project Structure

### Key Components
- **Snap**: Core library (.NET 8.0/9.0) - main deployment logic
- **Snapx**: CLI tool (.NET 9.0) - command-line interface
- **Snap.Installer**: GUI installer (.NET 9.0 with Avalonia) - user installation interface
- **Snap.CoreRun**: Native C++ component - application launcher
- **Snap.CoreRun.Pal**: Native C++ platform abstraction layer
- **Snap.Bsdiff**: Native C++ binary diff library

### Build Outputs
- Native binaries: `/build/native/Unix/linux-x64/Debug/`
- .NET assemblies: `src/*/bin/Debug/net9.0/`
- Packaged installers: `/build/dotnet/linux-x64/Snap.Installer/net9.0/Debug/publish/`
- NuGet packages: `/nupkgs/`

### Test Projects
- **Snap.Tests**: Core library tests
- **Snap.Shared.Tests**: Shared test utilities
- **Snap.Installer.Tests**: Installer component tests
- **Snapx.Tests**: CLI tool tests

## Common Tasks

### Working with Dependencies
- Always use `export PATH="$HOME/.dotnet:$PATH"` before running dotnet commands
- Submodules include: bsdiff, cxxopts, gtest, nanoid_cpp, plog
- Native dependencies built via CMake with Unix Makefiles generator

### Version Management
- GitVersion used for automatic versioning
- Requires full git history and proper branch setup
- Development builds use format: "1.0.0-dev"

### Docker Usage
- Available for cross-platform builds but requires network access
- Local Docker builds: use `-DockerLocal` flag
- May fail in network-restricted environments

Remember: **NEVER CANCEL LONG-RUNNING BUILDS** - they are normal and expected. Use the timeout values specified above.