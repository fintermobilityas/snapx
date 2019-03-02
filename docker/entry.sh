#!/bin/bash

groupadd -r appgroup -g $SNAPX_DOCKER_GROUP_ID
useradd -u $SNAPX_DOCKER_USER_ID -r -g appgroup -d /home/$SNAPX_DOCKER_USERNAME -m -s /bin/bash -c "App user" $SNAPX_DOCKER_USERNAME

export SNAPX_DOCKER_BUILD=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_XMLDOC_MODE="skip"

if [ ! $SNAPX_DOCKER_ENTRYPOINT ]; then
    echo "Error: Entrypoint is not configured."
fi

echo "[DOCKER]: Exeucting entrypoint: $SNAPX_DOCKER_ENTRYPOINT"

case $SNAPX_DOCKER_ENTRYPOINT in
    Native)
        # void
    ;;
    *)
    echo "[DOCKER]: Unknown entrypoint: $SNAPX_DOCKER_ENTRYPOINT"
    exit 1
esac

exec su -m $SNAPX_DOCKER_USERNAME -c "(cd /build/snapx && /usr/bin/pwsh -f build.ps1 -Target $SNAPX_DOCKER_ENTRYPOINT)"