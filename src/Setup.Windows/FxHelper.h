#pragma once

enum class NetCoreAppVersion {netcoreapp20 = 0, netcoreapp21 = 1, netcoreapp22 = 2};

class CFxHelper
{
public:
	static NetCoreAppVersion GetRequiredDotNetCoreVersion();
	static bool CanInstallDotNetCore();
	static bool IsDotNetCoreInstalled(NetCoreAppVersion requiredVersion);
	static HRESULT InstallDotNetCore(NetCoreAppVersion version, bool isQuiet);
private:
	static HRESULT HandleRebootRequirement(bool isQuiet);
	static bool WriteRunOnceEntry();
	static bool RebootSystem();
	static int GetDotNetCoreVersionReleaseNumber(NetCoreAppVersion version);
	static UINT GetInstallerUrlForVersion(NetCoreAppVersion version);
	static UINT GetInstallerMainInstructionForVersion(NetCoreAppVersion version);
	static UINT GetInstallerContentForVersion(NetCoreAppVersion version);
	static UINT GetInstallerExpandedInfoForVersion(NetCoreAppVersion version);
};

