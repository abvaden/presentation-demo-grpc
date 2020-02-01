#! /bin/bash

set -e

source=$(realpath $BASH_SOURCE)
scriptsDir=$(dirname $source)
sourceDir=$(dirname $scriptsDir)
rootFolder=$(dirname $sourceDir)

protosFolder=$(realpath $rootFolder/protos)
destFolder=$(realpath $sourceDir/server/voting)
$rootFolder/code-generator/run.sh $protosFolder $destFolder "go"