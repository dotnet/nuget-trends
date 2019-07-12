#!/bin/bash
set -e

export root=$(pwd)
# SPA
pushd src/NuGetTrends.Spa

yarn install
yarn prod

pushd dist/nuget-trends/
tar -zcvf $root/nuget-trends-spa.tar.gz .
popd
popd

# API
pushd src/NuGetTrends.Api

dotnet publish -c Release -o nuget-trends-api

pushd nuget-trends-api
tar -zcvf $root/nuget-trends-api.tar.gz .
popd
popd

# Scheduler
pushd src/NuGetTrends.Scheduler

dotnet publish -c Release -o nuget-trends-worker

pushd nuget-trends-worker
tar -zcvf $root/nuget-trends-worker.tar.gz .
popd
popd
