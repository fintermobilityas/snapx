#!/bin/bash


UBUNTU_VERSION=`lsb_release --release | awk -F ':' 'gsub(/^[ \t]+/,"",$2)'`
DISTRO_NAME="Unknown"
DISTRO_VERSION="Unknown"
INSTALL_CMAKE=1
WORKING_DIR=`pwd`
CMAKE_ROOT_DIR=$WORKING_DIR/cmake
CMAKE_INSTALL_DIR=$CMAKE_ROOT_DIR/cmake-x-xx

case "$UBUNTU_VERSION" in
"Release 18.04" )
	DISTRO_NAME="precise" 
	DISTRO_VERSION="18.04"
;;
*)
	(>&2 echo "Unsupported distro version: $UbuntuVersion")
	exit 1 
;;
esac

echo "Installing build dependencies"
echo "Distro: Ubuntu $UBUNTU_VERSION"

sudo apt-get update && apt-get install -y --no-install-recommends build-essential pkg-config software-properties-common gcc g++ mingw-w64 g++-mingw-w64-x86-64 g++-mingw-w64-i686 uuid-dev

if [ $INSTALL_CMAKE -eq 1 ]
then
	
	if [ ! -d "$CMAKE_INSTALL_DIR" ]; then
		echo "Downloading cmake"
		rm -rf $CMAKE_INSTALL_DIR
		mkdir -p $CMAKE_INSTALL_DIR
		cd $CMAKE_ROOT_DIR
		wget -qO- "https://cmake.org/files/v3.13/cmake-3.13.4-Linux-x86_64.tar.gz" | tar --strip-components=1 -xz -C cmake-x-xx
		cd $WORKING_DIR
		echo "Successfully downloaded cmake"
	fi
fi

echo "Installing powershell"

wget -q https://packages.microsoft.com/config/ubuntu/${DISTRO_VERSION}/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update && apt-get install -y --no-install-recommends powershell

echo "Installing .net core"

sudo apt-key adv --keyserver packages.microsoft.com --recv-keys EB3E94ADBE1229CF
sudo apt-key adv --keyserver packages.microsoft.com --recv-keys 52E16F86FEE04B979B07E28DB02C46DF417A0893
sudo rm /etc/apt/sources.list.d/dotnetdev.list
sudo sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-'"$DISTRO_NAME"'-prod '"$DISTRO_NAME"' main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-get update
sudo apt-get install -y dotnet-sdk-2.2
rm packages-microsoft-prod.deb

echo "Finished"
