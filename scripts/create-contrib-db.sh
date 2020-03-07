#!/bin/bash

# This script takes in a DB dump, restores it into a new container with a randomly chosen DB name.
# Delete download information older then a year and creates a new compressed DB dump.

set -euxo pipefail

if [ -z "$1" ]
  then
    echo "Please provide the path to the full NuGet Trends DB backup."
    echo "Example ./$0 nugettrends-2019-08-03.dump"
fi

IN_FILE=$1

export PGHOST=${PGHOST:-127.0.0.1}
export PGPORT=${PGPORT:-63435}
export PGDATABASE=$(cat /dev/urandom | env LC_CTYPE=C tr -dc 'a-zA-Z0-9' | fold -w 32 | head -n 1)
export PGUSER=${PGUSER:-postgres}
export PGPASSWORD=${PGPASSWORD:-PUg2rt6Pp8Arx7Z9FbgJLFvxEL7pZ2}
{
    DATA_FROM=$(date --date="-2 months" +%Y-%m-%d)
} || {
    # macOS
    DATA_FROM=$(date -v-2m +%Y-%m-%d)
}

OUT_FILE="nuget-trends-contrib.dump"

docker run -p $PGHOST:$PGPORT:5432/tcp \
    --name $PGDATABASE \
    -e POSTGRES_PASSWORD=$PGPASSWORD \
    -d postgres:10.5
trap 'docker rm -v --force $PGDATABASE' EXIT

# Wait DB boot
counter=0
{
  while ! echo -n > /dev/tcp/localhost/$PGPORT; do
    if [ "$counter" -gt "10" ]; then
        echo Done waiting!
        exit -1
    fi
    echo Waiting for 1 second.
    sleep 1
    counter=$((counter+1))
  done
} 2>/dev/null

# Even after bound to socket, server fails to process the first couple seconds?
sleep 5

# Commands use $PGPASSWORD env var automatically
createdb $PGDATABASE -h $PGHOST -p $PGPORT -U $PGUSER

{
    CPU_COUNT=$(cat /proc/cpuinfo | awk '/^processor/{print $3}' | wc -l)
} || {
    CPU_COUNT=4
}

# prepare for restore
psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE  -c "CREATE ROLE datadog;"

pg_restore -j $CPU_COUNT -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE $IN_FILE

# full DB in place. Modify as needed:
psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE  -c "DELETE FROM daily_downloads WHERE date < '$DATA_FROM';"
psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE  -c "VACUUM (VERBOSE, ANALYZE);"

# create new compressed backup
pg_dump -Fc -C -h $PGHOST -p $PGPORT -U $PGUSER $PGDATABASE > $OUT_FILE
