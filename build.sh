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

export framework=netcoreapp2.1
dotnet publish -c Release -f $framework

pushd bin/Release/$framework/publish/
tar -zcvf $root/nuget-trends-api.tar.gz .
popd
popd

# Scheduler
pushd src/NuGetTrends.Scheduler

export framework=netcoreapp2.1
dotnet publish -c Release -f $framework

pushd bin/Release/$framework/publish/
tar -zcvf $root/nuget-trends-worker.tar.gz .
popd
popd
