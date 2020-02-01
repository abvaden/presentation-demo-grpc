#! /bin/bash

set -e

source=$(realpath $BASH_SOURCE)
scriptsDir=$(dirname $source)
sourceDir=$(dirname $scriptsDir)
rootFolder=$(dirname $sourceDir)

version="1"

if [ $1 ] 
then
    version=$1
fi
echo "Building image user-server:$version"

docker build -f "user-server/Dockerfile" -t "user-server:$version" "$rootFolder"