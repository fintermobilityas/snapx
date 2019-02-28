echo "Installing build dependencies"

sudo apt-get update
sudo apt-get install -y --no-install-recommends build-essential pkg-config software-properties-common gcc g++ mingw-w64 g++-mingw-w64-x86-64 g++-mingw-w64-i686

echo "Installing powershell"

wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y powershell

echo "Installing .net core"

sudo apt-key adv --keyserver packages.microsoft.com --recv-keys EB3E94ADBE1229CF
sudo apt-key adv --keyserver packages.microsoft.com --recv-keys 52E16F86FEE04B979B07E28DB02C46DF417A0893
sudo rm /etc/apt/sources.list.d/dotnetdev.list
sudo sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-bionic-prod bionic main" > /etc/apt/sources.list.d/dotnetdev.list'
sudo apt-get update
sudo apt-get install -y dotnet-sdk-2.2
rm packages-microsoft-prod.deb

echo "Finished"