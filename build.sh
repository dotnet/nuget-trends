#!/bin/bash
set -e

export root=$(pwd)
# SPA
pushd src/NuGetTrends.Spa

yarn install
yarn prod

cp -r dist/nuget-trends/* $root/src/NuGetTrends.Api/wwwroot
popd

# API
pushd src/NuGetTrends.Api

export framework=netcoreapp2.1
dotnet publish -c Release -f $framework

pushd bin/Release/$framework/publish/
tar -zcvf $root/nuget-trends-web.tar.gz .
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
