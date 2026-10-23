#!/bin/sh
# Explicit deployment step: creates the evidence bucket, the least-privilege application identity and the
# readiness sentinel. Runs with the object-storage admin boundary (weed shell), never inside the web container.
# Idempotent: re-running recreates the application identity and rewrites the sentinel.
set -eu

bucket="${OBJECT_STORAGE_BUCKET:-carbon-evidence}"
master="${OBJECT_STORAGE_MASTER:-object-storage:9333}"
filer="${OBJECT_STORAGE_FILER:-object-storage:8888}"
app_user="${OBJECT_STORAGE_APP_USER:-carbon_app}"
: "${OBJECT_STORAGE_APP_ACCESS_KEY:?OBJECT_STORAGE_APP_ACCESS_KEY is required}"
: "${OBJECT_STORAGE_APP_SECRET_KEY:?OBJECT_STORAGE_APP_SECRET_KEY is required}"

shell() { printf '%s\n' "$1" | weed shell -master="$master" -filer="$filer" 2>&1; }

attempt=0
until shell "cluster.status" >/dev/null; do
  attempt=$((attempt + 1))
  [ "$attempt" -lt 30 ] || { echo "object storage master $master is not reachable" >&2; exit 1; }
  sleep 2
done

# The bucket may already exist (S3_BUCKET bootstrap or an earlier run).
shell "s3.bucket.create -name $bucket" >/dev/null || true
shell "s3.configure -user=$app_user -delete -apply" >/dev/null || true
shell "s3.configure -user=$app_user -access_key=$OBJECT_STORAGE_APP_ACCESS_KEY -secret_key=$OBJECT_STORAGE_APP_SECRET_KEY -actions=Write:$bucket/organizations/*,Read:$bucket/organizations/*,Read:$bucket/system/readiness-sentinel* -apply" >/dev/null

sentinel="$(mktemp)"
printf 'carbon-balance readiness sentinel\n' > "$sentinel"
cp "$sentinel" /tmp/readiness-sentinel
weed filer.copy /tmp/readiness-sentinel "http://$filer/buckets/$bucket/system/"
echo "object storage provisioned: bucket=$bucket identity=$app_user"
