#!/bin/bash

if [ "${SNAPX_DOCKER_HOST_OS}" == "Unix" ]; then
groupadd -r appgroup -g $SNAPX_DOCKER_GROUP_ID
useradd -u $SNAPX_DOCKER_USER_ID -r -g appgroup -d /home/$SNAPX_DOCKER_USERNAME -m -s /bin/bash -c "App user" $SNAPX_DOCKER_USERNAME
fi

export SNAPX_DOCKER_BUILD=1

if [ ! $SNAPX_DOCKER_ENTRYPOINT ]; then
    echo "[DOCKER - Error]: Entrypoint is not configured."
    exit 1
fi

if [ ! -d $SNAPX_DOCKER_WORKING_DIR ]; then
    echo "[DOCKER - Error]: Working directory does not exist: $SNAPX_DOCKER_WORKING_DIR"
    exit 1
fi

echo "[DOCKER - Info]: Executing entrypoint: $SNAPX_DOCKER_ENTRYPOINT"

case $SNAPX_DOCKER_ENTRYPOINT in
    Native)
        # void
    ;;
    *)
    echo "[DOCKER - Error]: Unknown entrypoint: $SNAPX_DOCKER_ENTRYPOINT"
    exit 1
esac

SCRIPT_ARGUMENTS="-Target $SNAPX_DOCKER_ENTRYPOINT -CIBuild $SNAPX_DOCKER_CI_BUILD -VisualStudioVersionStr $SNAPX_DOCKER_VISUAL_STUDIO_VERSION"

case $SNAPX_DOCKER_HOST_OS in
    Unix)
        exec su -m $SNAPX_DOCKER_USERNAME -c "(cd $SNAPX_DOCKER_WORKING_DIR && /usr/bin/pwsh -f build.ps1 $SCRIPT_ARGUMENTS)"
    ;;
    *)
        sh -c "(cd $SNAPX_DOCKER_WORKING_DIR && /usr/bin/pwsh -f build.ps1 $SCRIPT_ARGUMENTS)"
    ;;
esac

