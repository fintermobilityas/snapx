# LibLog to Microsoft.Extensions.Logging Migration

This document describes the replacement of LibLog (which is deprecated) with a compatible logging system that maintains the same API.

## What Changed

### Removed Dependencies
- **LibLog** package has been completely removed from the project

### New Logging System
- Created `Snap.Logging` namespace with full API compatibility
- Implemented all LibLog interfaces and classes:
  - `ILog` - Main logging interface
  - `LogProvider` - Static factory for loggers
  - `LogLevel` - Enum with same values as LibLog
  - `LogExtensions` - All extension methods (Debug, Info, Warn, Error, etc.)
  - `ILogProvider` - Interface for log providers

### Log Provider Support
The new system maintains support for external logging frameworks:
- **NLog** - Auto-detected if NLog assembly is present
- **Serilog** - Auto-detected if Serilog assembly is present  
- **Log4Net** - Auto-detected if log4net assembly is present
- **Loupe** - Auto-detected if Gibraltar.Agent assembly is present
- **ColoredConsoleLogProvider** - Built-in console logger with colors

## Usage (No Changes Required)

The API is 100% backward compatible. Existing code continues to work without changes:

```csharp
// Get a logger (unchanged)
var logger = LogProvider.For<MyClass>();

// Use extension methods (unchanged)
logger.Info("Information message");
logger.Debug("Debug message");
logger.ErrorException("Error occurred", exception);

// Set log provider (unchanged)
LogProvider.SetCurrentLogProvider(new ColoredConsoleLogProvider(LogLevel.Debug));
```

## Future Integration with Microsoft.Extensions.Logging

The system is designed to be extended with Microsoft.Extensions.Logging integration in the future while maintaining backward compatibility.

## Benefits

1. **No Breaking Changes** - Existing code works without modification
2. **Removed Deprecated Dependency** - No longer depends on the deprecated LibLog
3. **Maintained Functionality** - All logging features continue to work
4. **Future Ready** - Can be extended with MEL integration later
5. **Same Performance** - No performance impact compared to LibLog