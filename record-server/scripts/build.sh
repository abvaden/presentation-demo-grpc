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
echo "Building image voting-record-server:$version"

$scriptsDir/codegen.sh

docker build -f "$rootFolder/record-server/server/Dockerfile" -t "voting-record-server:$version" "$rootFolder"