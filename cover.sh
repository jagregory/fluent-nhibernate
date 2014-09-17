#!/bin/bash
cd coverage/settings

TC_DOTCOVER_PATH=${1:-"/c/TeamCity/buildAgent/tools/dotCover/dotCover.exe"}
echo Using dotCover from ${TC_DOTCOVER_PATH}

echo Running coverage...
${TC_DOTCOVER_PATH} cover nunit-coverage.xml
${TC_DOTCOVER_PATH} cover mspec-coverage.xml

echo Producing xml report...
${TC_DOTCOVER_PATH} merge merge-coverage.xml
${TC_DOTCOVER_PATH} report reporting.xml

echo Done.