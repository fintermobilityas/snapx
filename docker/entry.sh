#!/bin/bash

groupadd -r appgroup -g $SNAPX_DOCKER_GROUP_ID
useradd -u $SNAPX_DOCKER_USER_ID -r -g appgroup -d /home/$SNAPX_DOCKER_USERNAME -m -s /bin/bash -c "App user" $SNAPX_DOCKER_USERNAME

export SNAPX_DOCKER_BUILD=1

exec su -m $SNAPX_DOCKER_USERNAME -c "(cd /build/snapx && /usr/bin/pwsh -f build.ps1 -Target Bootstrap)"