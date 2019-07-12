#!/bin/bash
set -e

export root=$(pwd)
# SPA
pushd src/NuGetTrends.Spa
rm -rf dist/nuget-trends/

yarn install
yarn prod

pushd dist/nuget-trends/
tar -zcvf $root/nuget-trends-spa.tar.gz .
popd
popd

# API
pushd src/NuGetTrends.Api
rm -rf nuget-trends-api

dotnet publish -c Release -o nuget-trends-api

pushd nuget-trends-api
tar -zcvf $root/nuget-trends-api.tar.gz .
popd
popd

# Scheduler
pushd src/NuGetTrends.Scheduler
rm -rf nuget-trends-worker

dotnet publish -c Release -o nuget-trends-worker

pushd nuget-trends-worker
tar -zcvf $root/nuget-trends-worker.tar.gz .
popd
popd
