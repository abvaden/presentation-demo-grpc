#! /bin/bash

set -e

source=$(realpath $BASH_SOURCE)
rootDir=$(dirname $source)


echo "Building user-server"
$rootDir/user-server/scripts/build.sh $1

echo "Building record-server"
$rootDir/record-server/scripts/build.sh $1

echo "Building poll-summary-server"
$rootDir/poll-summary-server/scripts/build.sh $1