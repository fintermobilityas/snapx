FROM mcr.microsoft.com/dotnet/sdk:5.0.100-preview.7-focal as env-build

ENV DEBIAN_FRONTEND=noninteractive

ENV SNAPX_DOCKER_WORKING_DIR /build/snapx

RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake make gcc-8 g++-8 uuid-dev lsb-core curl && \ 
    rm -rf /var/lib/apt/lists/*

RUN \ 
	update-alternatives --install /usr/bin/gcc gcc /usr/bin/gcc-8 800 --slave /usr/bin/g++ g++ /usr/bin/g++-8

RUN \
  wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
  dpkg -i packages-microsoft-prod.deb

RUN \
  apt-get update && \
    apt-get install --no-install-recommends -y apt-transport-https && \
    rm -rf /var/lib/apt/lists/*
  
RUN \
  apt-get update && \
    apt-get install --no-install-recommends -y dotnet-sdk-2.1 && \
    rm -rf /var/lib/apt/lists/*

RUN \
  apt-get update && \
    apt-get install --no-install-recommends -y dotnet-sdk-3.1 && \
    rm -rf /var/lib/apt/lists/*

FROM env-build as env-run
RUN mkdir /root/.dotnet/tools
ENV PATH="/root/.dotnet/tools:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["sh", "-c", "(cd $SNAPX_DOCKER_WORKING_DIR && pwsh ./build.ps1 $BUILD_PS_PARAMETERS)"]
